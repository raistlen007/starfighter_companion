using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace StarfighterCompanion.Core.Library;

/// <summary>A folder found automatically for a variant, with where it came from.</summary>
public sealed record DetectedFolder(string VariantId, string Folder, string Source);

/// <summary>
/// Looks for installed games: the install folders configured in XWVM, Steam libraries, GOG (registry and
/// default folders), Heroic style game folders, classic CD install locations, the OpenXW family's user folders
/// (SDL preference path TotallyOpen/&lt;App&gt;) and XWVM's pilot folder (ApplicationData/XWVM/pilots).
/// Detection only suggests folders; anything the user set in Settings wins.
/// </summary>
public sealed partial class InstallFinder
{
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, IEnumerable<string>> _subdirectories;

    public InstallFinder()
        : this(Directory.Exists, SafeSubdirectories)
    {
    }

    /// <summary>For tests: inject a fake file system.</summary>
    internal InstallFinder(Func<string, bool> directoryExists, Func<string, IEnumerable<string>> subdirectories)
    {
        _directoryExists = directoryExists;
        _subdirectories = subdirectories;
    }

    /// <summary>Extra places to look (used by tests, and for Wine prefixes etc.).</summary>
    public List<string> ExtraSearchRoots { get; } = new();

    public IReadOnlyList<DetectedFolder> DetectAll()
    {
        var found = new List<DetectedFolder>();
        var seen = new HashSet<(string, string)>();

        void Add(string variant, string folder, string source)
        {
            folder = Path.GetFullPath(folder);
            if (_directoryExists(folder) && seen.Add((variant, folder.ToLowerInvariant())))
            {
                found.Add(new DetectedFolder(variant, folder, source));
            }
        }

        // Install folders XWVM itself was pointed at: the most reliable source when XWVM is installed.
        foreach (var (variant, folder) in XwvmConfiguredInstalls())
        {
            Add(variant, folder, "XWVM settings");
        }

        foreach (var (dir, source, nameHint) in CandidateGameFolders())
        {
            var name = nameHint ?? Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            switch (Classify(name))
            {
                case GameKind.XWing:
                    // Steam and GOG ship both editions; find each edition's own folder by its files
                    // (the same markers XWVM uses to recognise them).
                    var win98 = FindFolderWith(dir, IsXWing98Folder);
                    var dos = FindFolderWith(dir, IsXWingDosFolder);
                    Add("xwing98", win98 ?? dir, source);
                    Add("xwingdos", dos ?? dir, source);
                    break;
                case GameKind.Tie:
                    Add("tie", dir, source);
                    break;
                case GameKind.XvT:
                    Add("xvt", dir, source);
                    var bop = FindChild(dir, "BalanceOfPower") ?? FindChild(dir, "Balance of Power");
                    if (bop is not null)
                    {
                        Add("bop", bop, source);
                    }

                    break;
                case GameKind.Xwa:
                    Add(HasXwaHooks(dir) ? "xwaup" : "xwa", dir, source);
                    break;
                case GameKind.Tftc:
                    Add("tftc", dir, source);
                    break;
            }
        }

        foreach (var (variant, folder) in OpenFamilyFolders())
        {
            Add(variant, folder, "user folder");
        }

        foreach (var folder in XwvmPilotFolders())
        {
            Add("xwvm", folder, "user folder");
        }

        return found;
    }

    internal enum GameKind
    {
        None,
        XWing,
        Tie,
        XvT,
        Xwa,
        Tftc,
    }

    /// <summary>Recognises a game from its install folder name.</summary>
    internal static GameKind Classify(string folderName)
    {
        var n = folderName.ToLowerInvariant().Replace('_', ' ').Replace('-', ' ');
        n = MultiSpace().Replace(n, " ").Trim();

        if (n.Contains("tftc") || n.Contains("tie fighter total conversion") || n.Contains("total conversion"))
        {
            return GameKind.Tftc;
        }

        if (n.Contains("alliance") || n == "xwa" || n.StartsWith("xwa ") || n.Contains("xwaup"))
        {
            return GameKind.Xwa;
        }

        if (n.Contains("vs tie") || n.Contains("vs. tie") || n.Contains("versus tie") || n == "xvt" || n.Contains("balance of power"))
        {
            return GameKind.XvT;
        }

        if (n.Contains("tie fighter") || n == "tie" || n == "tie95" || n == "tie98" || n.EndsWith(" tie"))
        {
            return GameKind.Tie;
        }

        if (n.Contains("x wing") || n.Contains("xwing"))
        {
            return GameKind.XWing;
        }

        return GameKind.None;
    }

