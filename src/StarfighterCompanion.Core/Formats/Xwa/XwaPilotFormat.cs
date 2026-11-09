using StarfighterCompanion.Core.Binary;
using StarfighterCompanion.Core.Formats.XvT;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Formats.Xwa;

/// <summary>
/// X-Wing Alliance pilot file (<c>.plt</c>, 152,076 bytes). Also used by the X-Wing Alliance Upgrade (XWAUP),
/// the TIE Fighter Total Conversion (TFTC) and OpenXWA.
/// Layout from OpenXWA (PilotData in src/xwa/frontend/mission_setup.h), GPL-3.0.
/// </summary>
public sealed class XwaPilotFormat : PilotFormat
{
    public const int RecordSize = 152076;
    public const int CraftTypes = 512;
    public const int FlightGroups = 192;

    private static readonly Dictionary<string, RecordSchema> Schemas = new();

    public override string Game => "Star Wars: X-Wing Alliance";

    public override string Name => "X-Wing Alliance pilot (.plt) - also XWAUP / TFTC";

    public override IReadOnlyList<string> Extensions { get; } = new[] { ".plt" };

    public override string Signature => $"{RecordSize:N0} bytes";

    /// <summary>The schema with the original XWA craft names.</summary>
    public static RecordSchema Schema => SchemaFor(XwaEdition.For("xwa"));

    /// <summary>Statistics are kept separately for these kinds of mission (OpenXWA fediskio.c, statType).</summary>
    internal static readonly string[] StatTypes = { "Campaign battles", "Family missions", "Outside the campaign" };

    /// <summary>The four career records (OpenXWA PilotData.factionStatistics; currentFactionId).</summary>
    internal static readonly string[] Records =
        { "Career: campaign battles", "Unused record", "Career: outside the campaign", "Career: family missions" };

    /// <summary>The kind of mission each career record collects (-1: none).</summary>
    internal static readonly int[] RecordStatType = { 0, -1, 2, 1 };

    private static RecordSchema SchemaFor(XwaEdition edition)
    {
        lock (Schemas)
        {
            if (!Schemas.TryGetValue(edition.Id, out var schema))
            {
                schema = BuildSchema(edition.CraftLabel);
                Schemas[edition.Id] = schema;
            }

            return schema;
        }
    }

    public override bool Matches(string fileName, ReadOnlySpan<byte> data) =>
        HasExtension(fileName, Extensions) && data.Length == RecordSize;

    public override PilotDocument Open(string fileName, byte[] data) => Open(fileName, data, null);

    public override PilotDocument Open(string fileName, byte[] data, string? variantId)
    {
        var edition = XwaEdition.For(variantId);
        return new XwaPilotDocument(this, data, SchemaFor(edition), edition);
    }

