using StarfighterCompanion.Core.Binary;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Formats.XWing;

public enum XWingEdition
{
    /// <summary>1998 Windows "Collector Series" (also the GOG/Steam 1998 edition and OpenXW).</summary>
    Windows98,

    /// <summary>1993 floppy and 1994 Collector's CD-ROM (DOS, also GOG/Steam 1994 and OpenXW).</summary>
    Dos,
}

/// <summary>
/// Star Wars: X-Wing pilot file (<c>.plt</c>).
/// Layout from OpenXW (src/xw/frontend/register.h, src/xw_runtime/storage/pilot_file.c), GPL-3.0.
/// The DOS editions reserve one extra byte before the tour block (87 instead of 86 bytes at 0x289).
/// The game may write the record twice in a row; readers use the first copy.
/// </summary>
public sealed class XWingPilotFormat : PilotFormat
{
    public const int WindowsRecordSize = 1705;
    public const int DosRecordSize = 1706;

    private readonly XWingEdition _edition;
    private readonly Lazy<RecordSchema> _schema;

    public XWingPilotFormat(XWingEdition edition)
    {
        _edition = edition;
        _schema = new Lazy<RecordSchema>(() => BuildSchema(edition));
    }

    public override string Game => "Star Wars: X-Wing";

    public override string Name => _edition == XWingEdition.Windows98
        ? "X-Wing pilot (1998 Windows edition)"
        : "X-Wing pilot (1993 floppy / 1994 CD-ROM DOS edition)";

    public override IReadOnlyList<string> Extensions { get; } = new[] { ".plt" };

    public int RecordSize => _edition == XWingEdition.Windows98 ? WindowsRecordSize : DosRecordSize;

    public override string Signature => $"{RecordSize:N0} or {RecordSize * 2:N0} bytes";

    public RecordSchema Schema => _schema.Value;

    public override bool Matches(string fileName, ReadOnlySpan<byte> data) =>
        HasExtension(fileName, Extensions) && (data.Length == RecordSize || data.Length == RecordSize * 2);

    public override PilotDocument Open(string fileName, byte[] data) =>
        new XWingPilotDocument(this, fileName, data, data.Length / RecordSize);

    internal static readonly string[] Ranks = { "Flight Cadet", "Flight Officer", "Lieutenant", "Captain", "Commander", "General" };

    internal static readonly string[] Ships = { "X-wing", "Y-wing", "A-wing", "B-wing" };

    // Order as used by XWVM's X-Wing pilot import (Assets/Code/GameState/PilotGameRecord.cs).
    internal static readonly string[] KillTypes =
    {
        "X-wing", "Y-wing", "A-wing", "TIE Fighter", "TIE Interceptor", "TIE Bomber", "Assault Gunboat",
        "Transport", "Shuttle", "Tug", "Container", "Freighter", "Calamari Cruiser", "Nebulon-B Frigate",
        "Corellian Corvette", "Imperial Star Destroyer", "TIE Advanced", "Mine (type 1)", "Mine (type 2)",
        "Mine (type 3)", "Mine (type 4)", "Satellite", "Nav Buoy", "Probe",
    };

    /// <summary>
    /// Mission choices of every operation of tours 1-5 (OpenXW shipext.c g_tourNOperations; identical to XWVM's
    /// todStages.json). Each operation offers mission A, and sometimes an alternative mission B (255 = none).
    /// The pilot file keeps one score per mission, indexed by mission.
    /// </summary>
    internal static readonly (byte A, byte B)[][] Operations =
    {
        Linear(12),
        Linear(12),
        Linear(14),
        new (byte, byte)[] { (0, 255), (1, 255), (2, 255), (3, 255), (4, 255), (5, 6), (7, 255), (8, 9), (10, 255), (11, 255), (12, 255), (13, 14), (15, 255), (16, 255), (17, 255), (18, 19), (20, 255), (21, 255), (22, 255), (23, 255) },
        new (byte, byte)[] { (0, 255), (1, 255), (2, 255), (3, 255), (4, 255), (5, 255), (6, 255), (7, 255), (8, 255), (9, 255), (10, 11), (12, 255), (13, 255), (14, 15), (16, 255), (17, 255), (18, 19), (20, 255), (21, 255), (22, 23) },
    };

