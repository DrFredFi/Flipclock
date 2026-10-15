using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace Flipclock.Core.Settings;

public sealed record SettingsLoadResult(
    ClockSettings Settings,
    bool FileExisted,
    bool WasCorrupt,
    IReadOnlyList<string> RejectedKeys);

/// <summary>
/// Reads and writes <c>config.toml</c>. The file lives next to the executable;
/// there is deliberately no fallback location.
/// </summary>
public sealed class SettingsStore
{
    public const string FileName = "config.toml";

    public SettingsStore(string directory)
    {
        Directory = directory;
        FilePath = Path.Combine(directory, FileName);
    }

    public string Directory { get; }
    public string FilePath { get; }
    public string BackupPath => FilePath + ".bak";

    public SettingsLoadResult Load()
    {
        var settings = new ClockSettings();
        if (!File.Exists(FilePath))
            return new SettingsLoadResult(settings, FileExisted: false, WasCorrupt: false, []);

        string text;
        try
        {
            text = File.ReadAllText(FilePath, Encoding.UTF8);
        }
        catch (IOException)
        {
            return new SettingsLoadResult(settings, FileExisted: true, WasCorrupt: false, []);
        }
        catch (UnauthorizedAccessException)
        {
            return new SettingsLoadResult(settings, FileExisted: true, WasCorrupt: false, []);
        }

        var (loaded, corrupt, rejected) = Parse(text);
        if (corrupt) PreserveCorruptFile();
        return new SettingsLoadResult(loaded, FileExisted: true, corrupt, rejected);
    }

    /// <summary>Parses TOML text. Invalid keys keep their defaults; unparseable text yields all defaults.</summary>
    public static (ClockSettings Settings, bool Corrupt, IReadOnlyList<string> RejectedKeys) Parse(string text)
    {
        var settings = new ClockSettings();
        var doc = Toml.Parse(text);
        if (doc.HasErrors)
            return (settings, true, []);

        TomlTable model;
        try
        {
            model = doc.ToModel();
        }
        catch (TomlException)
        {
            return (settings, true, []);
        }

        var rejected = new List<string>();
        foreach (var entry in SettingsSchema.Entries)
        {
            if (!model.TryGetValue(entry.Section, out var sectionObj) || sectionObj is not TomlTable section)
                continue;
            if (!section.TryGetValue(entry.Key, out var raw))
                continue;
            if (!entry.TrySet(settings, raw))
                rejected.Add($"{entry.Section}.{entry.Key}");
        }
        return (settings, false, rejected);
    }

    public static string Serialize(ClockSettings settings)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Flipclock configuration. Written automatically; edit while the app is closed.");
        string? current = null;
        foreach (var entry in SettingsSchema.Entries)
        {
            var value = entry.Get(settings);
            if (value is null) continue;
            if (entry.Section != current)
            {
                sb.AppendLine();
                sb.Append('[').Append(entry.Section).AppendLine("]");
                current = entry.Section;
            }
            sb.Append(entry.Key).Append(" = ").AppendLine(SettingsSchema.FormatValue(value));
        }
        return sb.ToString();
    }

    /// <summary>Writes the settings atomically. Returns false if the file cannot be written.</summary>
    public bool TrySave(ClockSettings settings)
    {
        var tmp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(tmp, Serialize(settings), new UTF8Encoding(false));
            File.Move(tmp, FilePath, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(tmp); } catch { /* best effort */ }
            return false;
        }
    }

    /// <summary>Checks whether the config directory accepts writes.</summary>
    public bool IsWritable()
    {
        var probe = Path.Combine(Directory, $".write-probe-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    void PreserveCorruptFile()
    {
        try { File.Copy(FilePath, BackupPath, overwrite: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* nothing more we can do */ }
    }
}
