using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Library;

namespace StarfighterCompanion.Core.Conversion;

/// <summary>
/// Moves X-Wing and TIE Fighter pilots into and out of XWVM pilot files (<c>.vmpilot</c>).
/// <para>
/// X-Wing follows XWVM's own importer (PilotGameRecord(IOriginalPilotFile, …) in Assets/Code/GameState), with tour
/// progress read the way the game itself keeps it (OpenXW fediskio.c: the progress byte counts completed operations,
/// and an operation may offer two alternative missions).
/// TIE Fighter has no importer in XWVM; it is mapped onto the same records XWVM uses when it plays TIE Fighter
/// (battle tours "battle1"…"battle13", combat chamber groups "ship1"…"ship7", the secret order award and stats).
/// </para>
/// </summary>
public static class XwvmBridge
{
    public const string XWingGameId = "xwing";
    public const string TieGameId = "tf";

    internal const int Completed = int.MaxValue;

    /// <summary>Games every XWVM pilot carries; XWVM looks them up without checking that they exist.</summary>
    private static readonly string[] AllGameIds = { "xwing", "tf", "xvt", "xwa" };

    private static readonly string[] Ranks = { "FltCadet", "FltOfficer", "Lieutenant", "Captain", "Commander", "General" };
    private static readonly string[] Statuses = { "Alive", "Captured", "Dead" };
    private static readonly string[] Difficulties = { "Easy", "Medium", "Hard" };

    // ---- X-Wing ----------------------------------------------------------------------------------------------------

    /// <summary>Kill/capture slots of the X-Wing pilot file, in file order (XWVM PilotGameRecord import).</summary>
    internal static readonly string[] XWingCraft =
    {
        "XWing", "YWing", "AWing", "TIEFighter", "TIEInterceptor", "TIEBomber", "Gunboat", "Transport", "Shuttle", "Tug",
        "Container", "Freighter", "CalamariCruiser", "NebulonBFrigate", "CorellianCorvette", "ImperialStarDestroyer",
        "TIEAdvanced", "Mine1", "Mine2", "Mine3", "Mine4", "Satellite", "NavBuoy", "Probe",
    };

    private static readonly string[] XWingTrainingCraft = { "XWing", "YWing", "AWing", "BWing" };

    private static readonly string[] XWingHistoricTours = { "xcombat", "ycombat", "acombat", "bcombat", "bonus" };

    private const string KalidorAward = "kalidor";

    /// <summary>Mission choices of every operation (see <see cref="XWingPilotFormat.Operations"/>).</summary>
    internal static (byte A, byte B)[][] XWingOperations => XWingPilotFormat.Operations;

    // ---- TIE Fighter -----------------------------------------------------------------------------------------------

    /// <summary>TIE Fighter species (OpenTIE species table order) → XWVM entity type; null where XWVM has none.</summary>
    internal static readonly string?[] TieSpecies =
    {
        "XWing", "YWing", "AWing", "BWing", "TIEFighter", "TIEInterceptor", "TIEBomber", "TIEAdvanced", "TIEDefender", null,
        null, "MissileBoat", "TWing", "Z95", "R41", "Gunboat", "Shuttle", "EscortShuttle", "Spc", "ScoutCraft",
        "Transport", "AssaultTransport", "EscortTransport", "Tug", "CombatUtilityVehicle", "Container", "ContainerB", "ContainerC", "ContainerD", "HeavyLifter",
        null, "Freighter", "CargoFerry", "Conveyor", "ContainerTransport", null, "MuurianTransport", "CorellianTransport", null, "CorellianCorvette",
        "ModifiedCorvette", "NebulonBFrigate", "ModifiedFrigate", "Liner", "Carrack", "StrikeCruiser", "EscortCarrier", "Dreadnaught", "CalamariCruiser", "LightCruiser",
        "Interdictor", "VictorySD", "ImperialStarDestroyer", null /* Super Star Destroyer */, "ContainerE", "ContainerF", "ContainerG", "ContainerH", "ContainerI", "Platform1",
        "Platform2", "Platform3", "Platform4", "Platform5", "Platform6", "AsteroidHangar", "AsteroidLaserBattery", "AsteroidWarhead", "X7Factory",
    };

    /// <summary>Training and combat chamber craft, in file order (TIE ship1…ship7; XWVM TieGameData proving grounds list).</summary>
    internal static readonly string[] TieCraft = { "TIEFighter", "TIEInterceptor", "TIEBomber", "TIEAdvanced", "Gunboat", "TIEDefender", "MissileBoat" };

    private const int TieBattles = 13;
    private const string SecretOrderAward = "secretOrderRank";

    // ================================================================================================================
    // Legacy → XWVM
    // ================================================================================================================