    private static RecordSchema BuildSchema(Func<int, string> craft)
    {
        var b = new LayoutBuilder();
        var positive = FieldOptions.NonNegative;

        XvtCommon.Header(b); // name, total score + 5 ints (same size as XWA's gap/unk/players/mode block)
        b.Skip(4 + 4 + 4 * 7); // team, mission directory, selected mission per directory

        using (b.Group("Multiplayer"))
        {
            b.Text("gameName", "Game name", 32);
            b.Text("hostName", "Host name", 32);
        }

        b.Skip(4);

        using (b.Group("Rating"))
        {
            b.I32("promoPoints", "Promotion points", positive);
            b.I32("demotionPoints", "Demotion points", positive);
            b.Skip(4); // pending promotion announcement
            b.I32("nextPromotionPercent", "Progress to next rating (%)", FieldOptions.Range(0, 100));
        }

        using (b.Scope("main", "Career statistics"))
        {
            Stats(b, craft);
        }

        XvtCommon.LastMission(b, FlightGroups);
        b.Skip(20076); // object statistics of the last mission
        b.Expect(44282, "tour of duty missions");

        MissionRecords(b, "tod", "Campaign (Tour of Duty) missions");
        MissionRecords(b, "combat", "Combat Chamber missions");

        using (b.Group("Tech library"))
        {
            b.Array("craftKnown", BinaryKind.UInt8, CraftTypes, i => $"{craft(i)} unlocked", FieldOptions.AsFlag);
        }

        b.Skip(4); // "new craft added to tech room" announcement

        using (b.Group("Rating"))
        {
            b.I32("rating", "Rating", FieldOptions.Range(0, 24, "0 to 24. The game keeps one mission counter per rating, so higher values would corrupt the file."));
            b.I32("rank", "Rank", FieldOptions.Range(0, 8, "0 to 8."));
            b.I32("kalidor", "Kalidor Crescent level", FieldOptions.Range(0, 6));
            b.I32("missionsPlayed", "Missions played", positive);
            b.Array("missionsPerRating", BinaryKind.Int32, 25, i => $"Missions played at rating {i}", positive);
            b.Text("ratingName", "Rating name", 32, new FieldOptions { Help = "Text shown for the rating; normally set by the game." });
        }

        b.Skip(4 * 4);  // pending announcements (Emkay, family, tactical officer)
        b.Skip(100 * 8); // network players of the last mission
        b.Skip(28 * 10); // team results of the last mission
        b.Skip(4);       // selected faction record
        b.Expect(70526, "faction records");

        b.Repeat(4, "faction", i => Records[i], _ =>
        {
            using (b.Group("Summary"))
            {
                b.I32("missionsPlayed", "Missions played", positive);
            }

            b.Skip(4 + 4 + 4 * 7 + 32 + 4);
            using (b.Group("Awards"))
            {
                var awardHelp = new FieldOptions
                {
                    Min = 0,
                    Help = "How many missions currently hold this award level. The game keeps it in step with each mission's Award.",
                };
                b.Array("awardsA", BinaryKind.Int32, 6, i => $"Melee missions with award level {i + 1}", awardHelp);
                b.Skip(24);
                b.Array("awardsB", BinaryKind.Int32, 6, i => $"Other missions with award level {i + 1}", awardHelp);
                b.Skip(24);
                b.Array("kalidorPoints", BinaryKind.Int32, 4,
                    i => new[] { "Kalidor Crescent award, last multiplayer mission", "Unused value 1", "Kalidor Crescent award, last mission", "Unused value 2" }[i],
                    new FieldOptions { Min = 0, Help = "Set by the game after each mission and reset at the start of the next." });
            }

            b.Skip(12);
            using (b.Group("Summary"))
            {
                b.I32("score", "Score");
                b.Skip(4);
                b.I32("bonusScore", "Bonus score");
                b.I32("totalScore", "Total score");
            }

            using (b.Scope("stats", "Statistics"))
            {
                Stats(b, craft);
            }
        });

        b.Expect(151694, "e-mails");
        b.Skip(100 + 1); // e-mail read status and sort order

        using (b.Group("Campaign"))
        {
            b.Skip(4); // campaign mode
            b.I32("skipsRemaining", "Mission skips remaining", FieldOptions.Range(0, 3));
        }

        b.Skip(4 + 256 + 8 + 4 + 1); // regions, mission file name, proving grounds per player, melee index, hangar type

        return b.Build(RecordSize);
    }

    private static void Stats(LayoutBuilder b, Func<int, string> craft)
    {
        // Each statistic is stored three times, once per kind of mission (StatTypes); each kind gets its own sub-tree.
        void PerType(string id, string label, FieldOptions o)
        {
            for (var t = 0; t < 3; t++)
            {
                using (b.Group(StatTypes[t]))
                {
                    b.I32($"{id}[{t}]", label, o);
                }
            }
        }

        void PerTypeArray(string id, string label, int count, Func<int, string> item)
        {
            for (var t = 0; t < 3; t++)
            {
                using (b.Group($"{StatTypes[t]}/{label}"))
                {
                    b.Array($"{id}[{t}]", BinaryKind.Int32, count, item, FieldOptions.NonNegative);
                }
            }
        }

        PerType("scoreByType", "Score", FieldOptions.None);

        // Counted by the mission list the mission was started from, not by kind of mission (OpenXWA fediskio.c).
        var played = new FieldOptions
        {
            Min = 0,
            Help = "Counted by the list the mission was started from. Campaign and skirmish missions are not counted here.",
        };
        b.Array("played", BinaryKind.Int32, 3, i => $"Missions played from the {new[] { "Missions", "Melee", "Combat" }[i]} list", played);

        PerType("kills", "Kills", FieldOptions.NonNegative);
        PerType("friendlyKills", "Friendly craft killed", FieldOptions.NonNegative);
        PerTypeArray("killsByCraft", "Kills by craft", CraftTypes, craft);
        PerTypeArray("sharedKillsByCraft", "Shared kills by craft", CraftTypes, craft);
        PerTypeArray("assistsByCraft", "Assists by craft", CraftTypes, craft);
        PerTypeArray("killsOnPlayerRating", "Kills on players by rating", 25, i => $"Rating {i}");
        PerTypeArray("sharedKillsOnPlayerRating", "Shared kills on players by rating", 25, i => $"Rating {i}");
        PerTypeArray("assistsOnPlayerRating", "Assists on players by rating", 25, i => $"Rating {i}");
        PerTypeArray("killsOnAiRating", "Kills on AI by skill", 6, i => XvtCommon.AiRatings[i]);
        PerTypeArray("sharedKillsOnAiRating", "Shared kills on AI by skill", 6, i => XvtCommon.AiRatings[i]);
        PerTypeArray("assistsOnAiRating", "Assists on AI by skill", 6, i => XvtCommon.AiRatings[i]);
        PerType("inspections", "Special craft inspected", FieldOptions.NonNegative);
        PerType("energyHits", "Laser/ion hits", FieldOptions.NonNegative);
        PerType("energyFired", "Laser/ion shots fired", FieldOptions.NonNegative);
        PerType("warheadHits", "Warhead hits", FieldOptions.NonNegative);
        PerType("warheadsFired", "Warheads fired", FieldOptions.NonNegative);
        PerType("losses", "Craft lost", FieldOptions.NonNegative);
        PerType("lossesCollision", "Craft lost in collisions", FieldOptions.NonNegative);
        PerType("lossesStarship", "Craft lost to starships", FieldOptions.NonNegative);
        PerType("lossesMine", "Craft lost to mines", FieldOptions.NonNegative);
        PerTypeArray("killedByPlayerRating", "Killed by players by rating", 25, i => $"Rating {i}");
        PerTypeArray("killedByAiRating", "Killed by AI by skill", 6, i => XvtCommon.AiRatings[i]);
    }

