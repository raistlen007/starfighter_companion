using StarfighterCompanion.Core.Binary;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Formats.XvT;

/// <summary>
/// X-Wing vs. TIE Fighter pilot file (<c>.plt</c>, 253,754 bytes).
/// Layout from OpenXvT (PilotXvtRecord in src/xvt/frontend/pilot.c), GPL-3.0.
/// </summary>
public sealed class XvtPilotFormat : PilotFormat
{
    public const int RecordSize = 0x3DF3A;
    public const int Crafts = 88;

    private static readonly Lazy<RecordSchema> SchemaInstance = new(BuildSchema);

    public override string Game => "Star Wars: X-Wing vs. TIE Fighter";

    public override string Name => "X-Wing vs. TIE Fighter pilot (.plt)";

    public override IReadOnlyList<string> Extensions { get; } = new[] { ".plt" };

    public override string Signature => $"{RecordSize:N0} bytes";

    public static RecordSchema Schema => SchemaInstance.Value;

    public override bool Matches(string fileName, ReadOnlySpan<byte> data) =>
        HasExtension(fileName, Extensions) && data.Length == RecordSize;

    public override PilotDocument Open(string fileName, byte[] data) => new XvtPilotDocument(this, data, Schema, isBop: false);

    private static RecordSchema BuildSchema()
    {
        var b = new LayoutBuilder();
        XvtCommon.Header(b);
        b.Skip(320 + 32 + 320); // opaque payloads round-tripped by the game
        b.Skip(100);            // legacy rating state
        Promotion(b);

        using (b.Scope("main", "Career statistics"))
        {
            XvtCommon.Stats(b, Crafts);
        }

        b.Skip(3348); // mission sequence state (in-progress melee/tournament/battle)
        Rating(b);
        XvtCommon.LastMission(b, 48);
        b.Skip(4824);    // object statistics of the last mission
        b.Skip(88 * 8);  // network players of the last mission
        b.Skip(28 * 10); // team results of the last mission
        b.Skip(4);       // selected faction record

        b.Repeat(4, "faction", XvtCommon.FactionLabel, _ =>
        {
            using (b.Group("Summary"))
            {
                b.I32("missionsPlayed", "Missions played", FieldOptions.NonNegative);
            }

            b.Skip(68);
            XvtCommon.Awards(b);
            b.Skip(16);
            using (b.Group("Summary"))
            {
                b.I32("totalScore", "Total score");
            }

            using (b.Scope("stats", "Statistics"))
            {
                XvtCommon.Stats(b, Crafts);
            }

            XvtCommon.MissionRecords(b, "spTraining", "Single player/Training missions", 100);
            XvtCommon.MissionRecords(b, "spMelee", "Single player/Melee missions", 250);
            XvtCommon.MissionRecords(b, "spCombat", "Single player/Combat missions", 250);
            XvtCommon.MultiplayerMissionRecords(b, "mpTraining", "Multiplayer/Training missions", 100);
            XvtCommon.MultiplayerMissionRecords(b, "mpMelee", "Multiplayer/Melee missions", 250);
            XvtCommon.MultiplayerMissionRecords(b, "mpCombat", "Multiplayer/Combat missions", 250);
            XvtCommon.Tournaments(b, "spTournament", "Single player/Tournaments", multiplayer: false);
            XvtCommon.Tournaments(b, "mpTournament", "Multiplayer/Tournaments", multiplayer: true);
            XvtCommon.Battles(b, "spBattle", "Single player/Battles", multiplayer: false);
            XvtCommon.Battles(b, "mpBattle", "Multiplayer/Battles", multiplayer: true);
        });

        return b.Build(RecordSize);
    }

    internal static void Promotion(LayoutBuilder b)
    {
        using (b.Group("Rating"))
        {
            b.I32("promoPoints", "Promotion points", FieldOptions.NonNegative);
            b.I32("demotionPoints", "Demotion points", FieldOptions.NonNegative);
            b.I32("promotionDelta", "Last rating change", FieldOptions.OneOf(XvtCommon.PromotionChoices));
            b.I32("nextPromotionPercent", "Progress to next rating (%)", FieldOptions.Range(0, 100));
        }
    }