    private IEnumerable<(string Dir, string Source, string? NameHint)> CandidateGameFolders()
    {
        foreach (var library in SteamLibraries())
        {
            foreach (var dir in _subdirectories(Path.Combine(library, "steamapps", "common")))
            {
                yield return (dir, "Steam", null);
            }
        }

        foreach (var (dir, name) in GogRegistryFolders())
        {
            yield return (dir, "GOG", name);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var parents = new List<(string Path, string Source)>();
        if (OperatingSystem.IsWindows())
        {
            foreach (var drive in WindowsDrives())
            {
                parents.Add((Path.Combine(drive, "GOG Games"), "GOG"));
                parents.Add((Path.Combine(drive, "Games"), "games folder"));
                parents.Add((drive, "drive root"));
            }

            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            foreach (var p in new[] { pf86, pf }.Where(p => !string.IsNullOrEmpty(p)).Distinct())
            {
                parents.Add((Path.Combine(p, "GOG Galaxy", "Games"), "GOG Galaxy"));
                parents.Add((Path.Combine(p, "LucasArts"), "CD install"));
                parents.Add((Path.Combine(p, "Steam", "steamapps", "common"), "Steam"));
            }
        }
        else
        {
            parents.Add((Path.Combine(home, "GOG Games"), "GOG"));
            parents.Add((Path.Combine(home, "Games"), "games folder"));
            parents.Add((Path.Combine(home, "Games", "Heroic"), "Heroic"));
            parents.Add((Path.Combine(home, "Applications"), "applications"));
            if (OperatingSystem.IsMacOS())
            {
                parents.Add(("/Applications", "applications"));
            }
        }

        parents.AddRange(ExtraSearchRoots.Select(r => (r, "extra folder")));

        foreach (var (parent, source) in parents)
        {
            foreach (var dir in _subdirectories(parent))
            {
                yield return (dir, source, null);
            }
        }
    }

    private IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            var fromRegistry = ReadRegistryString(@"Software\Valve\Steam", "SteamPath", currentUser: true);
            if (fromRegistry is not null)
            {
                roots.Add(fromRegistry.Replace('/', Path.DirectorySeparatorChar));
            }

            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            roots.Add(Path.Combine(home, "Library", "Application Support", "Steam"));
        }
        else
        {
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }

        var libraries = new List<string>();
        foreach (var root in roots.Where(_directoryExists))
        {
            libraries.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(vdf);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (Match m in SteamPath().Matches(text))
            {
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }

        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).Where(_directoryExists);
    }

    private IEnumerable<(string Dir, string Name)> GogRegistryFolders()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (var (path, name) in ReadGogGames())
        {
            if (Classify(name) != GameKind.None && _directoryExists(path))
            {
                yield return (path, name);
            }
        }
    }

    private IEnumerable<(string Variant, string Folder)> OpenFamilyFolders()
    {
        // Aeron (the OpenXW family's engine) stores user data in SDL's preference path: <base>/TotallyOpen/<App>/.
        var bases = new List<string>();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            bases.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        }
        else if (OperatingSystem.IsMacOS())
        {
            bases.Add(Path.Combine(home, "Library", "Application Support"));
        }
        else
        {
            var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            bases.Add(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg);
        }

        foreach (var b in bases)
        {
            yield return ("openxw", Path.Combine(b, "TotallyOpen", "OpenXW"));
            yield return ("opentie", Path.Combine(b, "TotallyOpen", "OpenTIE"));
            yield return ("openxvt", Path.Combine(b, "TotallyOpen", "OpenXvT"));
            yield return ("openxwa", Path.Combine(b, "TotallyOpen", "OpenXWA"));
        }
    }

    /// <summary>XWVM's user folder: <c>Environment.SpecialFolder.ApplicationData/XWVM</c> (MainConfiguration.UserConfigPath).</summary>
    internal static string XwvmUserFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "XWVM");

    private IEnumerable<string> XwvmPilotFolders()
    {
        // MainConfiguration.PilotFilePath = UserConfigPath/pilots.
        yield return Path.Combine(XwvmUserFolder, "pilots");
    }

    /// <summary>
    /// Reads XWVM's configuration (JSON in its user folder) for the game install paths the player gave it:
    /// <c>gameVariantConfigurations[].variant / installPath</c>.
    /// </summary>
    private IEnumerable<(string Variant, string Folder)> XwvmConfiguredInstalls()
    {
        var results = new List<(string, string)>();
        if (!_directoryExists(XwvmUserFolder))
        {
            return results;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(XwvmUserFolder, "*.json").ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return results;
        }

        foreach (var file in files)
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
                if (!json.RootElement.TryGetProperty("gameVariantConfigurations", out var configs) ||
                    configs.ValueKind != System.Text.Json.JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var config in configs.EnumerateArray())
                {
                    var variant = config.TryGetProperty("variant", out var v) ? v.ToString() : null;
                    var path = config.TryGetProperty("installPath", out var p) ? p.GetString() : null;
                    var ours = MapXwvmVariant(variant);
                    if (ours is not null && !string.IsNullOrWhiteSpace(path))
                    {
                        results.Add((ours, path));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
            {
                // Not XWVM's configuration, or unreadable.
            }
        }

        return results;
    }

    /// <summary>Maps XWVM's GameVariant names to this editor's versions.</summary>
    internal static string? MapXwvmVariant(string? variant) => variant?.ToLowerInvariant() switch
    {
        null => null,
        var v when v.StartsWith("xwing98") => "xwing98",
        var v when v.StartsWith("xwing93") || v.StartsWith("xwing94") => "xwingdos",
        var v when v.StartsWith("tie") => "tie",
        _ => null,
    };

    /// <summary>1998 edition: XWING95.EXE next to "X-Wing Data" (XWVM's XW98 resource finders).</summary>
    internal bool IsXWing98Folder(string dir) =>
        FileExists(dir, "XWING95.EXE") && _subdirectories(dir).Any(d => string.Equals(Path.GetFileName(d), "X-Wing Data", StringComparison.OrdinalIgnoreCase));

    /// <summary>DOS editions: the B-Wing executable, or the CD image of the GOG (game.gog) / Steam (game.fil) 1994 edition.</summary>
    internal bool IsXWingDosFolder(string dir) =>
        FileExists(dir, "BWING.EXE") || FileExists(dir, "XWING.EXE") || FileExists(dir, "game.gog") || FileExists(dir, "game.fil");

    /// <summary>The folder itself or a sub-folder up to two levels down that satisfies <paramref name="test"/>.</summary>
    private string? FindFolderWith(string root, Func<string, bool> test)
    {
        if (test(root))
        {
            return root;
        }

        foreach (var child in _subdirectories(root))
        {
            if (test(child))
            {
                return child;
            }

            foreach (var grandchild in _subdirectories(child))
            {
                if (test(grandchild))
                {
                    return grandchild;
                }
            }
        }

        return null;
    }

    private static bool FileExists(string dir, string name)
    {
        try
        {
            return Directory.EnumerateFiles(dir, name, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true }).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool HasXwaHooks(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "hook_*.dll").Any() || _directoryExists(Path.Combine(dir, "Effects"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private string? FindChild(string dir, string name) =>
        _subdirectories(dir).FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> WindowsDrives()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch (IOException)
        {
            return new[] { @"C:\" };
        }
    }

    private static IEnumerable<string> SafeSubdirectories(string path)
    {
        try
        {
            return Directory.Exists(path)
                ? Directory.EnumerateDirectories(path, "*", new EnumerationOptions { IgnoreInaccessible = true }).ToList()
                : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static string? ReadRegistryString(string key, string value, bool currentUser)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        return ReadRegistryStringWindows(key, value, currentUser);
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadRegistryStringWindows(string key, string value, bool currentUser)
    {
        try
        {
            using var hive = currentUser ? Microsoft.Win32.Registry.CurrentUser : Microsoft.Win32.Registry.LocalMachine;
            using var k = hive.OpenSubKey(key);
            return k?.GetValue(value) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static IEnumerable<(string Path, string Name)> ReadGogGames()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<(string, string)>();
        }

        return ReadGogGamesWindows();
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<(string Path, string Name)> ReadGogGamesWindows()
    {
        var games = new List<(string, string)>();
        foreach (var keyPath in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(keyPath);
                if (key is null)
                {
                    continue;
                }

                foreach (var id in key.GetSubKeyNames())
                {
                    using var game = key.OpenSubKey(id);
                    if (game?.GetValue("path") is string path && game.GetValue("gameName") is string name)
                    {
                        games.Add((path, name));
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // Ignore unreadable registry keys.
            }
        }

        return games;
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex SteamPath();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpace();
}