    private static void MissionRecords(LayoutBuilder b, string id, string group)
    {
        var positive = FieldOptions.NonNegative;
        using (b.Group(group))
        {
            b.Repeat(255, id, _ => null, i =>
            {
                var m = $"Mission {i}";
                b.Skip(4);
                b.I32("flown", $"{m}: times flown", positive);
                b.I32("first", $"{m}: 1st places", positive);
                b.I32("second", $"{m}: 2nd places", positive);
                b.I32("third", $"{m}: 3rd places", positive);
                b.I32("completed", $"{m}: times completed", positive);
                b.I32("failed", $"{m}: times failed", positive);
                b.I32("bestScore", $"{m}: best score");
                b.U32("bestTime", $"{m}: best time");
                b.U32("bestPlacement", $"{m}: best placement");
                b.U32("award", $"{m}: award");
                b.U32("bestBonus", $"{m}: best bonus");
            });
        }
    }
}

internal sealed class XwaPilotDocument : BinaryPilotDocument
{
    private readonly XwaPilotFormat _format;

    private readonly XwaEdition _edition;

    public XwaPilotDocument(XwaPilotFormat format, byte[] data, RecordSchema schema, XwaEdition edition)
        : base(data, schema, 1)
    {
        _format = format;
        _edition = edition;
        Notes.Add(edition.Id == "xwa"
            ? "Craft names use the original X-Wing Alliance ship list; modded installations may use different names."
            : "Craft names come from this version's ship list.");
        if (edition.Campaign is null)
        {
            Notes.Add("Campaign missions are listed by their number in the pilot file; this version's mission list is not included yet.");
        }

        Arrange(edition.Campaign, edition.Combat);
    }

    /// <summary>For tests: a document with explicit mission lists.</summary>
    internal XwaPilotDocument(XwaPilotFormat format, byte[] data, RecordSchema schema, IReadOnlyList<XwaMissionListEntry>? campaign,
        IReadOnlyList<XwaMissionListEntry>? combat)
        : base(data, schema, 1)
    {
        _format = format;
        _edition = XwaEdition.For("xwa");
        Arrange(campaign, combat);
    }

    private static readonly string[] RecordFields =
        { "flown", "completed", "failed", "first", "second", "third", "bestScore", "bestTime", "bestPlacement", "award", "bestBonus" };

    private static readonly Dictionary<string, string> RecordLabels = new()
    {
        ["flown"] = "Times flown", ["first"] = "1st places", ["second"] = "2nd places", ["third"] = "3rd places",
        ["completed"] = "Times completed", ["failed"] = "Times failed", ["bestScore"] = "Best score", ["bestTime"] = "Best time",
        ["bestPlacement"] = "Best placement", ["award"] = "Award", ["bestBonus"] = "Best bonus",
    };

    private static readonly System.Text.RegularExpressions.Regex StatId =
        new(@"^(?:main|faction\[(\d)\]\.stats)\.([A-Za-z]+)\[(\d)\](?:\[(\d+)\])?$");

