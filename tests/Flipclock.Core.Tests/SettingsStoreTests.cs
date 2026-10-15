using Flipclock.Core.Settings;

namespace Flipclock.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "flipclock-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_yields_defaults()
    {
        var result = new SettingsStore(_dir).Load();
        Assert.False(result.FileExisted);
        Assert.Equal(250, result.Settings.Margin);
        Assert.Equal(DateLanguage.Italian, result.Settings.DateLanguage);
    }

    [Fact]
    public void Round_trips_every_setting()
    {
        var s = new ClockSettings
        {
            SecondsScale = 0.55, Margin = 120, TileColor = new RgbColor(1, 2, 3), DigitColor = new RgbColor(250, 251, 252),
            DateColor = new RgbColor(9, 8, 7), BackgroundColor = new RgbColor(100, 110, 120),
            DigitFontFamily = "Bahnschrift", DigitFontWeight = 600, DateFontFamily = "Georgia", DateFontWeight = 300,
            LightAngle = 45, LightIntensity = 1.5, Glossiness = 0.9, FlipDurationMs = 700, CascadeDelayMs = 0, Bounce = 0,
            SoundEnabled = false, Volume = 0.25, SoundFile = @"C:\Sounds\tick ""loud"".mp3",
            BackgroundImage = @"D:\Pictures\sky.jpg", BackgroundDim = 0.4,
            DateVisible = false, DatePosition = DatePosition.Right, DateSize = 1.75, DateLanguage = DateLanguage.Spanish,
            WindowX = -1500, WindowY = 40, WindowWidth = 900, WindowHeight = 600,
            Maximized = true, Fullscreen = true, Borderless = true, AlwaysOnTop = true,
        };
        var store = new SettingsStore(_dir);
        Assert.True(store.TrySave(s));

        var loaded = store.Load();
        Assert.Empty(loaded.RejectedKeys);
        Assert.False(loaded.WasCorrupt);
        foreach (var p in typeof(ClockSettings).GetProperties())
            Assert.Equal(p.GetValue(s), p.GetValue(loaded.Settings));
    }

    [Fact]
    public void Unset_window_position_is_omitted_and_stays_null()
    {
        var store = new SettingsStore(_dir);
        store.TrySave(new ClockSettings());
        Assert.DoesNotContain("x =", File.ReadAllText(store.FilePath));
        Assert.Null(store.Load().Settings.WindowX);
    }

    [Fact]
    public void Invalid_keys_revert_to_defaults_and_valid_ones_are_kept()
    {
        var (s, corrupt, rejected) = SettingsStore.Parse("""
            [clock]
            seconds_scale = 0.1
            margin = 80
            [colors]
            tile = "not a color"
            digit = "#ff0000"
            [date]
            language = "klingon"
            position = "left"
            [animation]
            flip_duration_ms = "fast"
            """);
        Assert.False(corrupt);
        Assert.Equal(1.0, s.SecondsScale);
        Assert.Equal(80, s.Margin);
        Assert.Equal(new ClockSettings().TileColor, s.TileColor);
        Assert.Equal(new RgbColor(255, 0, 0), s.DigitColor);
        Assert.Equal(DateLanguage.Italian, s.DateLanguage);
        Assert.Equal(DatePosition.Left, s.DatePosition);
        Assert.Equal(450, s.FlipDurationMs);
        Assert.Equal(["clock.seconds_scale", "colors.tile", "animation.flip_duration_ms", "date.language"], rejected);
    }

    [Fact]
    public void Integer_literal_is_accepted_for_a_float_setting()
    {
        var (s, _, rejected) = SettingsStore.Parse("[lighting]\nintensity = 2\n");
        Assert.Empty(rejected);
        Assert.Equal(2.0, s.LightIntensity);
    }

    [Fact]
    public void Corrupt_file_yields_defaults_and_is_backed_up()
    {
        var store = new SettingsStore(_dir);
        File.WriteAllText(store.FilePath, "[clock\nmargin = = 3");
        var result = store.Load();
        Assert.True(result.WasCorrupt);
        Assert.Equal(250, result.Settings.Margin);
        Assert.Equal("[clock\nmargin = = 3", File.ReadAllText(store.BackupPath));
    }

    [Fact]
    public void Writable_directory_is_detected()
    {
        Assert.True(new SettingsStore(_dir).IsWritable());
        Assert.False(new SettingsStore(Path.Combine(_dir, "does-not-exist")).IsWritable());
    }

    [Fact]
    public void Copy_appearance_keeps_window_placement()
    {
        var current = new ClockSettings { Margin = 10, WindowWidth = 999, Borderless = true };
        current.CopyAppearanceFrom(new ClockSettings());
        Assert.Equal(250, current.Margin);
        Assert.Equal(999, current.WindowWidth);
        Assert.True(current.Borderless);
    }
}
