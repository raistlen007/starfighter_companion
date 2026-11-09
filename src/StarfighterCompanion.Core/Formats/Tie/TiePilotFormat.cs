using StarfighterCompanion.Core.Binary;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Formats.Tie;

/// <summary>
/// Star Wars: TIE Fighter pilot file (<c>.tfr</c>): two identical 1928-byte records (current + backup).
/// Layout from OpenTIE (src/tie/shipext.h, src/tie_runtime/storage/pilot_storage.c), GPL-3.0.
/// The same layout is used by the 1994 floppy, 1995 CD-ROM and 1998 Windows editions and by OpenTIE.
/// </summary>
public sealed class TiePilotFormat : PilotFormat
{
    public const int RecordSize = 1928;
    public const int ShipCount = 12;
    public const int BattleCount = 20;
    public const int SpeciesCount = 69;

    private static readonly Lazy<RecordSchema> SchemaInstance = new(BuildSchema);

    public override string Game => "Star Wars: TIE Fighter";

    public override string Name => "TIE Fighter pilot (all editions)";

    public override IReadOnlyList<string> Extensions { get; } = new[] { ".tfr" };

    public override string Signature => $"{RecordSize:N0} or {RecordSize * 2:N0} bytes";

    public static RecordSchema Schema => SchemaInstance.Value;

    public override bool Matches(string fileName, ReadOnlySpan<byte> data) =>
        HasExtension(fileName, Extensions) && (data.Length == RecordSize || data.Length == RecordSize * 2);

    public override PilotDocument Open(string fileName, byte[] data) =>
        new TiePilotDocument(this, fileName, data, data.Length / RecordSize);

    // Training/Combat Chamber ship slots, not the 69-species kill-statistics index.
    // OpenTIE shipext_Find_Mission_Ship confirms slots 1-7; 8-12 remain unknown.
    private static readonly string[] TrainingShips =
    {
        "TIE Fighter", "TIE Interceptor", "TIE Bomber", "TIE Advanced",
        "Assault Gunboat", "TIE Defender", "Missile Boat",
    };

    private static string TrainingShip(int i) =>
        i < TrainingShips.Length ? $"{TrainingShips[i]} (Craft {i + 1})" : $"Craft {i + 1}";

    internal static readonly string[] Ranks = { "Cadet", "Officer", "Lieutenant", "Captain", "Commander", "General" };

    // Spec table order, from OpenTIE src/tie_runtime/flight_assets/species_names.c.
    internal static readonly string[] Species =
    {
        "X-wing", "Y-wing", "A-wing", "B-wing", "TIE Fighter", "TIE Interceptor", "TIE Bomber", "TIE Advanced",
        "TIE Defender", "(unused 9)", "(unused 10)", "Missile Boat", "T-wing", "Z-95 Headhunter", "R-41 Starchaser",
        "Assault Gunboat", "Shuttle", "Escort Shuttle", "Patrol Craft", "Scout Craft", "Transport",
        "Assault Transport", "Escort Transport", "Tug", "Utility Tug", "Container A", "Container B", "Container C",
        "Container D", "Heavy Lifter", "(unused 30)", "Freighter", "Cargo Ferry", "Modular Conveyor",
        "Container Transport", "(unused 35)", "Muurian Transport", "Corellian Transport", "(unused 38)",
        "Corellian Corvette", "Modified Corvette", "Nebulon-B Frigate", "Nebulon-B2 Frigate", "C-3 Passenger Liner",
        "Carrack Cruiser", "Strike Cruiser", "Escort Carrier", "Dreadnaught", "Calamari Cruiser",
        "Light Calamari Cruiser", "Interdictor", "Victory Star Destroyer", "Imperial Star Destroyer",
        "Super Star Destroyer", "Container E", "Container F", "Container G", "Container H", "Container I",
        "Platform 1", "Platform 2", "Platform 3", "Platform 4", "Platform 5", "Platform 6", "Asteroid R&D Station",
        "Asteroid Laser Battery", "Asteroid Warhead Launcher", "X/7 Factory",
    };

    internal static readonly IReadOnlyList<ChoiceOption> BattleStatus =
        Choices.Indexed("Not started", "Available / in progress", "Failed", "Completed");