    /// <summary>
    /// Puts a legacy pilot into an XWVM pilot file. When <paramref name="existingVmpilot"/> is given, only that game's
    /// record is replaced and everything else in the file is kept; otherwise a new pilot named <paramref name="pilotName"/> is created.
    /// </summary>
    public static ConversionResult ToXwvm(byte[] legacy, Edition source, byte[]? existingVmpilot, string pilotName)
    {
        var notes = new List<string>();
        var warnings = new List<string>();
        var document = existingVmpilot is null ? NewPilot(pilotName) : LoadVmpilot(existingVmpilot);
        var root = document.Root!;
        var gameId = source.Family == GameFamily.XWing ? XWingGameId : TieGameId;
        var previous = root.Elements("PilotGameRecord").FirstOrDefault(e => (string?)e.Attribute("Id") == gameId);

        var record = source.Family == GameFamily.XWing
            ? FromXWing(FirstXWingRecord(legacy, source), previous, notes, warnings)
            : FromTie(legacy.AsSpan(0, TiePilotFormat.RecordSize).ToArray(), previous, notes, warnings);

        if (previous is not null)
        {
            previous.ReplaceWith(record);
            notes.Add($"The {(source.Family == GameFamily.XWing ? "X-Wing" : "TIE Fighter")} career of XWVM pilot \"{(string?)root.Attribute("Name")}\" was replaced; the other games in the file were not touched.");
        }
        else
        {
            InsertGame(root, record);
        }

        EnsureAllGames(root);
        return new ConversionResult(SaveVmpilot(document, existingVmpilot), notes, warnings);
    }

    private static byte[] FirstXWingRecord(byte[] data, Edition source)
    {
        var size = source.DosLayout ? XWingPilotFormat.DosRecordSize : XWingPilotFormat.WindowsRecordSize;
        if (data.Length < size)
        {
            throw new InvalidDataException("The file is too short to be an X-Wing pilot.");
        }

        var record = data.AsSpan(0, size).ToArray();
        return source.DosLayout
            ? record.Take(EditionConverter.XWingDosExtraByte).Concat(record.Skip(EditionConverter.XWingDosExtraByte + 1)).ToArray()
            : record;
    }

    private static XElement FromXWing(byte[] r, XElement? previous, List<string> notes, List<string> warnings)
    {
        var game = GameElement(XWingGameId, previous,
            rank: r[XWingOffsets.Rank],
            status: r[XWingOffsets.Status] <= 2 ? r[XWingOffsets.Status] : 0,
            difficulty: null,
            currentScore: ClampInt(U32(r, XWingOffsets.Score)),
            aiScore: 4 * U16(r, XWingOffsets.Skill));

        // Awards: the Kalidor Crescent (tour medals follow from completed tours in XWVM).
        var kalidor = r[XWingOffsets.Kalidor];
        if (kalidor is > 0 and <= 6)
        {
            game.Add(Award(KalidorAward, kalidor - 1));
        }

        // Proving grounds
        for (var i = 0; i < XWingTrainingCraft.Length; i++)
        {
            game.Add(Training(XWingTrainingCraft[i], ClampInt(U32(r, XWingOffsets.TrainingScore + i * 4)), r[XWingOffsets.TrainingLevel + i]));
        }

        // Tours of Duty
        string? activeTour = null;
        for (var t = 0; t < XWingOperations.Length; t++)
        {
            var status = r[XWingOffsets.TourStatus + t];
            if (status == 0)
            {
                continue;
            }

            var operations = XWingOperations[t];
            var progress = r[XWingOffsets.TourProgress + t];
            var complete = status == 3 || progress >= operations.Length;
            var tour = Tour("PilotTourRecord", $"tour{t + 1}", complete ? Completed : progress);
            for (var k = 0; k < operations.Length; k++)
            {
                var (a, b) = operations[k];
                var score = Math.Max(U32(r, XWingOffsets.TourScores + (t * 25 + a) * 4), b == 255 ? 0 : U32(r, XWingOffsets.TourScores + (t * 25 + b) * 4));
                var done = complete || k < progress;
                if (done || score > 0)
                {
                    tour.Add(Operation(k, ClampInt(score), done));
                }
            }

            game.Add(tour);
            if (t == r[XWingOffsets.CurrentTour] && status is 1 or 2 && !complete)
            {
                activeTour = $"tour{t + 1}";
            }
        }

        if (activeTour is not null)
        {
            game.SetAttributeValue("ActiveTour", activeTour);
        }

        // Historical missions
        for (var s = 0; s < XWingHistoricTours.Length; s++)
        {
            var historic = Tour("HistoricTourRecord", XWingHistoricTours[s], 0);
            for (var j = 0; j < 16; j++)
            {
                var score = s < 4 ? U32(r, XWingOffsets.HistoricScore + (s * 16 + j) * 4) : U32(r, XWingOffsets.BonusScore + j * 4);
                var success = (s < 4 ? r[XWingOffsets.HistoricPatch + s * 16 + j] : r[XWingOffsets.BonusCompleted + j]) != 0;
                if (score != 0 || success)
                {
                    historic.Add(Operation(j, ClampInt(score), success));
                }
            }

            game.Add(historic);
        }

        // Kills and captures
        for (var i = 0; i < XWingCraft.Length; i++)
        {
            var kills = U16(r, XWingOffsets.KillsByType + i * 2);
            var captures = U16(r, XWingOffsets.CapturesByType + i * 2);
            if (kills != 0 || captures != 0)
            {
                game.Add(Victory(XWingCraft[i], kills, captures));
            }
        }

        game.Add(Stat("CraftLost", U16(r, XWingOffsets.Ejections)));
        game.Add(Stat("ProjectilesHits", U16(r, XWingOffsets.WarheadCraftHits)));
        game.Add(Stat("ProjectileShots", U16(r, XWingOffsets.WarheadShots)));
        game.Add(Stat("ProjectilesGroundHits", U16(r, XWingOffsets.WarheadSurfaceHits)));
        game.Add(Stat("LaserHits", ClampInt(U32(r, XWingOffsets.LaserCraftHits))));
        game.Add(Stat("LaserGroundHits", ClampInt(U32(r, XWingOffsets.LaserSurfaceHits))));
        game.Add(Stat("LaserShots", ClampInt(U32(r, XWingOffsets.LaserShots))));
        game.Add(Stat("SurfaceVictories", U16(r, XWingOffsets.SurfaceVictories)));

        CarryOver(previous, game);
        notes.Add("Converted the way XWVM's own pilot import does. Tour medals are awarded by XWVM from the completed tours.");
        return game;
    }

