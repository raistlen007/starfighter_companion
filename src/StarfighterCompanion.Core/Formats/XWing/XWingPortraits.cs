using StarfighterCompanion.Core.Media;

namespace StarfighterCompanion.Core.Formats.XWing;

/// <summary>
/// The pilot portraits of the DOS editions of X-Wing. The game has no portrait setting: the pilot selection screen
/// picks one of the 24 frames of the <c>faces</c> animation in <c>RESOURCE\PILOT.LFD</c> from the pilot's name
/// (OpenXW src/xw_dos94/frontend/pilot_selection.c, draw_pilot): the character codes of the name are added up and
/// the frame is <c>abs((sum - 1) % 24)</c>. The registration desk stores names in upper case, so the sum is taken
/// over the upper-case name. Colours come from the <c>PLTT</c> resources of PILOT.LFD on top of the game's
/// standard palette in <c>RESOURCE\XWING.LFD</c>.
/// The pictures are only ever read from the player's own installation.
/// </summary>
public sealed class XWingPortraits
{
    /// <summary>The game always chooses among 24 faces.</summary>
    public const int FaceCount = 24;

    private XWingPortraits(IReadOnlyList<GameImage> faces, string source)
    {
        Faces = faces;
        Source = source;
    }

    public IReadOnlyList<GameImage> Faces { get; }

    /// <summary>Where the faces were read from (a file, or a file inside a CD image).</summary>
    public string Source { get; }

    /// <summary>The face the game shows for <paramref name="pilotName"/>.</summary>
    public static int FaceIndex(string pilotName)
    {
        var sum = 0;
        foreach (var c in pilotName.ToUpperInvariant())
        {
            sum += c & 0xFF;
        }

        // C's % truncates towards zero like C#'s; an empty name gives abs(-1) = 1.
        return Math.Abs((sum - 1) % FaceCount);
    }

    public GameImage? For(string pilotName) => Faces.Count == 0 ? null : Faces[FaceIndex(pilotName) % Faces.Count];

    /// <summary>Decodes the faces from the contents of PILOT.LFD (and XWING.LFD for the standard palette).</summary>
    public static XWingPortraits? FromResources(byte[] pilotLfd, byte[]? xwingLfd, string source)
    {
        var pilot = LfdArchive.Read(pilotLfd);
        var faces = pilot.Find("ANIM", "faces");
        if (faces is null)
        {
            return null;
        }

        var palette = new VgaPalette();
        if (xwingLfd is not null)
        {
            var standard = LfdArchive.Read(xwingLfd).Find("PLTT", "standard");
            if (standard is not null)
            {
                palette.Apply(standard.Data);
            }
        }

        foreach (var pltt in pilot.All("PLTT"))
        {
            palette.Apply(pltt.Data);
        }

        var frames = LfdAnimation.Decode(faces.Data, palette);
        return frames.Count == 0 ? null : new XWingPortraits(frames, source);
    }

    /// <summary>
    /// Looks for PILOT.LFD in the given install folders: in their RESOURCE folder (also one or two folders down,
    /// for installs that keep each edition in a sub-folder), or inside the CD image of the GOG and Steam DOS editions.
    /// </summary>
    public static XWingPortraits? Find(IEnumerable<string> folders)
    {
        var list = folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var folder in list)
        {
            foreach (var dir in SelfAndChildren(folder, 2))
            {
                var resource = Child(dir, "RESOURCE", directory: true);
                foreach (var candidate in new[] { resource, dir }.Where(d => d is not null))
                {
                    var pilot = Child(candidate!, "PILOT.LFD", directory: false);
                    if (pilot is null)
                    {
                        continue;
                    }

                    var found = TryLoad(() => File.ReadAllBytes(pilot), () =>
                    {
                        var xwing = Child(candidate!, "XWING.LFD", directory: false);
                        return xwing is null ? null : File.ReadAllBytes(xwing);
                    }, pilot);
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }

        foreach (var folder in list)
        {
            foreach (var dir in SelfAndChildren(folder, 1))
            {
                foreach (var image in DiscImages(dir))
                {
                    using var disc = DiscImage.Open(image);
                    var path = disc?.Locate("PILOT.LFD");
                    if (disc is null || path is null)
                    {
                        continue;
                    }

                    var folderInImage = path.Contains('/') ? path[..(path.LastIndexOf('/') + 1)] : string.Empty;
                    var found = TryLoad(() => disc.ReadFile(path), () => disc.ReadFile(folderInImage + "XWING.LFD"),
                        $"{image} ({path})");
                    if (found is not null)
                    {
                        return found;
                    }
                }
            }
        }

        return null;
    }

    private static XWingPortraits? TryLoad(Func<byte[]?> pilot, Func<byte[]?> xwing, string source)
    {
        try
        {
            var data = pilot();
            return data is null ? null : FromResources(data, xwing(), source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    private static IEnumerable<string> DiscImages(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir)
                .Where(f => Path.GetFileName(f).ToLowerInvariant() is "game.gog" or "game.fil" ||
                            Path.GetExtension(f).Equals(".iso", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SelfAndChildren(string folder, int depth)
    {
        yield return folder;
        if (depth == 0)
        {
            yield break;
        }

        string[] children;
        try
        {
            children = Directory.GetDirectories(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var child in children)
        {
            foreach (var d in SelfAndChildren(child, depth - 1))
            {
                yield return d;
            }
        }
    }

    /// <summary>A file or folder directly in <paramref name="dir"/>, matched without regard to case.</summary>
    internal static string? Child(string dir, string name, bool directory)
    {
        try
        {
            var options = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };
            return (directory ? Directory.EnumerateDirectories(dir, name, options) : Directory.EnumerateFiles(dir, name, options)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