    private static (byte, byte)[] Linear(int count) => Enumerable.Range(0, count).Select(i => ((byte)i, (byte)255)).ToArray();

    /// <summary>Operations per tour (OpenXW shipext.h); tours 6-8 have none.</summary>
    internal static readonly int[] TourOperationCounts = { 12, 12, 14, 20, 20, 0, 0, 0 };

    internal static readonly string[] TourNames =
    {
        "Tour 1", "Tour 2", "Tour 3", "Tour 4 (Imperial Pursuit)", "Tour 5 (B-Wing)", "Tour 6", "Tour 7", "Tour 8",
    };

    internal static readonly IReadOnlyList<ChoiceOption> TourStatus = Choices.Indexed("Not started", "Active", "Incomplete (pilot lost during tour)", "Completed");

    private static RecordSchema BuildSchema(XWingEdition edition)
    {
        var b = new LayoutBuilder();
        var positive = FieldOptions.NonNegative;

        b.Skip(2); // +0x0 unknown byte, +0x1 soft-deletion marker (managed by the game's pilot list)

        using (b.Group("Pilot"))
        {
            b.U8("status", "Status", FieldOptions.OneOf(
                Choices.Indexed("Alive", "Captured", "Killed"),
                "Captured or killed pilots can only be revived (which resets their score) from the game's registration desk."));
            b.U8("rank", "Rank", FieldOptions.OneOf(Choices.Indexed(Ranks)));
            b.U32("score", "Tour of Duty score", positive);
            b.U16("skill", "Pilot skill", FieldOptions.Range(0, ushort.MaxValue,
                "Raised by career score and historical missions; the game uses it as the AI skill level of your craft."));
        }

        using (b.Group("Medals"))
        {
            b.U8("medal.corellianCross", "Corellian Cross", FieldOptions.AsFlag);
            b.U8("medal.mantooine", "Mantooine Medallion", FieldOptions.AsFlag);
            b.U8("medal.starOfAlderaan", "Star of Alderaan", FieldOptions.AsFlag);
            b.U8("medal.shieldOfYavin", "Shield of Yavin (Imperial Pursuit)", FieldOptions.AsFlag);
            b.U8("medal.talonsOfHoth", "Talons of Hoth (B-Wing)", FieldOptions.AsFlag);
            b.Skip(1); // unused medal slot
            b.Skip(1); // +0x10
            b.U8("medal.kalidor", "Kalidor Crescent level", FieldOptions.Range(0, 6,
                "0 = not awarded, 1 = Kalidor Crescent, 2-6 = successive embellishments."));
        }

        b.Skip(20);
        b.Expect(0x26, "training scores");

        using (b.Group("Proving grounds"))
        {
            b.Array("training.bestScore", BinaryKind.UInt32, 4, i => $"{Ships[i]} best score", positive);
            b.Skip(80);
            b.Expect(0x86, "training levels");
            b.Array("training.level", BinaryKind.UInt8, 6, i => $"{Ships[i]} highest level completed",
                FieldOptions.Range(0, 255, "Zero-based. The training room offers one level more than this, up to the number of levels installed."),
                include: i => i < 4);
        }

        b.Skip(18);
        b.Skip(2); // +0x9E/+0x9F combat simulator ship/mission selection (stamped by the game before launch)
        b.Expect(0xA0, "historical scores");

        using (b.Group("Historical missions"))
        {
            for (var ship = 0; ship < 4; ship++)
            {
                var s = ship;
                using (b.Group(Ships[s]))
                {
                    b.Array($"historic.score[{s}]", BinaryKind.UInt32, 16, i => $"Mission {i + 1} best score", positive);
                }
            }

            using (b.Group("Bonus"))
            {
                b.Array("historic.bonusScore", BinaryKind.UInt32, 6, i => $"Bonus mission {i + 1} best score", positive);
            }
        }

        b.Skip(104);
        b.Expect(0x220, "combat awards");

        using (b.Group("Historical missions"))
        {
            for (var ship = 0; ship < 4; ship++)
            {
                var s = ship;
                using (b.Group(Ships[s]))
                {
                    b.Array($"historic.patch[{s}]", BinaryKind.UInt8, 6, i => $"Mission {i + 1} completed (battle patch)", FieldOptions.AsFlag);
                    b.Skip(10);
                }
            }

            using (b.Group("Bonus"))
            {
                b.Array("historic.bonusCompleted", BinaryKind.UInt8, 6, i => $"Bonus mission {i + 1} completed", FieldOptions.AsFlag);
            }
        }

        b.Skip(26);
        b.Expect(0x280, "current tour");

        using (b.Group("Tours of Duty"))
        {
            b.U8("tour.current", "Current tour", FieldOptions.OneOf(Choices.Numbered(8, i => $"Tour {i + 1}")));
            b.Skip(2); // +0x281 unknown, +0x282 selected briefing mission (derived)
            b.U8("tour.currentOperation", "Current operation in the current tour", FieldOptions.Range(0, 24, "Zero-based."));
        }

        b.Skip(3);
        b.Skip(2); // +0x287 briefing A/B mission choices, derived from the tour script
        b.Skip(edition == XWingEdition.Windows98 ? 86 : 87);

        using (b.Group("Tours of Duty"))
        {
            b.Array("tour.status", BinaryKind.UInt8, 8, i => $"Tour {i + 1} status", FieldOptions.OneOf(TourStatus));
            b.Skip(8); // per-tour Combat Chamber unlock, maintained by the game
            b.Array("tour.progress", BinaryKind.UInt8, 8, i => $"Tour {i + 1} operations completed", FieldOptions.Range(0, 25));
            for (var tour = 0; tour < 8; tour++)
            {
                var t = tour;
                using (b.Group($"Tour {t + 1} scores"))
                {
                    b.Array($"tour.score[{t}]", BinaryKind.UInt32, 25, i => $"Mission {i + 1} score", positive);
                }
            }
        }

        b.Skip(24);

        using (b.Group("Statistics"))
        {
            b.U16("stats.kills", "Total kills", positive);
            b.U16("stats.captures", "Total captures", positive);
            b.U16("stats.surfaceVictories", "Surface victories", positive);
        }

        using (b.Group("Statistics/Kills by craft"))
        {
            b.Array("stats.killsByType", BinaryKind.UInt16, 24, i => KillTypes[i], positive);
        }

        using (b.Group("Statistics/Captures by craft"))
        {
            b.Array("stats.capturesByType", BinaryKind.UInt16, 24, i => KillTypes[i], positive);
        }

        using (b.Group("Statistics"))
        {
            b.U32("stats.laserShots", "Laser shots fired", positive);
            b.U32("stats.laserCraftHits", "Laser hits on craft", positive);
            b.U32("stats.laserSurfaceHits", "Laser hits on surface targets", positive);
            b.U16("stats.warheadsFired", "Warheads fired", positive);
            b.U16("stats.warheadCraftHits", "Warhead hits on craft", positive);
            b.U16("stats.warheadSurfaceHits", "Warhead hits on surface targets", positive);
            b.U16("stats.craftLost", "Craft lost / ejections", positive);
        }

        return b.Build(edition == XWingEdition.Windows98 ? WindowsRecordSize : DosRecordSize);
    }
}