    private static XElement FromTie(byte[] r, XElement? previous, List<string> notes, List<string> warnings)
    {
        var battleStatuses = Enumerable.Range(0, TieBattles).Select(b => r[TieOffsets.BattleStatus + b]).ToArray();
        var lost = battleStatuses.Any(s => s == 2);
        var avg = U16(r, TieOffsets.AverageScore);
        var game = GameElement(TieGameId, previous,
            rank: r[TieOffsets.Rank],
            status: lost ? 1 : 0,
            difficulty: r[TieOffsets.Difficulty] <= 2 ? r[TieOffsets.Difficulty] : 1,
            currentScore: I32NonNegative(r, TieOffsets.Score),
            aiScore: 4 * avg);

        if (lost)
        {
            notes.Add("The pilot was lost in battle in TIE Fighter (the file does not say whether captured or killed); XWVM shows the pilot as captured until revived.");
        }

        var secretRank = r[TieOffsets.SecretOrderRank];
        if (secretRank is > 0 and <= 9)
        {
            game.Add(Award(SecretOrderAward, secretRank - 1));
        }

        for (var i = 0; i < TieCraft.Length; i++)
        {
            // TIE keeps the highest level the pilot may pick (2 for a new pilot, raised to the last level reached);
            // XWVM keeps the highest level completed (-1 for none).
            var unlocked = r[TieOffsets.TrainingMaxLevel + i];
            game.Add(Training(TieCraft[i], I32NonNegative(r, TieOffsets.TrainingScore + i * 4), unlocked > 2 ? unlocked - 1 : -1));
        }

        string? activeTour = null;
        for (var b = 0; b < TieBattles; b++)
        {
            var status = battleStatuses[b];
            if (status == 0)
            {
                continue;
            }

            var cursor = r[TieOffsets.BattleCursor + b];
            var complete = status == 3;
            var tour = Tour("PilotTourRecord", $"battle{b + 1}", complete ? Completed : Math.Min((int)cursor, 7));
            for (var m = 0; m < 8; m++)
            {
                var score = I32NonNegative(r, TieOffsets.TourScore + (b * 8 + m) * 4);
                var secret = (r[TieOffsets.SecretBits + b] & (1 << m)) != 0;
                var bonus = (r[TieOffsets.BonusBits + b] & (1 << m)) != 0;
                var done = m < cursor; // a completed battle's cursor is its number of missions
                if (done || score > 0 || secret || bonus)
                {
                    var op = Operation(m, score, done);
                    if (secret)
                    {
                        op.SetAttributeValue("SecondaryGoals", "true");
                    }

                    if (bonus)
                    {
                        op.SetAttributeValue("BonusGoals", "true");
                    }

                    tour.Add(op);
                }
            }

            game.Add(tour);
            if (b == r[TieOffsets.CurrentBattle] && !complete)
            {
                activeTour = $"battle{b + 1}";
            }
        }

        if (activeTour is not null)
        {
            game.SetAttributeValue("ActiveTour", activeTour);
        }

        for (var s = 0; s < TieCraft.Length; s++)
        {
            var historic = Tour("HistoricTourRecord", $"ship{s + 1}", 0);
            for (var j = 0; j < 8; j++)
            {
                var score = I32NonNegative(r, TieOffsets.CombatScore + (s * 8 + j) * 4);
                var done = r[TieOffsets.CombatComplete + s * 8 + j] != 0;
                if (score != 0 || done)
                {
                    historic.Add(Operation(j, score, done));
                }
            }

            game.Add(historic);
        }

        var dropped = 0;
        for (var i = 0; i < TieSpecies.Length; i++)
        {
            var kills = U16(r, TieOffsets.KillsByType + i * 2);
            var captures = U16(r, TieOffsets.CapturesByType + i * 2);
            if (kills == 0 && captures == 0)
            {
                continue;
            }

            if (TieSpecies[i] is { } entity)
            {
                game.Add(Victory(entity, kills, captures));
            }
            else
            {
                dropped += kills + captures;
            }
        }

        if (dropped > 0)
        {
            warnings.Add($"{dropped} kill(s)/capture(s) of craft XWVM does not have (such as the Super Star Destroyer) were left out.");
        }

        game.Add(Stat("CraftLost", U16(r, TieOffsets.Ejections)));
        game.Add(Stat("ProjectilesHits", U16(r, TieOffsets.WarheadHits)));
        game.Add(Stat("ProjectileShots", U16(r, TieOffsets.WarheadShots)));
        game.Add(Stat("LaserHits", I32NonNegative(r, TieOffsets.LaserHits)));
        game.Add(Stat("LaserShots", I32NonNegative(r, TieOffsets.LaserShots)));
        game.Add(Stat("SecretOrderVictories", U16(r, TieOffsets.SecretCompletions)));
        game.Add(Stat("SecretOrderPoints", I32NonNegative(r, TieOffsets.SecretScore)));

        CarryOver(previous, game);
        notes.Add("XWVM has no TIE Fighter pilot import of its own; battles, combat chamber, training, Secret Order rank, kills and statistics were mapped onto the records XWVM keeps when it plays TIE Fighter.");
        return game;
    }