    private void Arrange(IReadOnlyList<XwaMissionListEntry>? campaign, IReadOnlyList<XwaMissionListEntry>? combat)
    {
        var layout = BeginLayout();

        // Career progression first.
        foreach (var field in Fields.Where(f => !f.Id.StartsWith("tod[", StringComparison.Ordinal) &&
                                                !f.Id.StartsWith("combat[", StringComparison.Ordinal) &&
                                                !f.Id.StartsWith("faction[", StringComparison.Ordinal) &&
                                                !f.Id.StartsWith("craftKnown[", StringComparison.Ordinal) &&
                                                f.Id != "skipsRemaining"))
        {
            if (Wanted(field))
            {
                layout.Show(field, field.Group);
            }
        }

        // Campaign: progression (mission skips) at the root, then one sub-tree per battle and per mission.
        layout.Show("skipsRemaining", "Campaign");
        Missions(layout, "tod", "Campaign", campaign);
        Missions(layout, "combat", "Combat chamber", combat);

        foreach (var field in Fields.Where(f => f.Id.StartsWith("craftKnown[", StringComparison.Ordinal)))
        {
            if (Wanted(field))
            {
                layout.Show(field, field.Group);
            }
        }

        // Career records: each collects one kind of mission, so its statistics are shown without the per-kind level;
        // the other kinds (normally empty) and the unused record appear only when they hold data.
        for (var r = 0; r < 4; r++)
        {
            var record = Fields.Where(f => f.Id.StartsWith($"faction[{r}].", StringComparison.Ordinal)).ToList();
            var type = XwaPilotFormat.RecordStatType[r];
            if (type < 0 && record.All(f => f.Editor == FieldEditor.Text || f.GetInteger() == 0))
            {
                continue;
            }

            foreach (var field in record)
            {
                if (!Wanted(field))
                {
                    continue;
                }

                var m = StatId.Match(field.Id);
                if (type >= 0 && m.Success && m.Groups[3].Value == type.ToString())
                {
                    layout.Show(field, field.Group.Replace($"/{XwaPilotFormat.StatTypes[type]}", "", StringComparison.Ordinal));
                }
                else if (type < 0 || !m.Success || m.Groups[2].Value == "played" || field.GetInteger() != 0)
                {
                    layout.Show(field, field.Group);
                }
            }
        }
    }

    /// <summary>Craft types the version does not have are shown only when they hold a value.</summary>
    private bool Wanted(EditableField field)
    {
        int craft;
        if (field.Id.StartsWith("craftKnown[", StringComparison.Ordinal))
        {
            craft = int.Parse(field.Id.AsSpan(11, field.Id.Length - 12));
        }
        else if (field.Id.Contains("ByCraft[", StringComparison.Ordinal) && StatId.Match(field.Id) is { Success: true } m)
        {
            craft = int.Parse(m.Groups[4].Value);
        }
        else
        {
            return true;
        }

        return _edition.CraftName(craft) is not null || field.GetInteger() != 0;
    }

    private void Missions(DisplayLayout layout, string prefix, string root, IReadOnlyList<XwaMissionListEntry>? list)
    {
        void ShowMission(int index, string group)
        {
            foreach (var name in RecordFields)
            {
                layout.Show($"{prefix}[{index}].{name}", group, RecordLabels[name]);
            }
        }

        bool Empty(int index) => layout.AllZero(RecordFields.Select(n => $"{prefix}[{index}].{n}"));

        if (list is null)
        {
            for (var i = 0; i < 255; i++)
            {
                ShowMission(i, $"{root}/Mission {i}");
            }

            return;
        }

        var listed = new HashSet<int>();
        var sections = new Dictionary<string, int>();
        foreach (var entry in list)
        {
            if (!listed.Add(entry.MissionIndex))
            {
                continue; // a mission listed twice keeps its first place
            }

            var section = string.IsNullOrWhiteSpace(entry.Section) ? "Missions" : Clean(entry.Section);
            var title = Clean(entry.Title.Length > 0 && !entry.Title.StartsWith('!') ? entry.Title : Path.GetFileNameWithoutExtension(entry.FileName));
            sections[section] = sections.GetValueOrDefault(section) + 1;
            ShowMission(entry.MissionIndex, $"{root}/{section}/{sections[section]}. {title}");
        }

        // Slots the list does not mention are shown only when they hold data.
        for (var i = 0; i < 255; i++)
        {
            if (!listed.Contains(i) && !Empty(i))
            {
                ShowMission(i, $"{root}/Not in the mission list/Mission {i}");
            }
        }
    }

    /// <summary>Group names use "/" as the separator.</summary>
    private static string Clean(string text) => text.Replace('/', '-').Trim();

    public override string GameName => _format.Game;

    public override string FormatName => _format.Name;

    public override string PilotName => Field("name").GetText();

    protected override IEnumerable<ValidationIssue> CheckRules()
    {
        if (string.IsNullOrWhiteSpace(PilotName))
        {
            yield return CommonRules.Odd("The pilot has no name.", Field("name"));
        }

        foreach (var issue in XvtCommon.MissionRules(this, "tod", 255))
        {
            yield return issue;
        }

        foreach (var issue in XvtCommon.MissionRules(this, "combat", 255))
        {
            yield return issue;
        }
    }
}
