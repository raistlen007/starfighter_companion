using System.Text.Json;
using System.Text.Json.Serialization;

namespace StarfighterCompanion.Core.Library;

/// <summary>
/// Folder settings per variant, stored as JSON in the user's settings folder.
/// A variant with no entry follows auto-detection; an entry with an empty string means "none" (the user cleared it).
/// </summary>
public sealed class LibrarySettings
{
    public Dictionary<string, string> Folders { get; set; } = new();

    [JsonIgnore]
    public string? FilePath { get; private set; }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StarfighterCompanion", "settings.json");

    public static LibrarySettings Load(string? path = null)
    {
        path ??= DefaultPath;
        LibrarySettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.LibrarySettings) ?? new LibrarySettings()
                : new LibrarySettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            settings = new LibrarySettings();
        }

        settings.FilePath = path;
        return settings;
    }

    public void Save()
    {
        var path = FilePath ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, SettingsJsonContext.Default.LibrarySettings));
    }

    /// <summary>True when the user chose the folder (or chose "none") instead of auto-detection.</summary>
    public bool IsUserSet(string variantId) => Folders.ContainsKey(variantId);

    public void SetFolder(string variantId, string? folder) => Folders[variantId] = folder?.Trim() ?? string.Empty;

    public void UseAutoDetection(string variantId) => Folders.Remove(variantId);
}

/// <summary>The folder in effect for every variant: the user's choice, or what was detected.</summary>
public sealed class GameLibrary
{
    private readonly Dictionary<string, DetectedFolder> _detected = new();

    public GameLibrary(LibrarySettings settings)
    {
        Settings = settings;
    }

    public LibrarySettings Settings { get; }

    public IReadOnlyDictionary<string, DetectedFolder> Detected => _detected;

    public void ApplyDetection(IEnumerable<DetectedFolder> detected)
    {
        _detected.Clear();
        foreach (var d in detected)
        {
            // First match wins (Steam before GOG before generic folders).
            _detected.TryAdd(d.VariantId, d);
        }
    }

    public string? FolderFor(string variantId)
    {
        if (Settings.Folders.TryGetValue(variantId, out var folder))
        {
            return string.IsNullOrWhiteSpace(folder) ? null : folder;
        }

        return _detected.TryGetValue(variantId, out var d) ? d.Folder : null;
    }

    public bool IsAutoDetected(string variantId) => !Settings.IsUserSet(variantId) && _detected.ContainsKey(variantId);
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(LibrarySettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