internal sealed class XWingPilotDocument : BinaryPilotDocument
{
    private readonly XWingPilotFormat _format;

    public XWingPilotDocument(XWingPilotFormat format, string fileName, byte[] data, int copies)
        : base(data, format.Schema, copies)
    {
        _format = format;
        PilotName = Path.GetFileNameWithoutExtension(fileName);
        Notes.Add("X-Wing stores the pilot's name as the file name: renaming the pilot renames the file.");
        Arrange();
    }

    private void Arrange()
    {
        var layout = BeginLayout();
        layout.ShowAsIs("status").ShowAsIs("rank").ShowAsIs("score").ShowAsIs("skill").ShowAsIs("medal.");

        for (var c = 0; c < 4; c++)
        {
            var craft = XWingPilotFormat.Ships[c];
            layout.Show($"training.bestScore[{c}]", $"Proving grounds/{craft}", "Best score")
                  .Show($"training.level[{c}]", $"Proving grounds/{craft}", "Highest level completed");
        }

        // Historical missions: six per craft (and six bonus missions), each with a score and a battle patch.
        for (var c = 0; c < 4; c++)
        {
            var craft = XWingPilotFormat.Ships[c];
            for (var m = 0; m < 16; m++)
            {
                if (m >= 6 && layout.AllZero(new[] { $"historic.score[{c}][{m}]" }))
                {
                    continue; // no such mission
                }

                var group = $"Historical missions/{craft}/Mission {m + 1}";
                layout.Show($"historic.score[{c}][{m}]", group, "Best score")
                      .Show($"historic.patch[{c}][{m}]", group, "Completed (battle patch)");
            }
        }

        for (var m = 0; m < 6; m++)
        {
            var group = $"Historical missions/Bonus/Mission {m + 1}";
            layout.Show($"historic.bonusScore[{m}]", group, "Best score")
                  .Show($"historic.bonusCompleted[{m}]", group, "Completed");
        }

        // Tours of Duty: which tour is being flown at the root; each tour's progress and mission scores in the tour.
        layout.Show("tour.current", "Tours of Duty").Show("tour.currentOperation", "Tours of Duty");
        for (var t = 0; t < 8; t++)
        {
            var real = t < XWingPilotFormat.Operations.Length;
            var scoreIds = Enumerable.Range(0, 25).Select(m => $"tour.score[{t}][{m}]").ToList();
            if (!real && layout.AllZero(scoreIds.Append($"tour.status[{t}]").Append($"tour.progress[{t}]")))
            {
                continue; // tours 6-8 do not exist
            }

            var group = real ? $"Tours of Duty/{XWingPilotFormat.TourNames[t]}" : $"Tours of Duty/Tour {t + 1} (not in the game)";
            layout.Show($"tour.status[{t}]", group, "Status")
                  .Show($"tour.progress[{t}]", group, "Operations completed");

            var labels = new Dictionary<int, string>();
            if (real)
            {
                var operations = XWingPilotFormat.Operations[t];
                for (var k = 0; k < operations.Length; k++)
                {
                    var (a, b) = operations[k];
                    labels[a] = b == 255 ? $"Operation {k + 1} score" : $"Operation {k + 1} score (mission A)";
                    if (b != 255)
                    {
                        labels[b] = $"Operation {k + 1} score (mission B)";
                    }
                }
            }

            for (var m = 0; m < 25; m++)
            {
                if (labels.TryGetValue(m, out var label))
                {
                    layout.Show($"tour.score[{t}][{m}]", group, label);
                }
                else if (!layout.AllZero(new[] { $"tour.score[{t}][{m}]" }))
                {
                    layout.Show($"tour.score[{t}][{m}]", group, $"Unused slot {m + 1} score");
                }
            }
        }

        layout.ShowAsIs("stats.");
    }

