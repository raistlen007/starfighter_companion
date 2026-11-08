using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Library;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Conversion;

/// <summary>The outcome of a conversion: the new file's bytes and what the user should know about it.</summary>
public sealed record ConversionResult(byte[] Data, IReadOnlyList<string> Notes, IReadOnlyList<string> Warnings);

/// <summary>
/// Converts pilot files between the retail editions of one game.
/// <list type="bullet">
/// <item>X-Wing: the 1993 floppy and 1994 CD-ROM write the same 1,706-byte record; the 1998 edition writes 1,705 bytes.
/// The only difference is one more reserved byte in the block at 0x289 (XWVM Xw93PilotFile / Xw98PilotFile), which no
/// edition reads.</item>
/// <item>TIE Fighter: every edition writes the same 1,928-byte record (twice). What differs is which battles exist.</item>
/// </list>
/// </summary>
public static class EditionConverter
{
    /// <summary>Start of the reserved block in front of the tour table (same offset in every X-Wing edition).</summary>
    internal const int XWingReservedBlock = 0x289;

    /// <summary>The DOS block is 87 bytes and the 1998 one 86; the converter adds or drops the block's last byte.</summary>
    internal const int XWingDosExtraByte = XWingReservedBlock + 86;

    public static ConversionResult Convert(byte[] data, Edition source, Edition target)
    {
        if (source.Family != target.Family)
        {
            throw new ArgumentException($"A {source.Family} pilot cannot be converted to {target.Name}.", nameof(target));
        }

        var notes = new List<string>();
        var warnings = new List<string>();
        byte[] output;

        if (source.Family == GameFamily.XWing)
        {
            var sourceSize = source.DosLayout ? XWingPilotFormat.DosRecordSize : XWingPilotFormat.WindowsRecordSize;
            if (data.Length == 0 || data.Length % sourceSize != 0)
            {
                throw new InvalidDataException($"An X-Wing pilot for the {source.Name} has {sourceSize:N0} bytes per record; this file has {data.Length:N0}.");
            }

            var copies = data.Length / sourceSize;
            var records = Enumerable.Range(0, copies).Select(i => data.AsSpan(i * sourceSize, sourceSize).ToArray()).ToList();
            var converted = records.Select(r => ToXWingLayout(r, source.DosLayout, target.DosLayout, warnings)).ToList();
            output = converted.SelectMany(r => r).ToArray();

            if (source.DosLayout != target.DosLayout)
            {
                notes.Add(target.DosLayout
                    ? "The record was widened to the DOS layout (1,706 bytes)."
                    : "The record was narrowed to the 1998 layout (1,705 bytes).");
            }
            else
            {
                notes.Add("Both editions use the same record layout, so nothing had to be moved.");
            }

            var fixups = FixXWingForEdition(output, target);
            notes.AddRange(fixups);
        }
        else
        {
            if (data.Length != TiePilotFormat.RecordSize && data.Length != TiePilotFormat.RecordSize * 2)
            {
                throw new InvalidDataException($"A TIE Fighter pilot has {TiePilotFormat.RecordSize * 2:N0} bytes; this file has {data.Length:N0}.");
            }

            output = (byte[])data.Clone();
            notes.Add("All TIE Fighter editions use the same record layout, so nothing had to be moved.");
            notes.AddRange(FixTieForEdition(output, target));
        }

        warnings.AddRange(CheckForEdition(output, target));
        return new ConversionResult(output, notes, warnings.Distinct().ToList());
    }

    /// <summary>Opens <paramref name="data"/> as a pilot of <paramref name="edition"/> and returns the problems the editor would report.</summary>
    internal static IEnumerable<string> CheckForEdition(byte[] data, Edition edition)
    {
        var fileName = "PILOT" + edition.Extension;
        var format = PilotFormats.Detect(fileName, data);
        if (format is null)
        {
            return new[] { "The converted file is not recognised as a pilot file." };
        }

        var document = format.Open(fileName, data);
        document.Edition = edition;
        return document.Validate().Select(i => i.Severity == IssueSeverity.Error ? "Error: " + i.Message : i.Message);
    }

    private static byte[] ToXWingLayout(byte[] record, bool fromDos, bool toDos, List<string> warnings)
    {
        if (fromDos == toDos)
        {
            return record;
        }

        if (fromDos)
        {
            if (record[XWingDosExtraByte] != 0)
            {
                warnings.Add($"A reserved byte that the 1998 edition does not have was not zero (0x{record[XWingDosExtraByte]:X2}) and was dropped. No edition is known to use it.");
            }

            return record.Take(XWingDosExtraByte).Concat(record.Skip(XWingDosExtraByte + 1)).ToArray();
        }

        return record.Take(XWingDosExtraByte).Append((byte)0).Concat(record.Skip(XWingDosExtraByte)).ToArray();
    }