    // ================================================================================================================
    // XWVM → legacy
    // ================================================================================================================

    // ---- used by the TFTC bridge, which goes through XWVM's TIE Fighter record ----------------------------------------

    /// <summary>A TIE Fighter pilot file as XWVM's TIE Fighter game record.</summary>
    internal static XElement TieToGame(byte[] tfr, List<string> notes, List<string> warnings) =>
        FromTie(tfr.AsSpan(0, TiePilotFormat.RecordSize).ToArray(), null, notes, warnings);

    /// <summary>XWVM's TIE Fighter game record as a TIE Fighter pilot file for <paramref name="target"/>.</summary>
    internal static byte[] GameToTie(XElement game, Edition target, List<string> notes, List<string> warnings)
    {
        var record = ToTie(game, target, notes, warnings);
        var data = record.Concat(record).ToArray();
        warnings.AddRange(EditionConverter.CheckForEdition(data, target));
        return data;
    }

    /// <summary>The TIE Fighter game record of an XWVM pilot, or null.</summary>
    internal static XElement? TieGameOf(byte[] vmpilot) =>
        LoadVmpilot(vmpilot).Root!.Elements("PilotGameRecord").FirstOrDefault(e => (string?)e.Attribute("Id") == TieGameId);

    /// <summary>
    /// Puts a TIE Fighter game record into an XWVM pilot (new, or replacing that game in <paramref name="existingVmpilot"/>
    /// and keeping the pilot's look and craft preferences).
    /// </summary>
    internal static byte[] PutTieGame(XElement game, byte[]? existingVmpilot, string pilotName, List<string> notes)
    {
        var document = existingVmpilot is null ? NewPilot(pilotName) : LoadVmpilot(existingVmpilot);
        var root = document.Root!;
        var previous = root.Elements("PilotGameRecord").FirstOrDefault(e => (string?)e.Attribute("Id") == TieGameId);
        foreach (var name in new[] { "Droid", "AvatarPath", "AvatarIndex" })
        {
            if (previous?.Attribute(name) is { } attribute)
            {
                game.SetAttributeValue(name, attribute.Value);
            }
        }

        CarryOver(previous, game);
        if (previous is not null)
        {
            previous.ReplaceWith(game);
            notes.Add($"The TIE Fighter career of XWVM pilot \"{(string?)root.Attribute("Name")}\" was replaced; the other games in the file were not touched.");
        }
        else
        {
            InsertGame(root, game);
        }

        EnsureAllGames(root);
        return SaveVmpilot(document, existingVmpilot);
    }

    /// <summary>Which games of an XWVM pilot can be exported (have any progress or at least a record).</summary>
    public static IReadOnlyList<GameFamily> ExportableGames(byte[] vmpilot)
    {
        var root = LoadVmpilot(vmpilot).Root!;
        var ids = root.Elements("PilotGameRecord").Select(e => (string?)e.Attribute("Id")).ToHashSet();
        var result = new List<GameFamily>();
        if (ids.Contains(XWingGameId))
        {
            result.Add(GameFamily.XWing);
        }

        if (ids.Contains(TieGameId))
        {
            result.Add(GameFamily.Tie);
        }

        return result;
    }

    /// <summary>The name stored in an XWVM pilot.</summary>
    public static string PilotNameOf(byte[] vmpilot) => (string?)LoadVmpilot(vmpilot).Root!.Attribute("Name") ?? "PILOT";

    /// <summary>Writes one game of an XWVM pilot as a new pilot file for <paramref name="target"/>.</summary>
    public static ConversionResult FromXwvm(byte[] vmpilot, Edition target)
    {
        var root = LoadVmpilot(vmpilot).Root!;
        var gameId = target.Family == GameFamily.XWing ? XWingGameId : TieGameId;
        var game = root.Elements("PilotGameRecord").FirstOrDefault(e => (string?)e.Attribute("Id") == gameId)
                   ?? throw new InvalidDataException($"This XWVM pilot has no {(target.Family == GameFamily.XWing ? "X-Wing" : "TIE Fighter")} career.");

        var notes = new List<string>();
        var warnings = new List<string>();
        byte[] data;
        if (target.Family == GameFamily.XWing)
        {
            var record = ToXWing(game, notes, warnings);
            data = target.DosLayout
                ? record.Take(EditionConverter.XWingDosExtraByte).Append((byte)0).Concat(record.Skip(EditionConverter.XWingDosExtraByte)).ToArray()
                : record;
        }
        else
        {
            var record = ToTie(game, target, notes, warnings);
            data = record.Concat(record).ToArray(); // current + backup copy, as the game writes a new pilot
        }

        warnings.AddRange(EditionConverter.CheckForEdition(data, target));
        return new ConversionResult(data, notes, warnings.Distinct().ToList());
    }