    private static RecordSchema BuildSchema()
    {
        var b = new LayoutBuilder();
        var positive = FieldOptions.NonNegative;

        using (b.Group("Pilot"))
        {
            b.U8("version", "Record version", new FieldOptions { ReadOnly = true, Help = "Always 1." });
            b.Skip(1); // +0x01 last mission exit code (written by the game)
            b.U8("rank", "Rank", FieldOptions.OneOf(Choices.Indexed(Ranks)));
            b.U8("difficulty", "Difficulty", FieldOptions.OneOf(Choices.Indexed("Easy", "Medium", "Hard")));
            b.I32("score", "Career score", positive);
            b.U16("avgScore", "Average score", positive);
            b.U8("secretOrder", "Emperor's Secret Order rank", FieldOptions.OneOf(
                Choices.Numbered(10, i => i == 0 ? "Not a member" : $"Rank {i}")));
            b.Skip(1);
            b.U16("secretCompletions", "Secret objectives completed", positive);
            b.I32("secretScore", "Secret Order score", positive);
            b.Skip(10);
        }

        b.Expect(0x1C, "training");
        b.Skip(1); // current training craft (UI selection)

        using (b.Group("Training simulator"))
        {
            b.Array("training.level", BinaryKind.UInt8, ShipCount, i => $"{TrainingShip(i)} current level", FieldOptions.Range(0, 255));
            b.Skip(1);
            b.Array("training.score", BinaryKind.Int32, ShipCount, i => $"{TrainingShip(i)} high score", positive);
            b.Array("training.maxLevel", BinaryKind.UInt8, ShipCount, i => $"{TrainingShip(i)} highest level reached", FieldOptions.Range(0, 255));
        }

        b.Expect(0x66, "combat chamber");
        b.Skip(1);          // current combat craft
        b.Skip(ShipCount + BattleCount); // course cursors (UI selection)
        b.Skip(1);

        using (b.Group("Combat chamber"))
        {
            for (var s = 0; s < ShipCount; s++)
            {
                var ship = s;
                using (b.Group(TrainingShip(ship)))
                {
                    b.Array($"combat.score[{ship}]", BinaryKind.Int32, 8, i => $"Course {i + 1} high score", positive);
                }
            }

            for (var s = 0; s < ShipCount; s++)
            {
                var ship = s;
                using (b.Group(TrainingShip(ship)))
                {
                    b.Array($"combat.complete[{ship}]", BinaryKind.UInt8, 8, i => $"Course {i + 1} completed", FieldOptions.AsFlag);
                }
            }
        }

        b.Expect(0x268, "battles");

        using (b.Group("Battles"))
        {
            b.U8("battle.current", "Current battle", FieldOptions.OneOf(Choices.Numbered(BattleCount, i => $"Battle {i + 1}")));
            b.Array("battle.status", BinaryKind.UInt8, BattleCount, i => $"Battle {i + 1} status", FieldOptions.OneOf(BattleStatus));
            b.Array("battle.mission", BinaryKind.UInt8, BattleCount, i => $"Battle {i + 1} current mission",
                FieldOptions.Range(0, 8, "Mission reached within the battle."));
            b.Skip(256); // mission linked data (game scratch)
            b.Array("battle.secretBits", BinaryKind.UInt8, BattleCount, i => $"Battle {i + 1} secret objectives (bit per mission)",
                FieldOptions.Range(0, 255, "Bit 0 = mission 1 ... bit 7 = mission 8."));
            b.Array("battle.bonusBits", BinaryKind.UInt8, BattleCount, i => $"Battle {i + 1} bonus objectives (bit per mission)",
                FieldOptions.Range(0, 255, "Bit 0 = mission 1 ... bit 7 = mission 8."));
            b.Skip(29 + 4);

            for (var bt = 0; bt < BattleCount; bt++)
            {
                var battle = bt;
                using (b.Group($"Battle {battle + 1} scores"))
                {
                    b.Array($"battle.score[{battle}]", BinaryKind.Int32, 8, i => $"Mission {i + 1} high score", positive);
                }
            }
        }

        b.Expect(0x65A, "statistics");

        using (b.Group("Statistics"))
        {
            b.U16("stats.kills", "Total kills", positive);
            b.U16("stats.captures", "Total captures", positive);
            b.Skip(2);
        }

        using (b.Group("Statistics/Kills by craft"))
        {
            b.Array("stats.killsByType", BinaryKind.UInt16, SpeciesCount, i => Species[i], positive);
        }

        using (b.Group("Statistics/Captures by craft"))
        {
            b.Array("stats.capturesByType", BinaryKind.UInt16, SpeciesCount, i => Species[i], positive);
        }

        using (b.Group("Statistics"))
        {
            b.I32("stats.laserShots", "Laser shots fired", positive);
            b.I32("stats.laserHits", "Laser hits", positive);
            b.Skip(4);
            b.U16("stats.warheadsFired", "Warheads fired", positive);
            b.U16("stats.warheadHits", "Warhead hits", positive);
            b.Skip(2);
            b.U16("stats.ejections", "Ejections", positive);
        }

        return b.Build(RecordSize);
    }
}

