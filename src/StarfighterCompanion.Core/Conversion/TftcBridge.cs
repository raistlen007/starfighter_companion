using System.Text.RegularExpressions;
using System.Xml.Linq;
using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.Xwa;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Conversion;

/// <summary>
/// Moves a TIE Fighter career between TIE Fighter (and XWVM) and TIE Fighter Total Conversion Classic.
/// TFTC Classic has TIE Fighter's 13 battles with the same missions, and its Combat Chamber has the same historical
/// missions per craft, so both map one to one through the built-in TFTC mission lists. The conversion goes through
/// XWVM's TIE Fighter game record, which TIE Fighter and XWVM already convert to and from.
/// Craft types: TFTC keeps X-Wing Alliance's numbering, where TIE Fighter species i is craft type i + 1.
/// </summary>
public static partial class TftcBridge
{
    public const string VariantId = "tftc";

    private static readonly XwaEdition Edition = XwaEdition.For(VariantId);

    /// <summary>Mission number in the pilot file for each battle (1-13) and mission.</summary>
    private static readonly Dictionary<int, int[]> Battles = Edition.Campaign!
        .Select(e => (Battle: BattleNumber().Match(e.Section), e.MissionIndex))
        .Where(x => x.Battle.Success)
        .GroupBy(x => int.Parse(x.Battle.Groups[1].Value))
        .ToDictionary(g => g.Key, g => g.Select(x => x.MissionIndex).ToArray());

    /// <summary>Combat Chamber mission numbers per TIE Fighter combat craft (XwvmBridge.TieCraft order).</summary>
    private static readonly int[][] CombatCraft = BuildCombatCraft();

    private static readonly string[] Types = { "[0]", "[1]", "[2]" };

    [GeneratedRegex(@"^Battle (\d+)\b")]
    private static partial Regex BattleNumber();

    private static int[][] BuildCombatCraft()
    {
        string[] sections = { "TIE Fighter Training", "TIE Interceptor Training", "TIE Bomber Training", "TIE Advanced Training",
                              "Assault Gunboat Training", "TIE Defender Training", "Missile Boat Training" };
        return sections.Select(s => Edition.Combat!.Where(e => e.Section == s).Select(e => e.MissionIndex).ToArray()).ToArray();
    }

    // ================================================================================================================
    // TFTC → XWVM's TIE Fighter record
    // ================================================================================================================

    internal static XElement ToGame(byte[] plt, List<string> notes, List<string> warnings)
    {
        var doc = Open(plt);
        long Get(string id) => doc.Field(id).GetInteger();
        int Clamp(long v) => (int)Math.Clamp(v, 0, int.MaxValue);

        var game = XwvmBridge.GameElement(XwvmBridge.TieGameId, null, rank: 0, status: 0, difficulty: null,
            currentScore: Clamp(Get("faction[0].totalScore")), aiScore: 0);

        var battles = new List<(int Number, bool[] Done, int[] Scores, int Reached, bool Complete, bool Started)>();
        for (var b = 1; b <= 13; b++)
        {
            var missions = Battles.GetValueOrDefault(b) ?? Array.Empty<int>();
            var done = missions.Select(i => Get($"tod[{i}].completed") > 0).ToArray();
            var scores = missions.Select(i => Clamp(Get($"tod[{i}].bestScore"))).ToArray();
            var reached = done.TakeWhile(d => d).Count();
            var complete = missions.Length > 0 && reached == missions.Length;
            battles.Add((b, done, scores, reached, complete, done.Any(d => d) || scores.Any(s => s > 0)));
        }

        // The battle being played: the last one started and not finished, else the first one after a finished battle.
        var active = battles.LastOrDefault(x => x.Started && !x.Complete).Number;
        if (active == 0)
        {
            active = battles.FirstOrDefault(x => !x.Complete && (x.Number == 1 || battles[x.Number - 2].Complete)).Number;
        }

        foreach (var (b, done, scores, reached, complete, started) in battles)
        {
            if (!started && !complete && b != active)
            {
                continue;
            }

            var tour = XwvmBridge.Tour("PilotTourRecord", $"battle{b}", complete ? XwvmBridge.Completed : Math.Min(reached, 7));
            for (var m = 0; m < done.Length && m < 8; m++)
            {
                if (done[m] || scores[m] > 0)
                {
                    tour.Add(XwvmBridge.Operation(m, scores[m], done[m]));
                }
            }

            game.Add(tour);
        }

        if (active != 0)
        {
            game.SetAttributeValue("ActiveTour", $"battle{active}");
        }

        for (var s = 0; s < CombatCraft.Length; s++)
        {
            var historic = XwvmBridge.Tour("HistoricTourRecord", $"ship{s + 1}", 0);
            for (var j = 0; j < CombatCraft[s].Length && j < 8; j++)
            {
                var i = CombatCraft[s][j];
                var score = Clamp(Get($"combat[{i}].bestScore"));
                var complete = Get($"combat[{i}].completed") > 0;
                if (score > 0 || complete)
                {
                    historic.Add(XwvmBridge.Operation(j, score, complete));
                }
            }

            game.Add(historic);
        }

        var dropped = 0L;
        for (var c = 1; c < XwaPilotFormat.CraftTypes; c++)
        {
            var kills = Types.Sum(t => Get($"main.killsByCraft{t}[{c}]"));
            if (kills <= 0)
            {
                continue;
            }

            var species = c - 1;
            if (species < XwvmBridge.TieSpecies.Length && XwvmBridge.TieSpecies[species] is { } entity)
            {
                game.Add(XwvmBridge.Victory(entity, Clamp(kills), 0));
            }
            else
            {
                dropped += kills;
            }
        }

        if (dropped > 0)
        {
            warnings.Add($"{dropped} kill(s) of craft that TIE Fighter does not have were left out.");
        }

        int Sum(string stat) => Clamp(Types.Sum(t => Get($"main.{stat}{t}")));
        game.Add(XwvmBridge.Stat("CraftLost", Sum("losses")));
        game.Add(XwvmBridge.Stat("ProjectilesHits", Sum("warheadHits")));
        game.Add(XwvmBridge.Stat("ProjectileShots", Sum("warheadsFired")));
        game.Add(XwvmBridge.Stat("LaserHits", Sum("energyHits")));
        game.Add(XwvmBridge.Stat("LaserShots", Sum("energyFired")));

        notes.Add("Battles, mission scores, Combat Chamber missions, kills by craft and weapon statistics were taken from TFTC.");
        notes.Add("TFTC does not keep TIE Fighter's rank, Secret Order, secret and bonus objectives or training simulator levels; " +
                  "they start as for a new pilot. Kills are counted over all of TFTC's statistics.");
        return game;
    }

