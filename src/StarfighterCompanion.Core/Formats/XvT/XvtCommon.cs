using StarfighterCompanion.Core.Binary;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Formats.XvT;

/// <summary>Structures shared by the X-Wing vs. TIE Fighter (.plt) and Balance of Power (.pl2) pilot files.</summary>
internal static class XvtCommon
{
    public static readonly string[] MissionTypes = { "Training", "Melee", "Combat" };

    public static readonly string[] Ratings =
    {
        "Target Drone", "Ground Crew", "Trainee", "Flight Cadet",
        "Officer 4th Class", "Officer 3rd Class", "Officer 2nd Class", "Officer 1st Class",
        "Veteran 4th Grade", "Veteran 3rd Grade", "Veteran 2nd Grade", "Veteran 1st Grade",
        "Ace 4th Level", "Ace 3rd Level", "Ace 2nd Level", "Ace 1st Level",
        "Top Ace 4th Order", "Top Ace 3rd Order", "Top Ace 2nd Order", "Top Ace 1st Order",
        "Jedi 4th Degree", "Jedi 3rd Degree", "Jedi 2nd Degree", "Jedi 1st Degree", "Jedi Master",
    };

    public static readonly string[] AiRatings = { "Novice", "Officer", "Veteran", "Ace", "Top Ace", "Jedi" };

    public static readonly IReadOnlyList<ChoiceOption> RatingChoices = Choices.Indexed(Ratings);

    public static readonly IReadOnlyList<ChoiceOption> PromotionChoices =
        Choices.Of((-1, "Demoted"), (0, "Unchanged"), (1, "Promoted"));

    // CraftSpecies values from OpenXvT src/xvt/assets/object_type.h (index = species id).
    public static readonly string[] Species =
    {
        "(none)", "X-wing", "Y-wing", "A-wing", "B-wing", "TIE Fighter", "TIE Interceptor", "TIE Bomber",
        "TIE Advanced", "TIE Defender", "(unused 10)", "(unused 11)", "Missile Boat", "T-wing", "Z-95 Headhunter",
        "R-41 Starchaser", "Assault Gunboat", "Shuttle", "Escort Shuttle", "System Patrol Craft", "Scout Craft",
        "Stormtrooper Transport", "Assault Transport", "Escort Transport", "Utility Tug", "Combat Utility Vehicle",
        "Container A", "Container B", "Container C", "Container D", "Heavy Lifter", "Bulk Barge", "Bulk Freighter",
        "Cargo Ferry", "Modular Conveyor", "Container Transport", "Heavy Transport", "Muurian Transport",
        "Corellian Transport", "Millennium Falcon", "Corellian Corvette", "Modified Corvette", "Nebulon-B Frigate",
        "Modified Frigate", "Passenger Liner", "Carrack Cruiser", "Strike Cruiser", "Escort Carrier", "Dreadnaught",
        "Calamari Cruiser", "Light Calamari Cruiser", "Interdictor", "Victory Star Destroyer",
        "Imperial Star Destroyer", "Super Star Destroyer", "Container E", "Container F", "Container G",
        "Container H", "Container I", "XQ1 Platform", "XQ2 Platform", "XQ3 Platform", "XQ4 Platform",
        "XQ5 Platform", "XQ6 Platform", "Asteroid Hangar", "Asteroid Laser Battery", "Asteroid Warhead Launcher",
        "X/7 Factory", "Comm Sat 1", "Comm Sat 2", "Satellite 3", "Satellite 4", "Satellite 5", "Mine type A",
        "Mine type B", "Mine type C", "Gun Emplacement", "Mine 5", "Probe", "Probe 2", "Probe 3", "Nav Buoy 1",
        "Nav Buoy 2", "Pilot", "Asteroid", "Planet", "Obstacle", "Component", "Shipyard", "Repair Yard",
        "Modified Strike Cruiser", "(unused 93)", "(unused 94)", "(unused 95)", "(unused 96)", "(unused 97)",
        "(unused 98)", "(unused 99)",
    };

    private static readonly FieldOptions Positive = FieldOptions.NonNegative;
    private static readonly FieldOptions AnyInt = FieldOptions.None;

