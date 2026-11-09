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
            var line = WithoutKey(raw.TrimEnd('\r', '\n'));
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
    /// The campaign mission list built into Starfighter Companion (xwa, embers, tftc, tftcr, tftc.combat), or null if
    /// none is included. Lists are compiled in from src/StarfighterCompanion.Core/Data/MissionLists/&lt;edition&gt;.lst;
    /// the editor never reads game files.
    /// </summary>
    public static IReadOnlyList<XwaMissionListEntry>? BuiltIn(string edition)
    {
        var name = $"MissionLists.{edition}.lst";
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

    /// <summary>
    /// Lines may start with a localisation key ("!MISSION_9_DESC!Convoy Attack"); the game replaces it with the
    /// translated text, and the English text follows it. Keep the text.
    /// </summary>
    private static string WithoutKey(string line)
    {
        if (line.Length > 2 && line[0] == '!')
        {
            var end = line.IndexOf('!', 1);
            if (end > 1 && line.AsSpan(1, end - 1).IndexOfAny(" \t") < 0 && end + 1 < line.Length)
            {
                return line[(end + 1)..];
            }
        }

        return line;
    }

    private static int LeadingInt(string text)
    {
        var digits = new string(text.TrimStart().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
    }
}