    internal static void Rating(LayoutBuilder b)
    {
        using (b.Group("Rating"))
        {
            b.I32("rating", "Rating", FieldOptions.OneOf(XvtCommon.RatingChoices));
            b.I32("missionsPlayed", "Missions played", FieldOptions.NonNegative);
            b.Array("missionsPerRating", BinaryKind.Int32, 25, i => $"Missions played as {XvtCommon.Ratings[i]}", FieldOptions.NonNegative);
            b.Text("ratingName", "Rating name", 32, new FieldOptions
            {
                ReadOnly = true,
                Help = "Kept in step with the rating automatically.",
            });
        }
    }
}

/// <summary>
/// X-Wing vs. TIE Fighter: Balance of Power pilot file (<c>.pl2</c>, 296,238 bytes).
/// Balance of Power also keeps an XvT-compatible <c>.plt</c> next to it with the same name.
/// Layout from OpenXvT (PilotData in src/xvt/frontend/pilot_record.h), GPL-3.0.
/// </summary>
public sealed class BopPilotFormat : PilotFormat
{
    public const int RecordSize = 296238;
    public const int Crafts = 100;

    private static readonly Lazy<RecordSchema> SchemaInstance = new(BuildSchema);

    public override string Game => "Star Wars: X-Wing vs. TIE Fighter - Balance of Power";

    public override string Name => "Balance of Power pilot (.pl2)";

    public override IReadOnlyList<string> Extensions { get; } = new[] { ".pl2" };

    public override string Signature => $"{RecordSize:N0} bytes";

    public static RecordSchema Schema => SchemaInstance.Value;

    public override bool Matches(string fileName, ReadOnlySpan<byte> data) =>
        HasExtension(fileName, Extensions) && data.Length == RecordSize;

    public override PilotDocument Open(string fileName, byte[] data) => new XvtPilotDocument(this, data, Schema, isBop: true);

    private static RecordSchema BuildSchema()
    {
        var b = new LayoutBuilder();
        XvtCommon.Header(b);
        b.Skip(672);              // XvT-compatible payload
        b.Skip(4 + 4 + 4 * 6);    // team, mission directory, selected mission per directory

        using (b.Group("Multiplayer"))
        {
            b.Text("gameName", "Game name", 32);
            b.Text("hostName", "Host name", 32);
        }

        b.Skip(4 + 4); // active sequence flag and id
        XvtPilotFormat.Promotion(b);

        using (b.Scope("main", "Career statistics"))
        {
            XvtCommon.Stats(b, Crafts);
        }

        b.Skip(256); // melee / tournament sequence state
        b.Skip(140); // battle sequence state
        XvtPilotFormat.Rating(b);
        XvtCommon.LastMission(b, 48);
        b.Skip(5256);    // object statistics of the last mission
        b.Skip(88 * 8);  // network players of the last mission
        b.Skip(28 * 10); // team results of the last mission
        b.Skip(4);       // selected faction record

        b.Repeat(4, "faction", XvtCommon.FactionLabel, _ =>
        {
            using (b.Group("Summary"))
            {
                b.I32("missionsPlayed", "Missions played", FieldOptions.NonNegative);
            }

            b.Skip(4 + 4 + 4 * 6 + 32 + 4 + 4);
            XvtCommon.Awards(b);
            b.Skip(16);
            using (b.Group("Summary"))
            {
                b.I32("totalScore", "Total score");
            }

            using (b.Scope("stats", "Statistics"))
            {
                XvtCommon.Stats(b, Crafts);
            }

            b.Skip(4);
            XvtCommon.MissionRecords(b, "spTraining", "Single player/Training missions", 100);
            XvtCommon.MissionRecords(b, "spMelee", "Single player/Melee missions", 250);
            XvtCommon.MissionRecords(b, "spCombat", "Single player/Combat missions", 250);
            XvtCommon.MultiplayerMissionRecords(b, "mpTraining", "Multiplayer/Training missions", 100);
            XvtCommon.MultiplayerMissionRecords(b, "mpMelee", "Multiplayer/Melee missions", 250);
            XvtCommon.MultiplayerMissionRecords(b, "mpCombat", "Multiplayer/Combat missions", 250);
            XvtCommon.Tournaments(b, "spTournament", "Single player/Tournaments", multiplayer: false);
            XvtCommon.Tournaments(b, "mpTournament", "Multiplayer/Tournaments", multiplayer: true);
            XvtCommon.Battles(b, "spBattle", "Single player/Battles", multiplayer: false);
            XvtCommon.Battles(b, "mpBattle", "Multiplayer/Battles", multiplayer: true);
            Campaigns(b, "spCampaign", "Single player/Campaigns");
            Campaigns(b, "mpCampaign", "Multiplayer/Campaigns");
            b.Skip(24 + 4); // unresolved bytes, CD movie check counter
            CampaignMissions(b, "spCampaignMission", "Single player/Campaign missions");
            b.Skip(32);
            CampaignMissions(b, "mpCampaignMission", "Multiplayer/Campaign missions");
        });

        b.Skip(24);       // active campaign sequence state
        b.Skip(25 * 160); // saved single player battles in progress
        b.Skip(25 * 160); // saved multiplayer battles in progress
        b.Skip(25 * 40);  // saved single player campaigns in progress
        b.Skip(25 * 40);  // saved multiplayer campaigns in progress

        return b.Build(RecordSize);
    }