    /// <summary>PilotStats (craft count 88 for XvT, 100 for Balance of Power).</summary>
    public static void Stats(LayoutBuilder b, int crafts)
    {
        string Craft(int i) => i < Species.Length ? Species[i] : $"Craft {i}";

        PerType(b, "scoreByType", "Score", AnyInt);
        PerType(b, "standalonePlayed", "Standalone missions played", Positive);
        PerType(b, "sequencePlayed", "Sequence missions played", Positive);
        PerType(b, "kills", "Kills", Positive);
        PerType(b, "friendlyKills", "Friendly craft killed", Positive);
        PerTypeArray(b, "killsByCraft", "Kills by craft", crafts, Craft);
        PerTypeArray(b, "sharedKillsByCraft", "Shared kills by craft", crafts, Craft);
        PerTypeArray(b, "assistsByCraft", "Assists by craft", crafts, Craft);
        PerTypeArray(b, "killsOnPlayerRating", "Kills on players by rating", 25, i => Ratings[i]);
        PerTypeArray(b, "sharedKillsOnPlayerRating", "Shared kills on players by rating", 25, i => Ratings[i]);
        PerTypeArray(b, "assistsOnPlayerRating", "Assists on players by rating", 25, i => Ratings[i]);
        PerTypeArray(b, "killsOnAiRating", "Kills on AI by skill", 6, i => AiRatings[i]);
        PerTypeArray(b, "sharedKillsOnAiRating", "Shared kills on AI by skill", 6, i => AiRatings[i]);
        PerTypeArray(b, "assistsOnAiRating", "Assists on AI by skill", 6, i => AiRatings[i]);
        PerType(b, "inspections", "Special craft inspected", Positive);
        PerType(b, "energyHits", "Laser/ion hits", Positive);
        PerType(b, "energyFired", "Laser/ion shots fired", Positive);
        PerType(b, "warheadHits", "Warhead hits", Positive);
        PerType(b, "warheadsFired", "Warheads fired", Positive);
        PerType(b, "losses", "Craft lost", Positive);
        PerType(b, "lossesCollision", "Craft lost in collisions", Positive);
        PerType(b, "lossesStarship", "Craft lost to starships", Positive);
        PerType(b, "lossesMine", "Craft lost to mines", Positive);
        PerTypeArray(b, "killedByPlayerRating", "Killed by players by rating", 25, i => Ratings[i]);
        PerTypeArray(b, "killedByAiRating", "Killed by AI by skill", 6, i => AiRatings[i]);
    }

    private static void PerType(LayoutBuilder b, string id, string label, FieldOptions o)
    {
        using (b.Group("Totals"))
        {
            b.Array(id, BinaryKind.Int32, 3, i => $"{label} ({MissionTypes[i]})", o);
        }
    }

    private static void PerTypeArray(LayoutBuilder b, string id, string label, int count, Func<int, string> item)
    {
        for (var t = 0; t < 3; t++)
        {
            var type = t;
            using (b.Group($"{label} ({MissionTypes[type]})"))
            {
                b.Array($"{id}[{type}]", BinaryKind.Int32, count, item, Positive);
            }
        }
    }

    /// <summary>Common head of PilotData / PilotXvtRecord.</summary>
    public static void Header(LayoutBuilder b)
    {
        using (b.Group("Pilot"))
        {
            b.Text("name", "Name", 14, new FieldOptions
            {
                ReadOnly = true,
                Help = "The game finds a pilot's files by this name, so it is not editable here. Rename the pilot in the game instead.",
            });
            b.I32("totalScore", "Total score");
        }

        b.Skip(4 * 5); // local player id, session marker, host flag, human players in last mission, game mode
    }

    /// <summary>Single player mission history record (36 bytes).</summary>
    public static void MissionRecords(LayoutBuilder b, string id, string group, int count)
    {
        using (b.Group(group))
        {
            b.Repeat(count, id, _ => null, i =>
            {
                var m = $"Mission {i + 1}";
                b.I32("flown", $"{m}: times flown", Positive);
                b.I32("completed", $"{m}: times completed", Positive);
                b.I32("failed", $"{m}: times failed", Positive);
                b.I32("bestScore", $"{m}: best score", AnyInt);
                b.I32("bestTime", $"{m}: best time", Positive);
                b.I32("bestPlacement", $"{m}: best placement", AnyInt);
                b.I32("award", $"{m}: award", AnyInt);
                b.U32("bestBonus", $"{m}: best bonus");
                b.Skip(4);
            });
        }
    }

