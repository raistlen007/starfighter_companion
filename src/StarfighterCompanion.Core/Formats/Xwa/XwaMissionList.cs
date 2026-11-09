using System.Globalization;
using System.Text;

namespace StarfighterCompanion.Core.Formats.Xwa;

/// <summary>One mission in an X-Wing Alliance mission list.</summary>
public sealed record XwaMissionListEntry(string Section, int MissionIndex, string FileName, string Title);

/// <summary>
/// Reads X-Wing Alliance's <c>MISSION.LST</c> (MISSIONS\ for the campaign, COMBAT\ for the Combat Chamber), the way
/// the game does (OpenXWA MissionSetup_LoadMissionList): "//" lines are comments, "[Name]" starts a section, and each
/// mission is three lines: its index in the pilot file, its file name (a leading "* " or "&amp; " marks it locked),
/// and its title. XWAUP and TFTC ship their own lists, so each installation shows its own campaigns.
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

    /// <summary>Looks for <c>&lt;folder&gt;\MISSION.LST</c> next to the pilot file or one or two folders up.</summary>
    public static IReadOnlyList<XwaMissionListEntry>? Find(string? pilotPath, string folder)
    {
        if (string.IsNullOrEmpty(pilotPath))
        {
            return null;
        }

        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(pilotPath));
            for (var level = 0; level < 3 && dir is not null; level++, dir = Path.GetDirectoryName(dir))
            {
                var sub = Directory.Exists(dir)
                    ? Directory.EnumerateDirectories(dir).FirstOrDefault(d => string.Equals(Path.GetFileName(d), folder, StringComparison.OrdinalIgnoreCase))
                    : null;
                var file = sub is null
                    ? null
                    : Directory.EnumerateFiles(sub).FirstOrDefault(f => string.Equals(Path.GetFileName(f), "MISSION.LST", StringComparison.OrdinalIgnoreCase));
                if (file is not null)
                {
                    var list = Parse(File.ReadAllLines(file, Encoding.Latin1));
                    return list.Count > 0 ? list : null;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // No readable list: the editor falls back to plain mission numbers.
        }

        return null;
    }

    private static int LeadingInt(string text)
    {
        var digits = new string(text.TrimStart().TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
    }
}
