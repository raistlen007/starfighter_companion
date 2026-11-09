using System.Globalization;
using System.Text;

namespace StarfighterCompanion.Core.Formats.Xwa;

/// <summary>One mission in an X-Wing Alliance mission list.</summary>
public sealed record XwaMissionListEntry(string Section, int MissionIndex, string FileName, string Title);

/// <summary>
/// Reads mission lists in the format of X-Wing Alliance's <c>MISSION.LST</c> (MISSIONS\ for the campaign, COMBAT\ for
/// the Combat Chamber), the way the game does (OpenXWA MissionSetup_LoadMissionList): "//" lines are comments, "[Name]" starts a section, and each
/// mission is three lines: its index in the pilot file, its file name (a leading "* " or "&amp; " marks it locked),
/// and its title. XWAUP and TFTC have their own lists; the ones included with
/// Starfighter Companion are built into the program, so nothing is read from the game installation.
/// </summary>
public static class XwaMissionList
{
    public static IReadOnlyList<XwaMissionListEntry> Parse(IEnumerable<string> lines)
    {
        var result = new List<XwaMissionListEntry>();
        var section = string.Empty;
        var pending = new List<string>(3);
        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r', '\n');
            if (line.StartsWith("//", StringComparison.Ordinal) || line.Trim().Length == 0)
            {
                continue;
            }

            if (pending.Count == 0 && line.StartsWith('['))
            {
                section = line.Trim().TrimStart('[').TrimEnd(']');
                continue;
            }

            pending.Add(line);
            if (pending.Count < 3)
            {
                continue;
            }

            var index = LeadingInt(pending[0]);
            var file = pending[1].Trim();
            if (file.Length > 2 && file[0] is '*' or '&')
            {
                file = file[2..].Trim();
            }

            if (index is >= 0 and < 255)
            {
                result.Add(new XwaMissionListEntry(section, index, file, pending[2].Trim()));
            }

            pending.Clear();
        }

        return result;
    }

    /// <summary>
    /// The mission list built into Starfighter Companion for an edition (XWA, XWAUP or TFTC) and list
    /// ("campaign" or "combat"), or null if none is included. Lists are compiled in from
    /// src/StarfighterCompanion.Core/Data/MissionLists/&lt;edition&gt;.&lt;list&gt;.lst; the editor never reads game files.
    /// </summary>
    public static IReadOnlyList<XwaMissionListEntry>? BuiltIn(string edition, string list)
    {
        var name = $"MissionLists.{edition}.{list}.lst";
        var assembly = typeof(XwaMissionList).Assembly;
        var resource = assembly.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(name, StringComparison.OrdinalIgnoreCase));
        if (resource is null)
        {
            return null;
        }

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.Latin1);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
        {
            lines.Add(line);
        }

        var entries = Parse(lines);
        return entries.Count > 0 ? entries : null;
    }

    private static int LeadingInt(string text)
    {
        var digits = new string(text.TrimStart().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
    }
}