    // ================================================================================================================
    // XWVM's TIE Fighter record → TFTC
    // ================================================================================================================

    /// <summary>Writes the career into a TFTC pilot (an existing one, or <see cref="NewPilot"/>). Everything else in the pilot is kept.</summary>
    internal static byte[] FromGame(XElement game, byte[] existingPlt, List<string> notes, List<string> warnings)
    {
        var doc = Open(existingPlt);
        void Set(string id, long value) => doc.Field(id).ForceInteger(value);
        long Get(string id) => doc.Field(id).GetInteger();

        var tours = game.Elements("PilotTourRecord").ToDictionary(t => XwvmBridge.TourNumber((string?)t.Attribute("TourId"), "battle"), t => t);
        for (var b = 1; b <= 13; b++)
        {
            var missions = Battles.GetValueOrDefault(b) ?? Array.Empty<int>();
            tours.TryGetValue(b, out var tour);
            var complete = tour is not null && XwvmBridge.Int(tour, "CurrentTourStep") == XwvmBridge.Completed;
            var step = tour is null ? 0 : XwvmBridge.Int(tour, "CurrentTourStep");
            var ops = tour?.Elements("PilotOperationRecord").ToDictionary(o => XwvmBridge.Int(o, "TourStep"), o => o) ?? new();
            for (var m = 0; m < missions.Length; m++)
            {
                var i = missions[m];
                ops.TryGetValue(m, out var op);
                var done = complete || (op is not null && XwvmBridge.Bool(op, "Complete")) || (tour is not null && m < step);
                Set($"tod[{i}].completed", done ? Math.Max(Get($"tod[{i}].completed"), 1) : 0);
                Set($"tod[{i}].bestScore", op is null ? 0 : Math.Max(XwvmBridge.Int(op, "Score"), 0));
                if (done && Get($"tod[{i}].flown") == 0)
                {
                    Set($"tod[{i}].flown", 1);
                }
            }
        }

        foreach (var extra in tours.Keys.Where(k => k is < 1 or > 13))
        {
            warnings.Add($"Battle {extra} does not exist in TFTC and was left out.");
        }

        var historic = game.Elements("HistoricTourRecord").ToDictionary(t => XwvmBridge.TourNumber((string?)t.Attribute("TourId"), "ship") - 1, t => t);
        for (var s = 0; s < CombatCraft.Length; s++)
        {
            historic.TryGetValue(s, out var ship);
            var ops = ship?.Elements("PilotOperationRecord").ToDictionary(o => XwvmBridge.Int(o, "TourStep"), o => o) ?? new();
            for (var j = 0; j < CombatCraft[s].Length; j++)
            {
                var i = CombatCraft[s][j];
                ops.TryGetValue(j, out var op);
                var done = op is not null && XwvmBridge.Bool(op, "Complete");
                Set($"combat[{i}].completed", done ? Math.Max(Get($"combat[{i}].completed"), 1) : 0);
                Set($"combat[{i}].bestScore", op is null ? 0 : Math.Max(XwvmBridge.Int(op, "Score"), 0));
                if (done && Get($"combat[{i}].flown") == 0)
                {
                    Set($"combat[{i}].flown", 1);
                }
            }

            var unused = ops.Keys.Count(j => j >= CombatCraft[s].Length);
            if (unused > 0)
            {
                warnings.Add($"{unused} Combat Chamber mission(s) of {XwvmBridge.TieCraft[s]} that TFTC does not have were left out.");
            }
        }

        // Kills replace the campaign statistics (the career record of campaign battles and the career totals).
        var kills = new long[XwaPilotFormat.CraftTypes];
        var dropped = 0L;
        foreach (var victory in game.Elements("EntityVictoryRecord"))
        {
            var species = Array.IndexOf(XwvmBridge.TieSpecies, (string?)victory.Attribute("Id"));
            var count = Math.Max(XwvmBridge.Int(victory, "Kills"), 0);
            if (species < 0)
            {
                dropped += count;
                continue;
            }

            kills[species + 1] = count;
        }

        if (dropped > 0)
        {
            warnings.Add($"{dropped} kill(s) of craft that TFTC's TIE Fighter numbering does not have were left out.");
        }

        var stats = XwvmBridge.Stats(game);
        foreach (var prefix in new[] { "main.", "faction[0].stats." })
        {
            for (var species = 0; species < XwvmBridge.TieSpecies.Length; species++)
            {
                if (XwvmBridge.TieSpecies[species] is not null)
                {
                    Set($"{prefix}killsByCraft[0][{species + 1}]", kills[species + 1]);
                }
            }

            Set($"{prefix}kills[0]", Math.Min(kills.Sum(), int.MaxValue));
            Set($"{prefix}energyFired[0]", stats.GetValueOrDefault("LaserShots"));
            Set($"{prefix}energyHits[0]", stats.GetValueOrDefault("LaserHits"));
            Set($"{prefix}warheadsFired[0]", stats.GetValueOrDefault("ProjectileShots"));
            Set($"{prefix}warheadHits[0]", stats.GetValueOrDefault("ProjectilesHits"));
            Set($"{prefix}losses[0]", stats.GetValueOrDefault("CraftLost"));
        }

        notes.Add("Battles, mission scores, Combat Chamber missions, kills by craft and weapon statistics replace the TFTC pilot's; " +
                  "its pilot training, rating, awards and everything else are kept.");
        notes.Add("TFTC has no rank, Secret Order, secret or bonus objectives or training simulator levels in TIE Fighter's form; those were not carried over.");
        return doc.Save();
    }