    private static byte[] ToXWing(XElement game, List<string> notes, List<string> warnings)
    {
        // A new pilot as the registration desk creates it (OpenXW shipext_Revive_Pilot, create mode).
        var r = new byte[XWingPilotFormat.WindowsRecordSize];
        r.AsSpan(XWingOffsets.TourReplayUnlock, 8).Fill(0xFF);

        var status = Index(Statuses, (string?)game.Attribute("Status"));
        r[XWingOffsets.Status] = (byte)status;
        r[XWingOffsets.Rank] = (byte)Index(Ranks, (string?)game.Attribute("Rank"));
        PutU32(r, XWingOffsets.Score, NonNegative(Int(game, "CurrentScore")));
        PutU16(r, XWingOffsets.Skill, Math.Min(NonNegative(Int(game, "AiScore")) / 4, ushort.MaxValue));

        foreach (var award in game.Elements("PilotAwardRecord"))
        {
            if ((string?)award.Attribute("Award") == KalidorAward)
            {
                r[XWingOffsets.Kalidor] = (byte)Math.Clamp(Int(award, "Level") + 1, 1, 6);
            }
        }

        foreach (var training in game.Elements("TrainingRecord"))
        {
            var i = Array.IndexOf(XWingTrainingCraft, (string?)training.Attribute("CraftType"));
            if (i >= 0)
            {
                PutU32(r, XWingOffsets.TrainingScore + i * 4, NonNegative(Int(training, "HighScore")));
                r[XWingOffsets.TrainingLevel + i] = (byte)Math.Clamp(Int(training, "HighestLevelCompleted"), 0, 255);
            }
        }

        var activeTour = (string?)game.Attribute("ActiveTour");
        foreach (var tour in game.Elements("PilotTourRecord"))
        {
            var t = TourNumber((string?)tour.Attribute("TourId"), "tour") - 1;
            if (t < 0 || t >= XWingOperations.Length)
            {
                warnings.Add($"The XWVM tour \"{(string?)tour.Attribute("TourId")}\" does not exist in X-Wing and was left out.");
                continue;
            }

            var operations = XWingOperations[t];
            var step = Int(tour, "CurrentTourStep");
            var complete = step == Completed || step >= operations.Length;
            var progress = complete ? operations.Length : Math.Max(step, 0);
            var lostInTour = status != 0 && (string?)tour.Attribute("TourId") == activeTour;

            r[XWingOffsets.TourStatus + t] = (byte)(complete ? 3 : lostInTour ? 2 : 1);
            r[XWingOffsets.TourProgress + t] = (byte)progress;
            if (progress > 0)
            {
                var (a, b) = operations[progress - 1];
                r[XWingOffsets.TourReplayUnlock + t] = b != 255 ? b : a;
            }

            foreach (var op in tour.Elements("PilotOperationRecord"))
            {
                var k = Int(op, "TourStep");
                if (k >= 0 && k < operations.Length)
                {
                    // Which alternative was flown is not recorded in XWVM; the score goes to the first one.
                    PutU32(r, XWingOffsets.TourScores + (t * 25 + operations[k].A) * 4, NonNegative(Int(op, "Score")));
                }
            }

            if (complete)
            {
                if (t < 3)
                {
                    r[XWingOffsets.Medals + t] = 1;
                }
                else
                {
                    r[XWingOffsets.ExpansionMedals + t - 3] = 1;
                }
            }
            else if ((string?)tour.Attribute("TourId") == activeTour)
            {
                var (a, b) = operations[progress];
                r[XWingOffsets.CurrentTour] = (byte)t;
                r[XWingOffsets.CurrentOperation] = (byte)progress;
                r[XWingOffsets.SelectedTourMission] = a;
                r[XWingOffsets.BriefingChoices] = a;
                r[XWingOffsets.BriefingChoices + 1] = b;
            }
        }

        foreach (var historic in game.Elements("HistoricTourRecord"))
        {
            var s = Array.IndexOf(XWingHistoricTours, (string?)historic.Attribute("TourId"));
            if (s < 0)
            {
                continue;
            }

            foreach (var op in historic.Elements("PilotOperationRecord"))
            {
                var j = Int(op, "TourStep");
                if (j is < 0 or >= 16)
                {
                    continue;
                }

                var score = NonNegative(Int(op, "Score"));
                var done = Bool(op, "Complete");
                if (s < 4)
                {
                    PutU32(r, XWingOffsets.HistoricScore + (s * 16 + j) * 4, score);
                    r[XWingOffsets.HistoricPatch + s * 16 + j] = (byte)(done ? 1 : 0);
                }
                else
                {
                    PutU32(r, XWingOffsets.BonusScore + j * 4, score);
                    r[XWingOffsets.BonusCompleted + j] = (byte)(done ? 1 : 0);
                }
            }
        }

        int totalKills = 0, totalCaptures = 0, dropped = 0;
        foreach (var victory in game.Elements("EntityVictoryRecord"))
        {
            var i = Array.IndexOf(XWingCraft, (string?)victory.Attribute("Id"));
            var kills = NonNegative(Int(victory, "Kills"));
            var captures = NonNegative(Int(victory, "Captures"));
            if (i < 0)
            {
                dropped += kills + captures;
                continue;
            }

            PutU16(r, XWingOffsets.KillsByType + i * 2, Math.Min(kills, ushort.MaxValue));
            PutU16(r, XWingOffsets.CapturesByType + i * 2, Math.Min(captures, ushort.MaxValue));
            totalKills += kills;
            totalCaptures += captures;
        }

        if (dropped > 0)
        {
            warnings.Add($"{dropped} kill(s)/capture(s) of craft X-Wing's pilot file has no slot for (such as the B-wing) were left out of the per-craft lists.");
        }

        PutU16(r, XWingOffsets.TotalKills, Math.Min(totalKills, ushort.MaxValue));
        PutU16(r, XWingOffsets.TotalCaptures, Math.Min(totalCaptures, ushort.MaxValue));

        var stats = Stats(game);
        PutU16(r, XWingOffsets.Ejections, Math.Min(stats.GetValueOrDefault("CraftLost"), ushort.MaxValue));
        PutU16(r, XWingOffsets.WarheadCraftHits, Math.Min(stats.GetValueOrDefault("ProjectilesHits"), ushort.MaxValue));
        PutU16(r, XWingOffsets.WarheadShots, Math.Min(stats.GetValueOrDefault("ProjectileShots"), ushort.MaxValue));
        PutU16(r, XWingOffsets.WarheadSurfaceHits, Math.Min(stats.GetValueOrDefault("ProjectilesGroundHits"), ushort.MaxValue));
        PutU32(r, XWingOffsets.LaserCraftHits, stats.GetValueOrDefault("LaserHits"));
        PutU32(r, XWingOffsets.LaserSurfaceHits, stats.GetValueOrDefault("LaserGroundHits"));
        PutU32(r, XWingOffsets.LaserShots, stats.GetValueOrDefault("LaserShots"));
        PutU16(r, XWingOffsets.SurfaceVictories, Math.Min(stats.GetValueOrDefault("SurfaceVictories"), ushort.MaxValue));

        notes.Add("Written as a new pilot the way X-Wing's registration desk creates one, then filled in from XWVM.");
        if (game.Elements("PilotTourRecord").Any(t => t.Elements("PilotOperationRecord").Any()))
        {
            notes.Add("XWVM does not record which of two alternative missions was flown; such scores are listed under the first one.");
        }

        return r;
    }