    private static void Campaigns(LayoutBuilder b, string id, string group)
    {
        using (b.Group(group))
        {
            b.Repeat(25, id, _ => null, i =>
            {
                var m = $"Campaign {i + 1}";
                b.I32("attempts", $"{m}: attempts", FieldOptions.NonNegative);
                b.I32("nextMission", $"{m}: next mission", FieldOptions.NonNegative);
                b.I32("finished", $"{m}: finished", FieldOptions.AsFlag);
                b.I32("bestScore", $"{m}: best score");
                b.Skip(20);
            });
        }
    }

    private static void CampaignMissions(LayoutBuilder b, string id, string group)
    {
        using (b.Group(group))
        {
            b.Repeat(99, id, _ => null, i =>
            {
                var m = $"Mission {i + 1}";
                b.Skip(4); // campaign id
                b.I32("flown", $"{m}: times flown", FieldOptions.NonNegative);
                b.I32("awardEligible", $"{m}: award eligible", FieldOptions.AsFlag);
                b.U32("bestScore", $"{m}: best score");
                b.I32("award", $"{m}: award");
                b.I32("bestTime", $"{m}: best time", FieldOptions.NonNegative);
                b.I32("completed", $"{m}: completed", FieldOptions.AsFlag);
                b.Skip(4);
            });
        }
    }
}

internal sealed class XvtPilotDocument : BinaryPilotDocument
{
    private static readonly string[] MissionGroups = { "spTraining", "spMelee", "spCombat", "mpTraining", "mpMelee", "mpCombat" };
    private static readonly int[] MissionCounts = { 100, 250, 250, 100, 250, 250 };

    private readonly PilotFormat _format;

    public XvtPilotDocument(PilotFormat format, byte[] data, RecordSchema schema, bool isBop)
        : base(data, schema, 1)
    {
        _format = format;
        Notes.Add(isBop
            ? "Balance of Power keeps an X-Wing vs. TIE Fighter copy of this pilot in a .plt file with the same name. Edit both if you play both games."
            : "If you also own Balance of Power, it keeps its own copy of this pilot in a .pl2 file with the same name.");
    }

    public override string GameName => _format.Game;

    public override string FormatName => _format.Name;

    public override string PilotName => Field("name").GetText();

    protected override void AfterFieldChanged(EditableField field)
    {
        if (field.Id == "rating")
        {
            var rating = (int)field.GetInteger();
            if (rating >= 0 && rating < XvtCommon.Ratings.Length)
            {
                Field("ratingName").ForceText(XvtCommon.Ratings[rating]);
            }
        }
    }

    protected override IEnumerable<ValidationIssue> CheckRules()
    {
        if (string.IsNullOrWhiteSpace(PilotName))
        {
            yield return CommonRules.Odd("The pilot has no name.", Field("name"));
        }

        for (var f = 0; f < 4; f++)
        {
            for (var g = 0; g < MissionGroups.Length; g++)
            {
                foreach (var issue in XvtCommon.MissionRules(this, $"faction[{f}].{MissionGroups[g]}", MissionCounts[g]))
                {
                    yield return issue;
                }
            }
        }
    }
}