    // ================================================================================================================
    // A new TFTC pilot
    // ================================================================================================================

    /// <summary>Longest pilot name: the name field holds 14 bytes with the terminating zero.</summary>
    public const int NameMaxLength = 13;

    /// <summary>
    /// A new pilot as TFTC Classic registers one (OpenXWA Pilot_CreateNew): zeros, the name, rating 2 with TFTC's
    /// title for it, three mission skips, the multiplayer game and host names, the first campaign and melee missions
    /// selected, and the craft TFTC's SHIPLIST.TXT marks Known unlocked in the tech library. The values that come from
    /// TFTC's own files (rating title FRONTTXT.TXT string 24 "Civilian", string 353 "'s Game", the first melee mission
    /// 58 from MELEE\MISSION.LST, the Known craft) were taken once from a TFTC Classic installation.
    /// </summary>
    public static byte[] NewPilot(string name)
    {
        const int Tour = 4, Skirmish = 3, FirstMelee = 58;
        var data = new byte[XwaPilotFormat.RecordSize];
        var doc = Open(data);
        doc.Field("name").ForceText(name);
        doc.Field("rating").ForceInteger(2);
        doc.Field("ratingName").ForceText("Civilian");
        doc.Field("skipsRemaining").ForceInteger(3);
        doc.Field("gameName").ForceText(name + "'s Game");
        doc.Field("hostName").ForceText(name + "'s Game");
        foreach (var craft in KnownCraft())
        {
            doc.Field($"craftKnown[{craft}]").ForceInteger(1);
        }

        data = doc.Save();

        // The selected mission per mission directory is not an editor field; place it by the fields around it
        // (PilotData: team, missionDirectoryId, missionDescriptionIds[7] right before the multiplayer game name, and
        // PilotFaction: totalMissionsPlayedCount, team, missionDirectoryId, missionDescriptionIds[7]).
        int Offset(string id) => XwaPilotFormat.Schema.Fields.Single(f => f.Id == id).Offset;
        void Put(int offset, int value) => System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset), value);
        var selected = Offset("gameName") - 4 * 7;
        int FactionDirectory(int f) => Offset($"faction[{f}].missionsPlayed") + 8;
        int FactionSelected(int f, int directory) => FactionDirectory(f) + 4 + 4 * directory;

        var firstTour = Edition.Campaign![0].MissionIndex;
        Put(selected + 4 * Tour, firstTour);
        Put(FactionDirectory(0), Tour);
        Put(FactionSelected(0, Tour), firstTour);

        Put(selected, FirstMelee);
        Put(FactionSelected(0, 1), FirstMelee);
        Put(FactionSelected(2, 1), FirstMelee);
        Put(FactionDirectory(2), Skirmish);
        return data;
    }

    /// <summary>Returns why <paramref name="name"/> cannot be a new TFTC pilot's name, or null.</summary>
    public static string? CheckNewName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a name.";
        }

        if (name.Length > NameMaxLength)
        {
            return $"TFTC pilot names can be at most {NameMaxLength} characters.";
        }

        return name.Any(c => c < 32 || c > 126 || Path.GetInvalidFileNameChars().Contains(c) || c is '\\' or '/' or ':' or '*' or '?' or '"' or '<' or '>' or '|')
            ? "Use letters, digits and simple punctuation only."
            : null;
    }

    /// <summary>The file name the game gives a new pilot: &lt;name&gt;0.plt, or the next free number.</summary>
    public static string NewPilotPath(string folder, string name)
    {
        for (var i = 0; ; i++)
        {
            var path = Path.Combine(folder, $"{name}{i}.plt");
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    private static IEnumerable<int> KnownCraft()
    {
        var assembly = typeof(TftcBridge).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("CraftLists.tftc.known.txt", StringComparison.OrdinalIgnoreCase));
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        while (reader.ReadLine() is { } line)
        {
            if (int.TryParse(line.Trim(), out var id) && id is > 0 and < XwaPilotFormat.CraftTypes)
            {
                yield return id;
            }
        }
    }

    private static PilotDocument Open(byte[] plt)
    {
        if (plt.Length != XwaPilotFormat.RecordSize)
        {
            throw new InvalidDataException("This is not an X-Wing Alliance / TFTC pilot file.");
        }

        return new XwaPilotFormat().Open("PILOT.plt", plt, VariantId);
    }

    // ================================================================================================================
    // Whole conversions
    // ================================================================================================================

    /// <summary>A TFTC pilot as a TIE Fighter pilot for <paramref name="target"/>.</summary>
    public static ConversionResult ToTie(byte[] plt, Library.Edition target)
    {
        var notes = new List<string>();
        var warnings = new List<string>();
        var game = ToGame(plt, notes, warnings);
        var data = XwvmBridge.GameToTie(game, target, new List<string>(), warnings);
        return new ConversionResult(data, notes, warnings.Distinct().ToList());
    }

    /// <summary>A TFTC pilot as the TIE Fighter career of an XWVM pilot.</summary>
    public static ConversionResult ToXwvm(byte[] plt, byte[]? existingVmpilot, string pilotName)
    {
        var notes = new List<string>();
        var warnings = new List<string>();
        var game = ToGame(plt, notes, warnings);
        var data = XwvmBridge.PutTieGame(game, existingVmpilot, pilotName, notes);
        return new ConversionResult(data, notes, warnings.Distinct().ToList());
    }

    /// <summary>A TIE Fighter pilot written into an existing TFTC pilot.</summary>
    public static ConversionResult FromTie(byte[] tfr, byte[] existingPlt)
    {
        var notes = new List<string>();
        var warnings = new List<string>();
        var game = XwvmBridge.TieToGame(tfr, new List<string>(), warnings);
        return new ConversionResult(FromGame(game, existingPlt, notes, warnings), notes, warnings.Distinct().ToList());
    }

    /// <summary>The TIE Fighter career of an XWVM pilot written into an existing TFTC pilot.</summary>
    public static ConversionResult FromXwvm(byte[] vmpilot, byte[] existingPlt)
    {
        var notes = new List<string>();
        var warnings = new List<string>();
        var game = XwvmBridge.TieGameOf(vmpilot) ?? throw new InvalidDataException("This XWVM pilot has no TIE Fighter career.");
        return new ConversionResult(FromGame(game, existingPlt, notes, warnings), notes, warnings.Distinct().ToList());
    }
}
