using StarfighterCompanion.Core.Media;

namespace StarfighterCompanion.Core.Library;

/// <summary>
/// Finds the icon of an installed game in its own folder. No game artwork is shipped with the program: when a game is
/// not installed (or has no icon, like the DOS editions), the app shows a generic placeholder instead.
/// </summary>
public static class GameIcons
{
    private const long MaxProgramSize = 64L * 1024 * 1024;

    /// <summary>The game's own programs, by version, checked first (case does not matter).</summary>
    private static readonly Dictionary<string, string[]> Programs = new()
    {
        ["xwing98"] = new[] { "XWING95.EXE", "XWINGTIE.EXE", Path.Combine("XWINGCD", "XWING95.EXE") },
        ["tie98"] = new[] { "TIE95.EXE" },
        ["xvt"] = new[] { "XWINGTIE.EXE", "Z_XVT__.EXE" }, // the game program itself has no icon; its launcher has
        ["bop"] = new[] { "Z_XVT__.EXE" },
        ["xwa"] = new[] { "XWINGALLIANCE.EXE" },
        ["xwaup"] = new[] { "XWINGALLIANCE.EXE" },
        ["xwaup2020"] = new[] { "XWINGALLIANCE.EXE" },
        ["embers"] = new[] { "XWINGALLIANCE.EXE" },
        ["tftc"] = new[] { "XWINGALLIANCE.EXE" },
        ["tftcr"] = new[] { "XWINGALLIANCE.EXE" },
    };

    /// <summary>
    /// Versions whose library folder only holds pilots: their icon is read from their program, found elsewhere
    /// (<see cref="InstallFinder.FindProgramFolder"/>). Program, folder names, and folder names that are another version.
    /// </summary>
    private static readonly Dictionary<string, (string Program, string[] Names, string[]? Not)> PilotOnlyVersions = new()
    {
        ["openxw"] = ("OpenXW.exe", new[] { "openxw" }, new[] { "openxwa" }),
        ["opentie"] = ("OpenTIE.exe", new[] { "opentie", "opentf" }, null),
        ["openxvt"] = ("OpenXvT.exe", new[] { "openxvt" }, null),
        ["openxwa"] = ("OpenXWA.exe", new[] { "openxwa" }, null),
        ["xwvm"] = ("xwvm.exe", new[] { "xwvm" }, null),
    };

    private static readonly Dictionary<string, string?> ProgramFolders = new();

    /// <summary>The icon of a version that keeps pilots apart from its program (OpenXW family, XWVM), or null.</summary>
    public static GameImage? FindForPilotOnlyVersion(string variantId, InstallFinder? finder = null)
    {
        if (!PilotOnlyVersions.TryGetValue(variantId, out var entry))
        {
            return null;
        }

        string? folder;
        lock (ProgramFolders)
        {
            if (!ProgramFolders.TryGetValue(variantId, out folder))
            {
                folder = (finder ?? new InstallFinder()).FindProgramFolder(entry.Program, entry.Names, entry.Not);
                ProgramFolders[variantId] = folder;
            }
        }

        return folder is null ? null : FromPrograms(folder, new[] { entry.Program }) ?? FromIcoFiles(folder, gogOnly: false);
    }

    public static bool IsPilotOnlyVersion(string variantId) => PilotOnlyVersions.ContainsKey(variantId);

    /// <summary>Forgets where the programs were found (after the game folders change).</summary>
    public static void ResetProgramFolders()
    {
        lock (ProgramFolders)
        {
            ProgramFolders.Clear();
        }
    }

    /// <summary>True when <paramref name="folder"/> holds the program of <paramref name="variantId"/>, or the version has no known program.</summary>
    internal static bool HasProgramOrUnknown(string variantId, string folder) =>
        !Programs.TryGetValue(variantId, out var programs) || programs.Any(p => Resolve(folder, p) is not null);

    /// <summary>Programs that are not the game itself and whose icon would be misleading.</summary>
    private static readonly string[] NotTheGame = { "unins", "setup", "install", "dosbox", "config", "launcher", "vcredist", "dxsetup", "dgvoodoo" };

    /// <summary>
    /// The icon of the game in <paramref name="folder"/>: the version's own program, a GOG icon file, any other
    /// program or .ico file in the folder, or the game's program or GOG icon one folder up (the DOS editions sold on
    /// GOG and Steam sit next to the Windows edition). Returns null when there is none.
    /// </summary>
    public static GameImage? Find(string variantId, string? folder)
    {
        if (IsPilotOnlyVersion(variantId))
        {
            return FindForPilotOnlyVersion(variantId);
        }

        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return null;
        }

        var gameId = GameCatalog.AllVariants.FirstOrDefault(v => v.Id == variantId)?.GameId;
        var own = Programs.GetValueOrDefault(variantId) ?? Array.Empty<string>();
        var family = gameId is null
            ? own
            : GameCatalog.Game(gameId).Variants.SelectMany(v => Programs.GetValueOrDefault(v.Id) ?? Array.Empty<string>()).Distinct().ToArray();

        return FromPrograms(folder, own)
               ?? FromIcoFiles(folder, gogOnly: true)
               ?? FromIcoFiles(folder, gogOnly: false)
               ?? FromAnyProgram(folder)
               ?? FromParent(folder, family);
    }

    private static GameImage? FromParent(string folder, string[] programs)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
        if (parent is null || Path.GetPathRoot(parent) == parent)
        {
            return null;
        }

        return FromPrograms(parent, programs) ?? FromIcoFiles(parent, gogOnly: true);
    }

    private static GameImage? FromPrograms(string folder, IEnumerable<string> programs)
    {
        foreach (var program in programs)
        {
            var path = Resolve(folder, program);
            var data = path is null ? null : ReadSmall(path);
            var icon = data is null ? null : IconReader.FromExecutable(data);
            if (icon is not null)
            {
                return icon;
            }
        }

        return null;
    }

    private static GameImage? FromIcoFiles(string folder, bool gogOnly)
    {
        foreach (var file in Files(folder, "*.ico").Where(f => !gogOnly || Path.GetFileName(f).StartsWith("goggame", StringComparison.OrdinalIgnoreCase)))
        {
            var data = ReadSmall(file);
            var icon = data is null ? null : IconReader.FromIco(data);
            if (icon is not null)
            {
                return icon;
            }
        }

        return null;
    }

    private static GameImage? FromAnyProgram(string folder)
    {
        foreach (var file in Files(folder, "*.exe").Where(f => !NotTheGame.Any(n => Path.GetFileName(f).Contains(n, StringComparison.OrdinalIgnoreCase))).Take(20))
        {
            var data = ReadSmall(file);
            var icon = data is null ? null : IconReader.FromExecutable(data);
            if (icon is not null)
            {
                return icon;
            }
        }

        return null;
    }

    /// <summary>Finds <paramref name="relative"/> under <paramref name="folder"/> without regard to case.</summary>
    private static string? Resolve(string folder, string relative)
    {
        var current = folder;
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var i = 0; i < parts.Length; i++)
        {
            var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };
            try
            {
                var next = i == parts.Length - 1
                    ? Directory.EnumerateFiles(current, parts[i], options).FirstOrDefault()
                    : Directory.EnumerateDirectories(current, parts[i], options).FirstOrDefault();
                if (next is null)
                {
                    return null;
                }

                current = next;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return current;
    }

    private static IEnumerable<string> Files(string folder, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(folder, pattern, new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static byte[]? ReadSmall(string path)
    {
        try
        {
            return new FileInfo(path).Length > MaxProgramSize ? null : File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
