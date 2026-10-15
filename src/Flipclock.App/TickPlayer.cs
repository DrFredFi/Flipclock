using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Flipclock.App;

/// <summary>
/// Plays the tick with low latency. The sound is decoded once into memory and mixed into an
/// always-open WASAPI stream, so a tick starts within a few milliseconds of being requested.
/// </summary>
public sealed class TickPlayer : IDisposable
{
    const int SampleRate = 44100;
    static readonly WaveFormat MixFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    readonly MixingSampleProvider _mixer = new(MixFormat) { ReadFully = true };
    WasapiOut? _output;
    float[] _samples = Synthesize();

    public TickPlayer()
    {
        try
        {
            _output = new WasapiOut(AudioClientShareMode.Shared, true, 40);
            _output.Init(_mixer);
            _output.Play();
        }
        catch (Exception)
        {
            // No audio device: ticks are silently skipped.
            _output?.Dispose();
            _output = null;
        }
    }

    /// <summary>Loads a custom sound, or the built-in tick when the path is empty. Returns false if the file could not be used.</summary>
    public bool Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _samples = Synthesize();
            return true;
        }
        try
        {
            using var reader = new MediaFoundationReader(path);
            ISampleProvider provider = reader.ToSampleProvider();
            if (provider.WaveFormat.Channels == 1) provider = new MonoToStereoSampleProvider(provider);
            else if (provider.WaveFormat.Channels > 2) throw new NotSupportedException("Only mono or stereo sounds are supported.");
            if (provider.WaveFormat.SampleRate != SampleRate) provider = new WdlResamplingSampleProvider(provider, SampleRate);

            // A tick should be short; cap at two seconds.
            var buffer = new float[SampleRate * 2 * 2];
            int total = 0, read;
            while (total < buffer.Length && (read = provider.Read(buffer, total, buffer.Length - total)) > 0)
                total += read;
            if (total == 0) throw new InvalidDataException("Empty sound.");
            _samples = buffer[..total];
            return true;
        }
        catch (Exception)
        {
            _samples = Synthesize();
            return false;
        }
    }

    public void Play(double volume)
    {
        if (_output is null || volume <= 0) return;
        var input = new VolumeSampleProvider(new BufferSampleProvider(_samples)) { Volume = (float)Math.Clamp(volume, 0, 1) };
        _mixer.AddMixerInput(input);
    }

    /// <summary>The built-in tick: a short mechanical click (a noise transient plus two damped resonances).</summary>
    static float[] Synthesize()
    {
        int length = (int)(SampleRate * 0.06);
        var data = new float[length * 2];
        var rng = new Random(7);
        for (int i = 0; i < length; i++)
        {
            double t = (double)i / SampleRate;
            double noise = (rng.NextDouble() * 2 - 1) * Math.Exp(-t * 900);
            double body = Math.Sin(2 * Math.PI * 2300 * t) * Math.Exp(-t * 160) * 0.55
                        + Math.Sin(2 * Math.PI * 820 * t) * Math.Exp(-t * 90) * 0.35;
            float s = (float)((noise * 0.8 + body) * 0.7);
            data[i * 2] = s;
            data[i * 2 + 1] = s;
        }
        return data;
    }

    public void Dispose()
    {
        _output?.Stop();
        _output?.Dispose();
        _output = null;
    }

    sealed class BufferSampleProvider(float[] samples) : ISampleProvider
    {
        int _position;
        public WaveFormat WaveFormat => MixFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, samples.Length - _position);
            if (n <= 0) return 0;
            Array.Copy(samples, _position, buffer, offset, n);
            _position += n;
            return n;
        }
    }
}
