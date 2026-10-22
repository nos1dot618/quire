using System.IO;
using System.Text.Json;

namespace Quire.Storage;

public enum ThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>Per-user preferences, stored as JSON in %LOCALAPPDATA%\Quire\settings.json.</summary>
public sealed class Settings
{
    public const int MaxRecentDocuments = 10;

    public List<string> RecentDocuments { get; set; } = [];
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public bool PreviewMode { get; set; } = true;
    public bool AttachDroppedFiles { get; set; }
    public bool ExportCellNames { get; set; } = true;

    /// <summary>Moves a path to the top of the recent list, removing duplicates and trimming the list.</summary>
    public void AddRecent(string path)
    {
        var full = Path.GetFullPath(path);
        RecentDocuments.RemoveAll(existing => string.Equals(existing, full, StringComparison.OrdinalIgnoreCase));
        RecentDocuments.Insert(0, full);
        if (RecentDocuments.Count > MaxRecentDocuments)
            RecentDocuments.RemoveRange(MaxRecentDocuments, RecentDocuments.Count - MaxRecentDocuments);
    }
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quire", "settings.json");

    /// <summary>Reads settings, falling back to defaults if the file is missing or unreadable.</summary>
    public Settings Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options) ?? new Settings();
            settings.RecentDocuments = settings.RecentDocuments.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            return settings;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return new Settings();
        }
    }

    /// <summary>Writes settings atomically. Failures are ignored: preferences are not worth an error dialog.</summary>
    public void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