    private static byte[] ToTie(XElement game, Edition target, List<string> notes, List<string> warnings)
    {
        // A new pilot as TIE Fighter creates it (OpenTIE shipext_Init_Pilot): zeroes, training level 2 for every craft.
        var r = new byte[TiePilotFormat.RecordSize];
        r[TieOffsets.Version] = 1;
        r.AsSpan(TieOffsets.TrainingMaxLevel, TiePilotFormat.ShipCount).Fill(2);

        var status = Index(Statuses, (string?)game.Attribute("Status"));
        r[TieOffsets.Rank] = (byte)Index(Ranks, (string?)game.Attribute("Rank"));
        r[TieOffsets.Difficulty] = (byte)Index(Difficulties, (string?)game.Attribute("Difficulty"), fallback: 1);
        var score = NonNegative(Int(game, "CurrentScore"));
        PutI32(r, TieOffsets.Score, score);
        PutU16(r, TieOffsets.AverageScore, Math.Min(NonNegative(Int(game, "AiScore")) / 4, ushort.MaxValue));

        foreach (var award in game.Elements("PilotAwardRecord"))
        {
            if ((string?)award.Attribute("Award") == SecretOrderAward)
            {
                r[TieOffsets.SecretOrderRank] = (byte)Math.Clamp(Int(award, "Level") + 1, 1, 9);
            }
        }

        foreach (var training in game.Elements("TrainingRecord"))
        {
            var i = Array.IndexOf(TieCraft, (string?)training.Attribute("CraftType"));
            if (i >= 0)
            {
                PutI32(r, TieOffsets.TrainingScore + i * 4, NonNegative(Int(training, "HighScore")));
                r[TieOffsets.TrainingMaxLevel + i] = (byte)Math.Clamp(Int(training, "HighestLevelCompleted") + 1, 2, 15);
            }
        }

        var activeTour = (string?)game.Attribute("ActiveTour");
        var maxBattles = target.MaxBattles;
        foreach (var tour in game.Elements("PilotTourRecord"))
        {
            var id = (string?)tour.Attribute("TourId");
            var b = TourNumber(id, "battle") - 1;
            if (b < 0 || b >= TieBattles)
            {
                warnings.Add($"The XWVM tour \"{id}\" does not exist in TIE Fighter and was left out.");
                continue;
            }

            var step = Int(tour, "CurrentTourStep");
            var complete = step == Completed;
            var ops = tour.Elements("PilotOperationRecord").ToList();
            var cursor = complete
                ? (byte)Math.Clamp(ops.Select(o => Int(o, "TourStep") + 1).DefaultIfEmpty(0).Max(), 0, 8)
                : (byte)Math.Clamp(step, 0, 7);

            r[TieOffsets.BattleStatus + b] = (byte)(complete ? 3 : status != 0 && id == activeTour ? 2 : 1);
            r[TieOffsets.BattleCursor + b] = cursor;
            foreach (var op in ops)
            {
                var m = Int(op, "TourStep");
                if (m is < 0 or >= 8)
                {
                    continue;
                }

                PutI32(r, TieOffsets.TourScore + (b * 8 + m) * 4, NonNegative(Int(op, "Score")));
                if (Bool(op, "SecondaryGoals"))
                {
                    r[TieOffsets.SecretBits + b] |= (byte)(1 << m);
                }

                if (Bool(op, "BonusGoals"))
                {
                    r[TieOffsets.BonusBits + b] |= (byte)(1 << m);
                }
            }

            if (id == activeTour && !complete && b < maxBattles)
            {
                r[TieOffsets.CurrentBattle] = (byte)b;
            }
        }

        foreach (var historic in game.Elements("HistoricTourRecord"))
        {
            var s = TourNumber((string?)historic.Attribute("TourId"), "ship") - 1;
            if (s < 0 || s >= TieCraft.Length)
            {
                continue;
            }

            foreach (var op in historic.Elements("PilotOperationRecord"))
            {
                var j = Int(op, "TourStep");
                if (j is >= 0 and < 8)
                {
                    PutI32(r, TieOffsets.CombatScore + (s * 8 + j) * 4, NonNegative(Int(op, "Score")));
                    r[TieOffsets.CombatComplete + s * 8 + j] = (byte)(Bool(op, "Complete") ? 1 : 0);
                }
            }
        }

        int totalKills = 0, totalCaptures = 0, dropped = 0;
        foreach (var victory in game.Elements("EntityVictoryRecord"))
        {
            var i = Array.IndexOf(TieSpecies, (string?)victory.Attribute("Id"));
            var kills = NonNegative(Int(victory, "Kills"));
            var captures = NonNegative(Int(victory, "Captures"));
            if (i < 0)
            {
                dropped += kills + captures;
                continue;
            }

            PutU16(r, TieOffsets.KillsByType + i * 2, Math.Min(kills, ushort.MaxValue));
            PutU16(r, TieOffsets.CapturesByType + i * 2, Math.Min(captures, ushort.MaxValue));
            totalKills += kills;
            totalCaptures += captures;
        }

        if (dropped > 0)
        {
            warnings.Add($"{dropped} kill(s)/capture(s) of craft TIE Fighter does not have were left out.");
        }

        PutU16(r, TieOffsets.TotalKills, Math.Min(totalKills, ushort.MaxValue));
        PutU16(r, TieOffsets.TotalCaptures, Math.Min(totalCaptures, ushort.MaxValue));

        var stats = Stats(game);
        PutU16(r, TieOffsets.Ejections, Math.Min(stats.GetValueOrDefault("CraftLost"), ushort.MaxValue));
        PutU16(r, TieOffsets.WarheadHits, Math.Min(stats.GetValueOrDefault("ProjectilesHits"), ushort.MaxValue));
        PutU16(r, TieOffsets.WarheadShots, Math.Min(stats.GetValueOrDefault("ProjectileShots"), ushort.MaxValue));
        PutI32(r, TieOffsets.LaserHits, stats.GetValueOrDefault("LaserHits"));
        PutI32(r, TieOffsets.LaserShots, stats.GetValueOrDefault("LaserShots"));
        PutU16(r, TieOffsets.SecretCompletions, Math.Min(stats.GetValueOrDefault("SecretOrderVictories"), ushort.MaxValue));
        PutI32(r, TieOffsets.SecretScore, stats.GetValueOrDefault("SecretOrderPoints"));

        notes.Add("Written as a new pilot the way TIE Fighter creates one, then filled in from XWVM.");
        return r;
    }

