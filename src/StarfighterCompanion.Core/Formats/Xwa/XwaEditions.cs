using System.Reflection;
using System.Text;

namespace StarfighterCompanion.Core.Formats.Xwa;

/// <summary>
/// What differs between the XWA versions a pilot can belong to: craft names and mission lists. All of it is built
/// into Starfighter Companion (Data/CraftLists and Data/MissionLists); they are not read from the game.
/// </summary>
internal sealed class XwaEdition
{
    private static readonly Dictionary<string, XwaEdition> Cache = new();

    private XwaEdition(string id, string? craftList, string missionList, string? combatList)
    {
        Id = id;
        CraftNames = craftList is null ? OriginalXwaCraftNames.All : LoadCraftNames(craftList);
        Campaign = XwaMissionList.BuiltIn(missionList);
        Combat = combatList is null ? Campaign : XwaMissionList.BuiltIn(combatList);
    }

    public string Id { get; }

    /// <summary>Craft names by craft type; null where the version has no (visible) craft.</summary>
    public IReadOnlyList<string?> CraftNames { get; }

    public IReadOnlyList<XwaMissionListEntry>? Campaign { get; }

    /// <summary>
    /// The Combat Chamber list. In XWA and the Upgrade it is the campaign list (the game records a mission replayed
    /// outside the campaign under the same number); TFTC has its own COMBAT\MISSION.LST.
    /// </summary>
    public IReadOnlyList<XwaMissionListEntry>? Combat { get; }

    public string? CraftName(int id) => id >= 0 && id < CraftNames.Count ? CraftNames[id] : null;

    public string CraftLabel(int id) => CraftName(id) is { } name ? $"{name} (craft type {id})" : $"Craft type {id}";

    /// <summary>The edition for a library version id; unknown ids and no id mean the original game.</summary>
    public static XwaEdition For(string? variantId)
    {
        var id = variantId switch
        {
            "xwaup" or "xwaup2020" or "embers" or "tftc" or "tftcr" => variantId,
            _ => "xwa",
        };

        lock (Cache)
        {
            if (!Cache.TryGetValue(id, out var edition))
            {
                edition = id switch
                {
                    "xwaup" => new XwaEdition(id, "xwaup", "xwa", null),
                    "xwaup2020" => new XwaEdition(id, "xwaup2020", "xwa", null),
                    "embers" => new XwaEdition(id, "embers", "embers", null),
                    "tftc" => new XwaEdition(id, "tftc", "tftc", "tftc.combat"),
                    "tftcr" => new XwaEdition(id, "tftc", "tftcr", "tftc.combat"),
                    _ => new XwaEdition(id, null, "xwa", null),
                };
                Cache[id] = edition;
            }

            return edition;
        }
    }

    private static string?[] LoadCraftNames(string list)
    {
        var assembly = typeof(XwaEdition).Assembly;
        var resource = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith($"CraftLists.{list}.txt", StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var names = new string?[XwaPilotFormat.CraftTypes];
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('\t');
            if (line.StartsWith("//", StringComparison.Ordinal) || tab < 0 || !int.TryParse(line[..tab], out var id) ||
                id < 0 || id >= names.Length)
            {
                continue;
            }

            names[id] = line[(tab + 1)..].Trim();
        }

        return names;
    }
}