    /// <summary>Multiplayer mission history record (48 bytes).</summary>
    public static void MultiplayerMissionRecords(LayoutBuilder b, string id, string group, int count)
    {
        using (b.Group(group))
        {
            b.Repeat(count, id, _ => null, i =>
            {
                var m = $"Mission {i + 1}";
                b.I32("flown", $"{m}: times flown", Positive);
                b.I32("first", $"{m}: 1st places", Positive);
                b.I32("second", $"{m}: 2nd places", Positive);
                b.I32("third", $"{m}: 3rd places", Positive);
                b.I32("completed", $"{m}: times completed", Positive);
                b.I32("failed", $"{m}: times failed", Positive);
                b.I32("bestScore", $"{m}: best score", AnyInt);
                b.I32("bestTime", $"{m}: best time", Positive);
                b.I32("bestPlacement", $"{m}: best placement", AnyInt);
                b.I32("award", $"{m}: award", AnyInt);
                b.U32("bestBonus", $"{m}: best bonus");
                b.Skip(4);
            });
        }
    }

    public static void Tournaments(LayoutBuilder b, string id, string group, bool multiplayer)
    {
        using (b.Group(group))
        {
            b.Repeat(25, id, _ => null, i =>
            {
                var m = $"Tournament {i + 1}";
                b.I32("attempts", $"{m}: attempts", Positive);
                b.I32("completed", $"{m}: completed", Positive);
                b.I32("first", $"{m}: 1st places", Positive);
                b.I32("second", $"{m}: 2nd places", Positive);
                b.I32("third", $"{m}: 3rd places", Positive);
                b.I32("bestScore", $"{m}: best score", AnyInt);
                b.I32("bestPlacement", $"{m}: best placement", AnyInt);
                if (multiplayer)
                {
                    b.Skip(4);
                }

                b.I32("award", $"{m}: award", AnyInt);
                b.U32("bestMargin", $"{m}: best margin");
                b.Skip(4);
            });
        }
    }

    public static void Battles(LayoutBuilder b, string id, string group, bool multiplayer)
    {
        using (b.Group(group))
        {
            b.Repeat(25, id, _ => null, i =>
            {
                var m = $"Battle {i + 1}";
                b.I32("attempts", $"{m}: attempts", Positive);
                b.I32("victories", $"{m}: victories", Positive);
                b.I32("defeats", $"{m}: defeats", Positive);
                b.I32("draws", $"{m}: draws", Positive);
                b.I32("bestScore", $"{m}: best score", AnyInt);
                b.Skip(multiplayer ? 8 : 4);
                b.I32("award", $"{m}: award", AnyInt);
                b.U32("bestMargin", $"{m}: best victory margin");
                b.Skip(4);
            });
        }
    }

    public static void Awards(LayoutBuilder b)
    {
        using (b.Group("Awards"))
        {
            b.Array("meleePlaques", BinaryKind.Int32, 6, i => $"Melee plaques, level {i + 1}", Positive);
            b.Array("tournamentTrophies", BinaryKind.Int32, 6, i => $"Tournament trophies, level {i + 1}", Positive);
            b.Array("missionEvaluations", BinaryKind.Int32, 6, i => $"Mission evaluations, level {i + 1}", Positive);
            b.Array("battleMedallions", BinaryKind.Int32, 6, i => $"Battle medallions, level {i + 1}", Positive);
            b.Array("currentAward", BinaryKind.Int32, 4, i => $"Current {new[] { "melee", "tournament", "evaluation", "battle" }[i]} award level", AnyInt);
        }
    }

    /// <summary>Per-target kill tables that follow the rating block.</summary>
    public static void LastMission(LayoutBuilder b, int flightGroups)
    {
        using (b.Group("Last mission"))
        {
            b.I32("missionScore", "Score");
            b.Skip(4 * (8 + 8 + flightGroups + flightGroups + 8 + 8 + flightGroups + flightGroups + flightGroups));
        }
    }

    public static string FactionLabel(int i) => $"Faction record {i + 1}";

    /// <summary>Rules common to XvT, BoP and XWA.</summary>
    public static IEnumerable<ValidationIssue> MissionRules(PilotDocument doc, string prefix, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var flown = doc.Field($"{prefix}[{i}].flown");
            var completed = doc.Field($"{prefix}[{i}].completed");
            var failed = doc.Field($"{prefix}[{i}].failed");
            if (completed.GetInteger() + failed.GetInteger() > flown.GetInteger())
            {
                yield return CommonRules.Odd($"{flown.Group}: mission {i + 1} has more completions and failures than times flown.", flown);
            }
        }
    }
}