    // ================================================================================================================
    // .vmpilot helpers
    // ================================================================================================================

    /// <summary>XWVM's file name for a pilot (PilotRoster.PilotNameToFileName, with Windows' invalid characters).</summary>
    public static string VmpilotFileName(string pilotName)
    {
        var invalid = new HashSet<char>("\"<>|:*?\\/%");
        for (var c = (char)0; c < 32; c++)
        {
            invalid.Add(c);
        }

        var sb = new StringBuilder();
        foreach (var ch in pilotName)
        {
            if (invalid.Contains(ch))
            {
                sb.Append('%').Append(((int)ch & 0xFF).ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb + ".vmpilot";
    }

    private static XDocument NewPilot(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An XWVM pilot needs a name.", nameof(name));
        }

        return new XDocument(new XElement("PilotRecord", new XAttribute("Name", name)));
    }

    private static XDocument LoadVmpilot(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        var document = XDocument.Load(reader);
        if (document.Root?.Name.LocalName != "PilotRecord")
        {
            throw new InvalidDataException("This is not an XWVM pilot file.");
        }

        return document;
    }

    private static byte[] SaveVmpilot(XDocument document, byte[]? original)
    {
        // XWVM saves with XElement.Save (UTF-8 with a byte-order mark); keep an existing file's choice.
        var bom = original is null || (original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF);
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(bom), Indent = true }))
        {
            document.Save(writer);
        }