    /// <summary>Makes sure the converted pilot cannot point the game at something the edition does not have.</summary>
    private static IEnumerable<string> FixXWingForEdition(byte[] data, Edition target)
    {
        // Nothing is removed: tours 4 and 5 are available on the 1993 floppy with the add-on disks.
        // The rules in XWingPilotDocument warn about them.
        return Array.Empty<string>();
    }

    private static IEnumerable<string> FixTieForEdition(byte[] data, Edition target)
    {
        var copies = data.Length / TiePilotFormat.RecordSize;
        var changed = false;
        int oldBattle = 0, newBattle = 0;
        for (var c = 0; c < copies; c++)
        {
            var o = c * TiePilotFormat.RecordSize;
            var current = data[o + TieOffsets.CurrentBattle];
            if (current < target.MaxBattles)
            {
                continue;
            }

            // The game would load a battle that does not exist. Point it at the last battle that does and has progress.
            var replacement = Enumerable.Range(0, target.MaxBattles).LastOrDefault(b => data[o + TieOffsets.BattleStatus + b] != 0);
            if (c == 0)
            {
                oldBattle = current;
                newBattle = replacement;
            }

            data[o + TieOffsets.CurrentBattle] = (byte)replacement;
            changed = true;
        }

        if (changed)
        {
            yield return $"The current battle was Battle {oldBattle + 1}, which the {target.Name} does not have; it is now Battle {newBattle + 1}. Progress in later battles is kept in the file.";
        }
    }
}

/// <summary>Byte offsets in the TIE Fighter record (OpenTIE src/tie/shipext.h, on-disk layout).</summary>
internal static class TieOffsets
{
    public const int Version = 0x000;
    public const int Rank = 0x002;
    public const int Difficulty = 0x003;
    public const int Score = 0x004;
    public const int AverageScore = 0x008;
    public const int SecretOrderRank = 0x00A;
    public const int SecretCompletions = 0x00C;
    public const int SecretScore = 0x00E;
    public const int TrainingLevel = 0x01D;
    public const int TrainingScore = 0x02A;
    public const int TrainingMaxLevel = 0x05A;
    public const int CombatScore = 0x088;
    public const int CombatComplete = 0x208;
    public const int CurrentBattle = 0x268;
    public const int BattleStatus = 0x269;
    public const int BattleCursor = 0x27D;
    public const int SecretBits = 0x391;
    public const int BonusBits = 0x3A5;
    public const int TourScore = 0x3DA;
    public const int TotalKills = 0x65A;
    public const int TotalCaptures = 0x65C;
    public const int KillsByType = 0x660;
    public const int CapturesByType = 0x6EA;
    public const int LaserShots = 0x774;
    public const int LaserHits = 0x778;
    public const int WarheadShots = 0x780;
    public const int WarheadHits = 0x782;
    public const int Ejections = 0x786;
}

/// <summary>Byte offsets in the X-Wing 1998 record (OpenXW src/xw/frontend/register.h); DOS records are one byte longer from 0x2DF on.</summary>
internal static class XWingOffsets
{
    public const int Status = 0x002;
    public const int Rank = 0x003;
    public const int Score = 0x004;
    public const int Skill = 0x008;
    public const int Medals = 0x00A;          // Corellian Cross, Mantooine Medallion, Star of Alderaan
    public const int ExpansionMedals = 0x00D; // Shield of Yavin, Talons of Hoth
    public const int Kalidor = 0x011;
    public const int TrainingScore = 0x026;
    public const int TrainingLevel = 0x086;
    public const int HistoricScore = 0x0A0;   // [4 craft][16]
    public const int BonusScore = 0x1A0;
    public const int HistoricPatch = 0x220;   // [4 craft][16]
    public const int BonusCompleted = 0x260;
    public const int CurrentTour = 0x280;
    public const int SelectedTourMission = 0x282;
    public const int CurrentOperation = 0x283;
    public const int BriefingChoices = 0x287;
    public const int TourStatus = 0x2DF;
    public const int TourReplayUnlock = 0x2E7;
    public const int TourProgress = 0x2EF;
    public const int TourScores = 0x2F7;      // [8 tours][25 missions]
    public const int TotalKills = 0x62F;
    public const int TotalCaptures = 0x631;
    public const int SurfaceVictories = 0x633;
    public const int KillsByType = 0x635;
    public const int CapturesByType = 0x665;
    public const int LaserShots = 0x695;
    public const int LaserCraftHits = 0x699;
    public const int LaserSurfaceHits = 0x69D;
    public const int WarheadShots = 0x6A1;
    public const int WarheadCraftHits = 0x6A3;
    public const int WarheadSurfaceHits = 0x6A5;
    public const int Ejections = 0x6A7;
}