    public override string GameName => _format.Game;

    public override string FormatName => _format.Name;

    public override string PilotName { get; }

    protected override IEnumerable<ValidationIssue> CheckRules()
    {
        foreach (var issue in CommonRules.HitsVsShots(this, "stats.laserShots", "stats.laserCraftHits", "stats.laserSurfaceHits", "laser"))
        {
            yield return issue;
        }

        foreach (var issue in CommonRules.HitsVsShots(this, "stats.warheadsFired", "stats.warheadCraftHits", "stats.warheadSurfaceHits", "warhead"))
        {
            yield return issue;
        }

        foreach (var issue in CommonRules.TotalVsBreakdown(this, "stats.kills", "stats.killsByType", 24, "kills"))
        {
            yield return issue;
        }

        foreach (var issue in CommonRules.TotalVsBreakdown(this, "stats.captures", "stats.capturesByType", 24, "captures"))
        {
            yield return issue;
        }

        var activeTours = Enumerable.Range(0, 8).Where(i => Int($"tour.status[{i}]") == 1).ToList();
        if (activeTours.Count > 1)
        {
            yield return CommonRules.Broken(
                $"Only one tour can be active at a time (tours {string.Join(", ", activeTours.Select(t => t + 1))} are all marked active).",
                activeTours.Select(t => Field($"tour.status[{t}]")).ToArray());
        }

        if (activeTours.Count == 1 && activeTours[0] != Int("tour.current"))
        {
            yield return CommonRules.Broken(
                $"Tour {activeTours[0] + 1} is active, but the current tour is set to Tour {Int("tour.current") + 1}.",
                Field("tour.current"), Field($"tour.status[{activeTours[0]}]"));
        }

        for (var t = 0; t < 8; t++)
        {
            var status = Int($"tour.status[{t}]");
            var progress = Int($"tour.progress[{t}]");
            if (status == 0 && progress > 0)
            {
                yield return CommonRules.Odd(
                    $"Tour {t + 1} is not started but has {progress} completed operations.", Field($"tour.progress[{t}]"));
            }
        }

        for (var t = 0; t < 8; t++)
        {
            var status = Field($"tour.status[{t}]");
            var progress = Field($"tour.progress[{t}]");
            var count = XWingPilotFormat.TourOperationCounts[t];
            if (count == 0 && (status.GetInteger() != 0 || progress.GetInteger() != 0))
            {
                yield return CommonRules.Broken($"Tour {t + 1} does not exist in any edition of X-Wing, so it cannot be started.", status, progress);
            }
            else if (count > 0 && progress.GetInteger() > count)
            {
                yield return CommonRules.Broken($"Tour {t + 1} has only {count} operations, so at most {count} can be completed.", progress);
            }
            else if (count > 0 && status.GetInteger() == 3 && progress.GetInteger() < count)
            {
                yield return CommonRules.Odd($"Tour {t + 1} is marked completed but only {progress.GetInteger()} of its {count} operations are.", progress);
            }
        }

        if (activeTours.Count == 1 && activeTours[0] < 5 &&
            Int("tour.currentOperation") >= XWingPilotFormat.TourOperationCounts[activeTours[0]])
        {
            yield return CommonRules.Broken(
                $"The current operation is beyond the last operation of Tour {activeTours[0] + 1}.",
                Field("tour.currentOperation"));
        }

        if (Edition is { } edition && edition.BaseTours < 5)
        {
            for (var t = edition.BaseTours; t < 5; t++)
            {
                if (Int($"tour.status[{t}]") != 0)
                {
                    yield return CommonRules.Odd(
                        $"{XWingPilotFormat.TourNames[t]} is not part of the {edition.Name}; it needs the {(t == 3 ? "Imperial Pursuit" : "B-Wing")} add-on disk.",
                        Field($"tour.status[{t}]"));
                }
            }
        }

        if (Int("status") != 0 && activeTours.Count > 0)
        {
            yield return CommonRules.Odd(
                "The pilot is captured or killed but still has an active tour. The game marks the tour incomplete when a pilot is lost.",
                Field("status"));
        }
    }
}