        return stream.ToArray();
    }

    private static void InsertGame(XElement root, XElement record)
    {
        var order = Array.IndexOf(AllGameIds, (string?)record.Attribute("Id"));
        var after = root.Elements("PilotGameRecord").LastOrDefault(e => Array.IndexOf(AllGameIds, (string?)e.Attribute("Id")) < order);
        if (after is not null)
        {
            after.AddAfterSelf(record);
        }
        else if (root.Elements("PilotGameRecord").FirstOrDefault() is { } first)
        {
            first.AddBeforeSelf(record);
        }
        else
        {
            root.Add(record);
        }
    }

    private static void EnsureAllGames(XElement root)
    {
        foreach (var id in AllGameIds)
        {
            if (!root.Elements("PilotGameRecord").Any(e => (string?)e.Attribute("Id") == id))
            {
                InsertGame(root, GameElement(id, null, 0, 0, null, 0, 0));
            }
        }
    }

    // ---- element builders (attribute names and order as XWVM writes them) ----

    internal static XElement GameElement(string id, XElement? previous, int rank, int status, int? difficulty, int currentScore, int aiScore)
    {
        var difficultyName = difficulty is { } d
            ? Difficulties[Math.Clamp(d, 0, 2)]
            : (string?)previous?.Attribute("Difficulty") ?? "Medium";
        var game = new XElement("PilotGameRecord",
            new XAttribute("Id", id),
            new XAttribute("Rank", Ranks[Math.Clamp(rank, 0, 5)]),
            new XAttribute("Status", Statuses[Math.Clamp(status, 0, 2)]),
            new XAttribute("Difficulty", difficultyName),
            new XAttribute("CurrentScore", currentScore),
            new XAttribute("AiScore", aiScore));

        // The pilot's look in XWVM is not part of the old games; keep what the XWVM pilot already had.
        foreach (var name in new[] { "Droid", "AvatarPath" })
        {
            if (previous?.Attribute(name) is { } attribute)
            {
                game.SetAttributeValue(name, attribute.Value);
            }
        }

        game.SetAttributeValue("AvatarIndex", (string?)previous?.Attribute("AvatarIndex") ?? "-1");
        return game;
    }

    private static void CarryOver(XElement? previous, XElement game)
    {
        if (previous?.Element("CraftConfigurationPreferences") is { } preferences)
        {
            game.Add(new XElement(preferences));
        }
    }

    internal static XElement Tour(string element, string id, int step) =>
        new(element, new XAttribute("TourId", id), new XAttribute("CurrentTourStep", step.ToString(CultureInfo.InvariantCulture)));

    internal static XElement Operation(int step, int score, bool complete) =>
        new("PilotOperationRecord",
            new XAttribute("TourStep", step),
            new XAttribute("Score", score),
            new XAttribute("Complete", complete ? "true" : "false"));

    private static XElement Award(string name, int level) =>
        new("PilotAwardRecord", new XAttribute("Award", name), new XAttribute("Level", level));

    private static XElement Training(string craft, int score, int level) =>
        new("TrainingRecord", new XAttribute("CraftType", craft), new XAttribute("HighScore", score), new XAttribute("HighestLevelCompleted", level));

    internal static XElement Victory(string entity, int kills, int captures) =>
        new("EntityVictoryRecord", new XAttribute("Id", entity), new XAttribute("Kills", kills), new XAttribute("Captures", captures));

    internal static XElement Stat(string id, int value) =>
        new("StatRecord", new XAttribute("Id", id), new XAttribute("Value", value));

    // ---- small helpers ----

    internal static Dictionary<string, int> Stats(XElement game)
    {
        var stats = new Dictionary<string, int>();
        foreach (var stat in game.Elements("StatRecord"))
        {
            var id = (string?)stat.Attribute("Id");
            if (id is not null)
            {
                stats[id] = NonNegative(Int(stat, "Value"));
            }
        }

        return stats;
    }

    internal static int TourNumber(string? id, string prefix) =>
        id is not null && id.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(id[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static int Index(string[] names, string? value, int fallback = 0)
    {
        var i = Array.IndexOf(names, value);
        return i >= 0 ? i : fallback;
    }

    internal static int Int(XElement e, string attribute) =>
        int.TryParse((string?)e.Attribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    internal static bool Bool(XElement e, string attribute) =>
        bool.TryParse((string?)e.Attribute(attribute), out var v) && v;

    private static int NonNegative(int value) => Math.Max(value, 0);

    private static int ClampInt(long value) => (int)Math.Min(value, int.MaxValue);

    private static long U32(byte[] r, int o) => BinaryPrimitives.ReadUInt32LittleEndian(r.AsSpan(o));

    private static int U16(byte[] r, int o) => BinaryPrimitives.ReadUInt16LittleEndian(r.AsSpan(o));

    private static int I32NonNegative(byte[] r, int o) => Math.Max(BinaryPrimitives.ReadInt32LittleEndian(r.AsSpan(o)), 0);

    private static void PutU32(byte[] r, int o, int v) => BinaryPrimitives.WriteUInt32LittleEndian(r.AsSpan(o), (uint)v);

    private static void PutI32(byte[] r, int o, int v) => BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(o), v);

    private static void PutU16(byte[] r, int o, int v) => BinaryPrimitives.WriteUInt16LittleEndian(r.AsSpan(o), (ushort)v);
}
