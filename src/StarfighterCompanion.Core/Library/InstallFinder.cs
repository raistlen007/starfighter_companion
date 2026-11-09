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
                    var floppy = FindFolderWith(dir, IsXWing93Folder);
                    var cd = FindFolderWith(dir, IsXWing94Folder);
                    if (win98 is not null)
                    {
                        Add("xwing98", win98, source);
                    }

                    if (floppy is not null)
                    {
                        Add("xwing93", floppy, source);
                    }

                    if (cd is not null)
                    {
                        Add("xwing94", cd, source);
                    }

                    if (win98 is null && floppy is null && cd is null)
                    {
                        // Unrecognised layout: offer the folder for the editions found on Steam and GOG.
                        Add("xwing98", dir, source);
                        Add("xwing94", dir, source);
                    }

                    break;
                case GameKind.Tie:
                    // Steam and GOG ship the DOS and Windows editions side by side; pick the edition folder by
                    // its files (XWVM's TIE resource finders), preferring the Windows 98 edition.
                    var tie98 = FindFolderWith(dir, IsTie98Folder);
                    var tie95 = FindFolderWith(dir, IsTie95Folder);
                    var tie94 = FindFolderWith(dir, IsTie94Folder);
                    if (tie98 is not null)
                    {
                        Add("tie98", tie98, source);
                    }

                    if (tie95 is not null)
                    {
                        Add("tie95", tie95, source);
                    }

                    if (tie94 is not null)
                    {
                        Add("tie94", tie94, source);
                    }

                    if (tie98 is null && tie95 is null && tie94 is null)
                    {
                        Add("tie98", dir, source);
                        Add("tie95", dir, source);
                    }

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
                    Add(XwaVariant(dir), dir, source);
                    break;
                case GameKind.Tftc:
                    Add(IsTftcReimagined(Path.GetFileName(dir.TrimEnd('\\', '/'))) ? "tftcr" : "tftc", dir, source);
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

        // When a version turns up in several folders, the one that holds the game's program comes first: a Steam
        // folder left behind after uninstalling (pilots and ddraw files only) must not hide the real installation.
        return found.OrderBy(f => GameIcons.HasProgramOrUnknown(f.VariantId, f.Folder) ? 0 : 1).ToList();
    }

    /// <summary>XWVM's installer id (innosetup/xwvm.iss AppID); Inno Setup records the install folder under it.</summary>
    private const string XwvmUninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{ED9A9575-B62E-4202-95C5-9FBC8FEDE2D4}_is1";

    /// <summary>
    /// Finds the folder with the program of a version whose library folder only holds pilots (the OpenXW family and
    /// XWVM keep pilots in a user folder), so its icon can be read from the program. Looks in the places games are
    /// searched for, by folder name ("OpenXWA Testing", "XWVM"), and for XWVM where its installer put it.
    /// Returns null when the program is not found.
    /// </summary>
    public string? FindProgramFolder(string program, IReadOnlyList<string> folderNames, IReadOnlyList<string>? excludedNames = null)
    {
        static string Normal(string name) => MultiSpace().Replace(name.ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), " ").Trim();

        var candidates = new List<string>();
        if (program.Equals("xwvm.exe", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var currentUser in new[] { false, true })
            {
                if (ReadRegistryString(XwvmUninstallKey, "InstallLocation", currentUser) is { Length: > 0 } location)
                {
                    candidates.Add(location);
                }
            }

            if (OperatingSystem.IsWindows())
            {
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "XWVM"));
                candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "XWVM"));
            }
        }

        foreach (var (dir, _, nameHint) in CandidateGameFolders())
        {
            var name = Normal(nameHint ?? Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            if (folderNames.Any(n => name.StartsWith(n, StringComparison.Ordinal)) &&
                (excludedNames is null || !excludedNames.Any(n => name.StartsWith(n, StringComparison.Ordinal))))
            {
                candidates.Add(dir);
            }
        }

        foreach (var dir in candidates.Where(d => !string.IsNullOrWhiteSpace(d) && _directoryExists(d)))
        {
            // The program itself, or one folder down (a build folder such as "bin" or "Release").
            foreach (var folder in new[] { dir }.Concat(_subdirectories(dir)))
            {
                try
                {
                    if (Directory.EnumerateFiles(folder, program, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive }).Any())
                    {
                        return folder;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Not readable: try the next one.
                }
            }
        }

        return null;
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

        if (n.Contains("alliance") || n == "xwa" || n.StartsWith("xwa ") || n.Contains("xwaup") || n.StartsWith("xwau") ||
            n.Contains("embers of the empire") || n == "embers")
        {
            return GameKind.Xwa;
        }

        if (n.Contains("vs tie") || n.Contains("vs. tie") || n.Contains("versus tie") || n == "xvt" || n.EndsWith(" xvt", StringComparison.Ordinal) ||
            n.Contains("balance of power"))
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
        var v when v.StartsWith("xwing94") => "xwing94",
        var v when v.StartsWith("xwing93") => "xwing93",
        var v when v.StartsWith("tie98") => "tie98",
        var v when v.StartsWith("tie95") => "tie95",
        var v when v.StartsWith("tie94") => "tie94",
        _ => null,
    };

    // Edition markers, taken from XWVM's resource finders (XW93/XW94/XW98 and Tie94/Tie95/Tie98 ...ResourceFinder).

    /// <summary>1998 edition, digital (XWING95.EXE next to "X-Wing Data") or CD layout (XWINGTIE.EXE + XWINGCD).</summary>
    internal bool IsXWing98Folder(string dir) =>
        Matches(dir, ["XWING95.EXE", Path.Combine("X-Wing Data", "MISSION")], [])
        || Matches(dir, ["XWINGTIE.EXE", Path.Combine("XWINGCD", "XWING95.EXE"), Path.Combine("XWINGCD", "X-Wing Data", "MISSION")], ["XWING.EXE", "XWING95.EXE"]);

    /// <summary>DOS editions: the 1994 CD-ROM or the 1993 floppy.</summary>
    internal bool IsXWingDosFolder(string dir) => IsXWing94Folder(dir) || IsXWing93Folder(dir);

    /// <summary>1994 Collector's CD (BWING.EXE + BFLIGHT.OVL), or the CD image of the GOG (game.gog) / Steam (game.fil) DOS edition.</summary>
    internal bool IsXWing94Folder(string dir) =>
        Matches(dir, ["MISSION", "RESOURCE", "BWING.EXE", "BFLIGHT.OVL"], [])
        || Has(dir, "game.gog") || Has(dir, "game.fil");

    /// <summary>1993 floppy: XWING.EXE + FLIGHT.OVL, without the CD-ROM's CLASSIC folder.</summary>
    internal bool IsXWing93Folder(string dir) =>
        Matches(dir, ["MISSION", "RESOURCE", "XWING.EXE", "FLIGHT.OVL"], ["CLASSIC"]);

    /// <summary>The TIE Fighter edition whose files are in <paramref name="dir"/>, if any.</summary>
    public string? TieEditionOf(string dir) =>
        IsTie98Folder(dir) ? "tie98" : IsTie95Folder(dir) ? "tie95" : IsTie94Folder(dir) ? "tie94" : null;

    /// <summary>The X-Wing DOS edition whose files are in <paramref name="dir"/>, if any.</summary>
    public string? XWingDosEditionOf(string dir) =>
        IsXWing94Folder(dir) ? "xwing94" : IsXWing93Folder(dir) ? "xwing93" : null;

    /// <summary>TIE Fighter Windows 98 edition: TIE95.EXE without the DOS TIE.EXE.</summary>
    internal bool IsTie98Folder(string dir) => Matches(dir, ["MISSION", "RESOURCE", "TIE95.EXE"], ["TIE.EXE"]);

    /// <summary>TIE Fighter 1995 Collector's CD (DOS), as a CD install or the GOG/Steam CD image.</summary>
    internal bool IsTie95Folder(string dir) => Matches(dir, ["MISSION", "RESOURCE", "Z_TIE__.EXE", "TIE.EXE"], []);

    /// <summary>TIE Fighter 1994 floppy edition.</summary>
    internal bool IsTie94Folder(string dir) => Matches(dir, ["MISSION", "RESOURCE", "TIE.EXE", "FLIGHT.OVL"], []);

    private bool Matches(string dir, string[] present, string[] absent) =>
        present.All(p => Has(dir, p)) && !absent.Any(a => Has(dir, a));

    /// <summary>Whether a file or folder exists at <paramref name="relative"/> under <paramref name="dir"/>, ignoring case.</summary>
    private bool Has(string dir, string relative)
    {
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = dir;
        for (var i = 0; i < parts.Length; i++)
        {
            var last = i == parts.Length - 1;
            var sub = _subdirectories(current).FirstOrDefault(d => string.Equals(Path.GetFileName(d), parts[i], StringComparison.OrdinalIgnoreCase));
            if (sub is not null)
            {
                current = sub;
                continue;
            }

            return last && FileExists(current, parts[i]);
        }

        return true;
    }

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

    /// <summary>TFTC Reimagined is told apart from Classic by its folder name ("TFTC-R", "TFTC Reimagined").</summary>
    internal static bool IsTftcReimagined(string folderName)
    {
        var n = MultiSpace().Replace(folderName.ToLowerInvariant().Replace('_', ' ').Replace('-', ' '), " ").Trim();
        return n.Contains("reimagined") || n.EndsWith(" r", StringComparison.Ordinal);
    }

    /// <summary>
    /// Which XWA version an installation is, from the files that exist there (their contents are not read):
    /// Embers of the Empire has EmbersVersion.txt, the 2025 Upgrade adds its installer-runner folder, and the 2020
    /// Mega Patch only the hooks.
    /// </summary>
    private string XwaVariant(string dir)
    {
        if (!HasXwaHooks(dir))
        {
            return "xwa";
        }

        if (FileExists(Path.Combine(dir, "EmbersVersion.txt")))
        {
            return "embers";
        }

        return _directoryExists(Path.Combine(dir, "XwaInstallerRunner")) || _directoryExists(Path.Combine(dir, "patcher-xml"))
            ? "xwaup"
            : "xwaup2020";
    }

    private static bool FileExists(string path)
    {
        try
        {
            return File.Exists(path);
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