internal sealed class TiePilotDocument : BinaryPilotDocument
{
    private readonly TiePilotFormat _format;

    public TiePilotDocument(TiePilotFormat format, string fileName, byte[] data, int copies)
        : base(data, TiePilotFormat.Schema, copies)
    {
        _format = format;
        PilotName = Path.GetFileNameWithoutExtension(fileName);
        Notes.Add("TIE Fighter stores the pilot's name as the file name, so it cannot be changed here.");
    }

    public override string GameName => _format.Game;

    public override string FormatName => _format.Name;

    public override string PilotName { get; }

    protected override IEnumerable<ValidationIssue> CheckRules()
    {
        if (Int("version") != 1)
        {
            yield return CommonRules.Odd($"Record version is {Int("version")}; TIE Fighter always writes 1.", Field("version"));
        }

        foreach (var issue in CommonRules.HitsVsShots(this, "stats.laserShots", "stats.laserHits", null, "laser"))
        {
            yield return issue;
        }

        foreach (var issue in CommonRules.HitsVsShots(this, "stats.warheadsFired", "stats.warheadHits", null, "warhead"))
        {
            yield return issue;
        }

        foreach (var issue in CommonRules.TotalVsBreakdown(this, "stats.kills", "stats.killsByType", TiePilotFormat.SpeciesCount, "kills"))
        {
            yield return issue;
        }

        foreach (var issue in CommonRules.TotalVsBreakdown(this, "stats.captures", "stats.capturesByType", TiePilotFormat.SpeciesCount, "captures"))
        {
            yield return issue;
        }

        for (var t = 0; t < TiePilotFormat.ShipCount; t++)
        {
            if (Int($"training.level[{t}]") > Int($"training.maxLevel[{t}]") + 1)
            {
                yield return CommonRules.Odd(
                    $"Training craft {t + 1}: current level is beyond the highest level reached.", Field($"training.level[{t}]"));
            }
        }

        var maxBattles = Edition?.MaxBattles ?? 13;
        var current = Field("battle.current");
        if (current.GetInteger() >= maxBattles)
        {
            yield return CommonRules.Broken(
                Edition is null
                    ? $"Battle {current.GetInteger() + 1} does not exist; TIE Fighter has 13 battles."
                    : $"Battle {current.GetInteger() + 1} does not exist in the {Edition.Name} (it has battles 1-{maxBattles}{(maxBattles == 10 ? ", 8-10 with Defender of the Empire" : string.Empty)}).",
                current);
        }
        else if (Edition is { } edition && current.GetInteger() >= edition.BaseBattles && Int($"battle.status[{current.GetInteger()}]") != 0)
        {
            yield return CommonRules.Odd(
                $"Battle {current.GetInteger() + 1} needs the Defender of the Empire add-on in the {edition.Name}.", current);
        }

        for (var b = maxBattles; b < TiePilotFormat.BattleCount; b++)
        {
            if (Int($"battle.status[{b}]") != 0)
            {
                yield return CommonRules.Odd(
                    $"Battle {b + 1} has progress, but {(Edition is null ? "TIE Fighter" : "the " + Edition.Name)} has no such battle; the game ignores it.",
                    Field($"battle.status[{b}]"));
            }
        }

        if (Int("secretOrder") == 0 && Int("secretScore") > 0)
        {
            yield return CommonRules.Odd("The pilot has a Secret Order score but is not a member of the Secret Order.", Field("secretOrder"));
        }
    }
}
