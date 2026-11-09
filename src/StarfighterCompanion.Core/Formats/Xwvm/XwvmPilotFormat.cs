using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Formats.Xwvm;

/// <summary>
/// XWVM (X-Wing Virtual Machine) pilot file (<c>.vmpilot</c>): an XML document with one record per game.
/// Element and attribute names follow XWVM's Assets/Code/GameState/*.cs.
/// The document is edited in place, so anything the editor does not know about is kept.
/// </summary>
public sealed class XwvmPilotFormat : PilotFormat
{
    public override string Game => "XWVM";

    public override string Name => "XWVM pilot (.vmpilot)";

    public override IReadOnlyList<string> Extensions { get; } = new[] { ".vmpilot" };

    public override string Signature => "an XML file whose root element is <PilotRecord>";

    public override bool Matches(string fileName, ReadOnlySpan<byte> data)
    {
        if (!HasExtension(fileName, Extensions))
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(data.ToArray());
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
            reader.MoveToContent();
            return reader.NodeType == XmlNodeType.Element && reader.LocalName == "PilotRecord";
        }
        catch (XmlException)
        {
            return false;
        }
    }

    public override PilotDocument Open(string fileName, byte[] data) => new XwvmPilotDocument(this, data);
}

internal sealed class XwvmPilotDocument : PilotDocument
{
    internal const int CompletedStep = int.MaxValue;

    private static readonly Dictionary<string, string> GameNames = new()
    {
        ["xwing"] = "X-Wing",
        ["tf"] = "TIE Fighter",
        ["xvt"] = "X-Wing vs. TIE Fighter",
        ["xwa"] = "X-Wing Alliance",
    };

    /// <summary>Every XWVM pilot carries these game records; XWVM looks them up without checking (PilotRecord).</summary>
    internal static readonly string[] AllGames = { "xwing", "tf", "xvt", "xwa" };

    internal static readonly string[] Ranks = { "FltCadet", "FltOfficer", "Lieutenant", "Captain", "Commander", "General" };
    internal static readonly string[] RankLabels = { "Flight Cadet", "Flight Officer", "Lieutenant", "Captain", "Commander", "General" };
    internal static readonly string[] Statuses = { "Alive", "Captured", "Dead" };
    internal static readonly string[] Difficulties = { "Easy", "Medium", "Hard" };

    /// <summary>The order XWVM writes a game record's children in (PilotGameRecord.ToXElement).</summary>
    private static readonly string[] ChildOrder =
    {
        "PilotAwardRecord", "TrainingRecord", "PilotTourRecord", "HistoricTourRecord", "EntityVictoryRecord", "StatRecord",
        "CraftConfigurationPreferences",
    };

    /// <summary>What XWVM knows about each game it plays (GameMetadata/XWing, GameMetadata/Tie, GameState).</summary>
    private sealed record GameSchema(
        (string Id, string Label, int Steps)[] Tours,
        (string Id, string Label, int Missions)[] Historic,
        (string Id, string Label)[] TrainingCraft,
        int FlightBadgeLevel,
        string? Award,
        string[] AwardLevels,
        (string Id, string Label)[] Craft,
        (string Id, string Label)[] Stats,
        bool ObjectivePins);

    private static readonly (string, string)[] CommonStats =
    {
        ("LaserShots", "Laser shots fired"), ("LaserHits", "Laser hits"), ("LaserGroundHits", "Laser hits on ground targets"),
        ("ProjectileShots", "Warheads fired"), ("ProjectilesHits", "Warhead hits"), ("ProjectilesGroundHits", "Warhead hits on ground targets"),
        ("CraftLost", "Craft lost"), ("SurfaceVictories", "Surface victories"),
    };

    private static readonly Dictionary<string, GameSchema> Schemas = new()
    {
        // XWingTours (todStages.json), XWingHistoricalMissions, XWingGameData, KalidorAward, FlightBadge.
        ["xwing"] = new GameSchema(
            Tours: new[]
            {
                ("tour1", "Tour 1", 12), ("tour2", "Tour 2", 12), ("tour3", "Tour 3", 14),
                ("tour4", "Tour 4 (Imperial Pursuit)", 20), ("tour5", "Tour 5 (B-Wing)", 20),
            },
            Historic: new[]
            {
                ("xcombat", "X-wing", 6), ("ycombat", "Y-wing", 6), ("acombat", "A-wing", 6), ("bcombat", "B-wing", 6), ("bonus", "Bonus", 6),
            },
            TrainingCraft: new[] { ("XWing", "X-wing"), ("YWing", "Y-wing"), ("AWing", "A-wing"), ("BWing", "B-wing") },
            FlightBadgeLevel: 8,
            Award: "kalidor",
            AwardLevels: new[] { "Kalidor Crescent", "Bronze Cluster", "Silver Talons", "Silver Scimitar", "Golden Wings", "Diamond Eyes" },
            Craft: Conversion.XwvmBridge.XWingCraft.Select((id, i) => (id, XWingPilotFormat.KillTypes[i]))
                .Append(("BWing", "B-wing")).ToArray(),
            Stats: CommonStats,
            ObjectivePins: false),

        // TieTours (todStagesTie.json), TieHistoricalMissions, TieGameData, SecretOrderAward, FlightBadge.
        ["tf"] = new GameSchema(
            Tours: TiePilotFormat.BattleMissions.Select((n, i) => ($"battle{i + 1}", $"Battle {i + 1}", n)).ToArray(),
            Historic: TiePilotFormat.TrainingShips.Select((name, i) => ($"ship{i + 1}", name, 8)).ToArray(),
            TrainingCraft: new[] { "TIEFighter", "TIEInterceptor", "TIEBomber", "TIEAdvanced", "Gunboat", "TIEDefender", "MissileBoat" }
                .Select((id, i) => (id, TiePilotFormat.TrainingShips[i])).ToArray(),
            FlightBadgeLevel: 6,
            Award: "secretOrderRank",
            AwardLevels: Enumerable.Range(1, 9).Select(n => $"Secret Order rank {n}").ToArray(),
            Craft: Conversion.XwvmBridge.TieSpecies.Select((id, i) => (id, TiePilotFormat.Species[i]))
                .Where(c => c.id is not null).Select(c => (c.id!, c.Item2)).ToArray(),
            Stats: CommonStats.Concat(new[] { ("SecretOrderVictories", "Secret Order victories"), ("SecretOrderPoints", "Secret Order points") }).ToArray(),
            ObjectivePins: true),
    };

    private readonly XwvmPilotFormat _format;
    private readonly XDocument _xml;
    private readonly bool _hadBom;
    private readonly List<(XmlSlot Game, string GameId, EditableField Field)> _activeTourFields = new();

    public XwvmPilotDocument(XwvmPilotFormat format, byte[] data)
    {
        _format = format;
        _hadBom = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF;
        using (var stream = new MemoryStream(data))
        using (var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit }))
        {
            _xml = XDocument.Load(reader, LoadOptions.None);
        }

        var root = _xml.Root ?? throw new InvalidDataException("The file has no root element.");
        Build(root);
        Notes.Add("XWVM pilots are XML; unknown elements and attributes are kept as they are.");
        Notes.Add("Everything XWVM can read is listed, also what this pilot does not have yet. A record is added to the file only when you change one of its values.");
        Notes.Add("Close XWVM before saving: it rewrites every pilot file when it exits.");

        // Each operation, mission and proving-ground craft keeps several values: one sub-tree each.
        BeginLayout().ShowRestSplitting(f =>
            f.Group.Contains("/Tours of Duty/", StringComparison.Ordinal) ||
            f.Group.Contains("/Historical missions/", StringComparison.Ordinal) ||
            f.Group.EndsWith("/Proving grounds", StringComparison.Ordinal));
    }

    public override string GameName => _format.Game;

    public override string FormatName => _format.Name;

    public override string PilotName => Field("name").GetText();

    // ---- building -------------------------------------------------------------------------------------------------

    private void Build(XElement root)
    {
        AddField(new XmlTextField(new XmlSlot(root), "Name", "name", "Pilot", "Name", 32)
        {
            Help = "XWVM identifies pilots by this name. Two pilot files with the same name cannot both be loaded.",
        });

        var gameIds = AllGames.Concat(root.Elements("PilotGameRecord").Select(e => (string?)e.Attribute("Id") ?? "?")).Distinct().ToList();
        foreach (var id in gameIds)
        {
            var gameId = id;
            var game = new XmlSlot(
                () => root.Elements("PilotGameRecord").FirstOrDefault(e => (string?)e.Attribute("Id") == gameId),
                () => CreateGame(root, gameId));
            BuildGame(game, gameId);
        }
    }

    private static XElement CreateGame(XElement root, string id)
    {
        var element = NewGame(id);
        var order = Array.IndexOf(AllGames, id);
        var after = root.Elements("PilotGameRecord").LastOrDefault(e => Array.IndexOf(AllGames, (string?)e.Attribute("Id")) is var o && o >= 0 && o < order);
        if (after is not null)
        {
            after.AddAfterSelf(element);
        }
        else if (root.Elements("PilotGameRecord").FirstOrDefault() is { } first)
        {
            first.AddBeforeSelf(element);
        }
        else
        {
            root.Add(element);
        }

        return element;
    }

    /// <summary>A game record as XWVM writes a new one.</summary>
    internal static XElement NewGame(string id) => new("PilotGameRecord",
        new XAttribute("Id", id), new XAttribute("Rank", "FltCadet"), new XAttribute("Status", "Alive"),
        new XAttribute("Difficulty", "Medium"), new XAttribute("CurrentScore", "0"), new XAttribute("AiScore", "0"),
        new XAttribute("AvatarIndex", "-1"));

    private void BuildGame(XmlSlot game, string gameId)
    {
        var g = GameNames.TryGetValue(gameId, out var n) ? n : gameId;
        var p = $"game[{gameId}]";
        Schemas.TryGetValue(gameId, out var schema);
        var existing = game.Element;

        AddField(new XmlEnumField(game, "Rank", Ranks, RankLabels, $"{p}.rank", g, "Rank"));
        AddField(new XmlEnumField(game, "Status", Statuses, Statuses, $"{p}.status", g, "Status")
        {
            Help = "Setting a captured or dead pilot back to Alive is what XWVM's revive does; XWVM also resets the current score and caps the rank at Flight Officer when it revives a pilot.",
        });
        AddField(new XmlEnumField(game, "Difficulty", Difficulties, Difficulties, $"{p}.difficulty", g, "Difficulty", defaultIndex: 1));
        AddField(new XmlIntField(game, "CurrentScore", $"{p}.score", g, "Current score")
        {
            Minimum = 0,
            Maximum = int.MaxValue,
            Help = "Tour of Duty score; promotions follow it.",
        });
        AddField(new XmlIntField(game, "AiScore", $"{p}.aiScore", g, "Best score (AI skill)")
        {
            Minimum = 0,
            Maximum = int.MaxValue,
            Help = "XWVM derives the AI skill of your craft from this value (best score / 4 / 65536). Historical missions raise it up to 144,000.",
        });
        AddField(new XmlTextField(game, "Droid", $"{p}.droid", g, "Droid name", 32, removeWhenEmpty: true));
        AddField(new XmlIntField(game, "AvatarIndex", $"{p}.avatar", g, "Avatar index", defaultValue: -1)
        {
            Minimum = -1,
            Maximum = 999,
            Help = "-1 = chosen automatically.",
        });
        AddField(new XmlTextField(game, "AvatarPath", $"{p}.avatarPath", g, "Custom avatar image", 260, removeWhenEmpty: true));

        // Career award stored by name (other medals, patches and pins follow from the records below).
        if (schema?.Award is { } awardName)
        {
            var award = Child(game, "PilotAwardRecord", "Award", awardName, () => new XElement("PilotAwardRecord",
                new XAttribute("Award", awardName), new XAttribute("Level", "0")));
            AddField(new XmlAwardField(award, $"{p}.award[{awardName}]", g, gameId == "xwing" ? "Kalidor Crescent" : "Emperor's Secret Order", schema.AwardLevels));
        }

        // Awards under other names are kept and shown as they are (XWVM itself only uses the one above).
        foreach (var other in existing?.Elements("PilotAwardRecord").Where(a => (string?)a.Attribute("Award") != schema?.Award).ToList() ?? new List<XElement>())
        {
            var name = (string?)other.Attribute("Award") ?? "?";
            AddField(new XmlIntField(new XmlSlot(other), "Level", $"{p}.award[{name}]", $"{g}/Other awards", name)
            {
                Minimum = 0,
                Maximum = 99,
                Help = schema?.Award is { } expected ? $"XWVM does not use an award named \"{name}\" for this game (it uses \"{expected}\")." : null,
            });
        }

        // Tours of Duty.
        var tourIds = (schema?.Tours.Select(t => t.Id) ?? Enumerable.Empty<string>())
            .Concat(existing?.Elements("PilotTourRecord").Select(t => (string?)t.Attribute("TourId") ?? "") ?? Enumerable.Empty<string>())
            .Where(t => t.Length > 0).Distinct().ToArray();
if (tourIds.Length > 0)
        {
                    var activeTour = new XmlActiveTourField(game, tourIds, tourIds.Select(t => schema?.Tours.FirstOrDefault(s => s.Id == t).Label ?? t).ToArray(),
                $"{p}.activeTour", $"{g}/Tours of Duty", "Active tour");
            AddField(activeTour);
            _activeTourFields.Add((game, gameId, activeTour));
        }

        foreach (var tourId in tourIds)
        {
            var known = schema?.Tours.FirstOrDefault(t => t.Id == tourId);
            var label = known?.Label ?? tourId;
            var tour = Child(game, "PilotTourRecord", "TourId", tourId, () => NewTour("PilotTourRecord", tourId));
            AddTour(tour, $"{p}.tour[{tourId}]", $"{g}/Tours of Duty/{label}", known?.Steps, isHistoric: false, pins: schema?.ObjectivePins ?? false);
        }

        // Historical missions (combat chamber).
        var historicIds = (schema?.Historic.Select(t => t.Id) ?? Enumerable.Empty<string>())
            .Concat(existing?.Elements("HistoricTourRecord").Select(t => (string?)t.Attribute("TourId") ?? "") ?? Enumerable.Empty<string>())
            .Where(t => t.Length > 0).Distinct().ToArray();
        foreach (var tourId in historicIds)
        {
            var known = schema?.Historic.FirstOrDefault(t => t.Id == tourId);
            var label = known?.Label ?? tourId;
            var tour = Child(game, "HistoricTourRecord", "TourId", tourId, () => NewTour("HistoricTourRecord", tourId));
            AddTour(tour, $"{p}.historic[{tourId}]", $"{g}/Historical missions/{label}", known?.Missions, isHistoric: true, pins: false);
        }

        // Proving grounds.
        var craftIds = (schema?.TrainingCraft.Select(c => c.Id) ?? Enumerable.Empty<string>())
            .Concat(existing?.Elements("TrainingRecord").Select(t => (string?)t.Attribute("CraftType") ?? "") ?? Enumerable.Empty<string>())
            .Where(t => t.Length > 0).Distinct();
        foreach (var craft in craftIds)
        {
            var craftId = craft;
            var label = schema?.TrainingCraft.FirstOrDefault(c => c.Id == craftId).Label ?? craftId;
            var training = Child(game, "TrainingRecord", "CraftType", craftId, () => new XElement("TrainingRecord",
                new XAttribute("CraftType", craftId), new XAttribute("HighScore", "0"), new XAttribute("HighestLevelCompleted", "-1")));
            var group = $"{g}/Proving grounds";
            AddField(new XmlIntField(training, "HighScore", $"{p}.training[{craftId}].score", group, $"{label}: high score") { Minimum = 0, Maximum = int.MaxValue });
            AddField(new XmlIntField(training, "HighestLevelCompleted", $"{p}.training[{craftId}].level", group, $"{label}: highest level completed", defaultValue: -1)
            {
                Minimum = -1,
                Maximum = 999,
                Help = schema is null
                    ? "-1 = none."
                    : $"-1 = none. XWVM awards the flight badge for this craft from level {schema.FlightBadgeLevel}, and promotes a cadet to Flight Officer at level 5.",
            });
        }

        // Kills and captures.
        var victories = (schema?.Craft ?? Array.Empty<(string, string)>())
            .Concat(existing?.Elements("EntityVictoryRecord").Select(v => ((string?)v.Attribute("Id") ?? "", (string?)v.Attribute("Id") ?? "")) ?? Enumerable.Empty<(string, string)>())
            .Where(v => v.Item1.Length > 0).DistinctBy(v => v.Item1).ToList();
        foreach (var (entityId, label) in victories)
        {
            var id = entityId;
            var victory = Child(game, "EntityVictoryRecord", "Id", id, () => new XElement("EntityVictoryRecord",
                new XAttribute("Id", id), new XAttribute("Kills", "0"), new XAttribute("Captures", "0")));
            AddField(new XmlIntField(victory, "Kills", $"{p}.victory[{id}].kills", $"{g}/Statistics/Kills by craft", label) { Minimum = 0, Maximum = int.MaxValue });
        }

        foreach (var (entityId, label) in victories)
        {
            var id = entityId;
            var victory = Child(game, "EntityVictoryRecord", "Id", id, () => new XElement("EntityVictoryRecord",
                new XAttribute("Id", id), new XAttribute("Kills", "0"), new XAttribute("Captures", "0")));
            AddField(new XmlIntField(victory, "Captures", $"{p}.victory[{id}].captures", $"{g}/Statistics/Captures by craft", label) { Minimum = 0, Maximum = int.MaxValue });
        }

        // Statistics.
        var stats = (schema?.Stats ?? Array.Empty<(string, string)>())
            .Concat(existing?.Elements("StatRecord").Select(s => ((string?)s.Attribute("Id") ?? "", (string?)s.Attribute("Id") ?? "")) ?? Enumerable.Empty<(string, string)>())
            .Where(s => s.Item1.Length > 0).DistinctBy(s => s.Item1);
        foreach (var (statId, label) in stats)
        {
            var id = statId;
            var stat = Child(game, "StatRecord", "Id", id, () => new XElement("StatRecord", new XAttribute("Id", id), new XAttribute("Value", "0")));
            AddField(new XmlIntField(stat, "Value", $"{p}.stat[{id}]", $"{g}/Statistics", label) { Minimum = 0, Maximum = int.MaxValue });
        }

        // Craft loadout preferences (names come from the craft data, so only existing ones can be edited).
        if (existing?.Element("CraftConfigurationPreferences") is { } preferences)
        {
            foreach (var preference in preferences.Elements("CraftConfigurationPreference"))
            {
                var craft = (string?)preference.Attribute("CraftType") ?? "?";
                AddField(new XmlTextField(new XmlSlot(preference), "HardpointConfiguration", $"{p}.loadout[{craft}]", $"{g}/Craft loadouts", craft, 64));
            }
        }
    }

    private static XElement NewTour(string element, string tourId) =>
        new(element, new XAttribute("TourId", tourId), new XAttribute("CurrentTourStep", "0"));

    private void AddTour(XmlSlot tour, string p, string g, int? steps, bool isHistoric, bool pins)
    {
        AddField(new XmlExistsField(tour, $"{p}.started", g, isHistoric ? "Has a record" : "Started")
        {
            Help = isHistoric
                ? "Whether XWVM has a record for this group. Removing it deletes its mission scores."
                : "Whether the pilot has started this tour. Unticking removes the tour record and its scores.",
        });

        if (!isHistoric)
        {
            AddField(new XmlTourCompletedField(tour, $"{p}.completed", g, "Completed"));
            AddField(new XmlIntField(tour, "CurrentTourStep", $"{p}.step", g, "Current operation")
            {
                Minimum = 0,
                Maximum = steps is { } count ? count - 1 : CompletedStep,
                Help = steps is { } c
                    ? $"Zero-based: 0 is the first operation, {c - 1} the last. Use \"Completed\" for a finished tour."
                    : "Zero-based operation the pilot is on. 2147483647 means completed.",
            });
        }

        var existing = tour.Element?.Elements("PilotOperationRecord").Select(o => (string?)o.Attribute("TourStep") ?? "").ToList() ?? new List<string>();
        var stepIds = Enumerable.Range(0, steps ?? 0).Select(i => i.ToString(CultureInfo.InvariantCulture))
            .Concat(existing).Where(s => s.Length > 0).Distinct().ToList();

        foreach (var step in stepIds)
        {
            var stepId = step;
            var op = Child(tour, "PilotOperationRecord", "TourStep", stepId, () => new XElement("PilotOperationRecord",
                new XAttribute("TourStep", stepId), new XAttribute("Score", "0")));
            var o = $"{p}.op[{stepId}]";
            var label = int.TryParse(stepId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
                ? $"{(isHistoric ? "Mission" : "Operation")} {s + 1}"
                : $"Operation {stepId}";
            var name = (string?)op.Element?.Attribute("Name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                label += $" ({name})";
            }

            AddField(new XmlIntField(op, "Score", $"{o}.score", g, $"{label}: score") { Minimum = int.MinValue, Maximum = int.MaxValue });
            AddField(new XmlBoolField(op, "Complete", $"{o}.complete", g, $"{label}: completed")
            {
                Help = isHistoric ? "A completed historical mission earns its patch (coin in TIE Fighter)." : null,
            });
            if (!isHistoric)
            {
                AddField(new XmlIntField(op, "Attempts", $"{o}.attempts", g, $"{label}: attempts", removeWhenAbsentAndDefault: true)
                {
                    Minimum = 0,
                    Maximum = int.MaxValue,
                    Help = "After 5 attempts XWVM considers the pilot stuck on this operation.",
                });
            }

            if (pins || op.Element?.Attribute("SecondaryGoals") is not null || op.Element?.Attribute("BonusGoals") is not null)
            {
                AddField(new XmlBoolField(op, "SecondaryGoals", $"{o}.secondary", g, $"{label}: secondary goals") { Help = "Earns the secondary objectives pin." });
                AddField(new XmlBoolField(op, "BonusGoals", $"{o}.bonus", g, $"{label}: bonus goals") { Help = "Earns the bonus objectives pin." });
            }
        }
    }

    /// <summary>A child element of <paramref name="parent"/> identified by an attribute; created (in XWVM's order) when first written.</summary>
    private static XmlSlot Child(XmlSlot parent, string element, string keyAttribute, string key, Func<XElement> create) =>
        new(() => parent.Element?.Elements(element).FirstOrDefault(e => (string?)e.Attribute(keyAttribute) == key),
            () =>
            {
                var owner = parent.Ensure();
                var created = create();
                var order = Array.IndexOf(ChildOrder, element);
                if (order < 0)
                {
                    owner.Add(created);
                    return created;
                }

                var before = owner.Elements().FirstOrDefault(e => Array.IndexOf(ChildOrder, e.Name.LocalName) > order);
                var sameKind = owner.Elements(element).LastOrDefault();
                if (sameKind is not null)
                {
                    sameKind.AddAfterSelf(created);
                }
                else if (before is not null)
                {
                    before.AddBeforeSelf(created);
                }
                else
                {
                    owner.Add(created);
                }

                return created;
            });

    // ---- rules ----------------------------------------------------------------------------------------------------

    protected override IEnumerable<ValidationIssue> CheckRules()
    {
        if (string.IsNullOrWhiteSpace(PilotName))
        {
            yield return CommonRules.Broken("The pilot needs a name.", Field("name"));
        }

        var root = _xml.Root!;
        foreach (var gameId in AllGames)
        {
            if (!root.Elements("PilotGameRecord").Any(e => (string?)e.Attribute("Id") == gameId))
            {
                yield return CommonRules.Odd(
                    $"The pilot has no {GameNames[gameId]} record. XWVM expects one for every game and may fail when that game is played; changing any {GameNames[gameId]} value adds it.",
                    Field($"game[{gameId}].rank"));
            }
        }

        foreach (var (game, gameId, field) in _activeTourFields)
        {
            var element = game.Element;
            var active = (string?)element?.Attribute("ActiveTour");
            if (element is null || active is null)
            {
                continue;
            }

            var tour = element.Elements("PilotTourRecord").FirstOrDefault(t => (string?)t.Attribute("TourId") == active);
            if (tour is null)
            {
                // XWVM throws while loading such a pilot.
                yield return CommonRules.Broken($"The active tour \"{active}\" is not started; XWVM would fail to load this pilot.", field);
            }
            else if ((string?)tour.Attribute("CurrentTourStep") == CompletedStep.ToString(CultureInfo.InvariantCulture))
            {
                yield return CommonRules.Odd($"The active tour \"{active}\" is already completed; XWVM will clear it.", field);
            }

            foreach (var t in element.Elements("PilotTourRecord"))
            {
                var id = (string?)t.Attribute("TourId");
                if (Schemas.TryGetValue(gameId, out var schema) && schema.Tours.FirstOrDefault(s => s.Id == id) is { Id: not null } known &&
                    int.TryParse((string?)t.Attribute("CurrentTourStep"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var step) &&
                    step != CompletedStep && step >= known.Steps)
                {
                    yield return CommonRules.Broken(
                        $"{known.Label} has {known.Steps} operations; its current operation is beyond the last one, which XWVM cannot load.",
                        Field($"game[{gameId}].tour[{id}].step"));
                }
            }
        }
    }

    protected override byte[] Serialize()
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            Encoding = new UTF8Encoding(_hadBom),
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            _xml.Save(writer);
        }

        return stream.ToArray();
    }
}

/// <summary>An element that may not exist yet: read from it if it does, create it when a value is written.</summary>
internal sealed class XmlSlot
{
    private readonly Func<XElement?> _find;
    private readonly Func<XElement>? _create;

    public XmlSlot(XElement element)
    {
        _find = () => element;
    }

    public XmlSlot(Func<XElement?> find, Func<XElement> create)
    {
        _find = find;
        _create = create;
    }

    public XElement? Element => _find();

    public XElement Ensure() => _find() ?? _create?.Invoke() ?? throw new InvalidOperationException("The element cannot be created.");

    public void Remove() => _find()?.Remove();
}

/// <summary>Integer attribute.</summary>
internal sealed class XmlIntField : EditableField
{
    private readonly XmlSlot _slot;
    private readonly string _attribute;
    private readonly long _default;

    public XmlIntField(XmlSlot slot, string attribute, string id, string group, string label, long defaultValue = 0, bool removeWhenAbsentAndDefault = false)
        : base(id, group, label)
    {
        _slot = slot;
        _attribute = attribute;
        _default = defaultValue;
        _ = removeWhenAbsentAndDefault;
    }

    public override long GetInteger() =>
        long.TryParse((string?)_slot.Element?.Attribute(_attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : _default;

    protected override void WriteInteger(long value) =>
        _slot.Ensure().SetAttributeValue(_attribute, value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Optional boolean attribute ("true"/"false").</summary>
internal sealed class XmlBoolField : EditableField
{
    private readonly XmlSlot _slot;
    private readonly string _attribute;

    public XmlBoolField(XmlSlot slot, string attribute, string id, string group, string label)
        : base(id, group, label)
    {
        _slot = slot;
        _attribute = attribute;
        Editor = FieldEditor.Flag;
        Minimum = 0;
        Maximum = 1;
    }

    public override long GetInteger() =>
        bool.TryParse((string?)_slot.Element?.Attribute(_attribute), out var v) && v ? 1 : 0;

    protected override void WriteInteger(long value) =>
        _slot.Ensure().SetAttributeValue(_attribute, value != 0 ? "true" : "false");
}

/// <summary>Enum attribute stored by name.</summary>
internal sealed class XmlEnumField : EditableField
{
    private readonly XmlSlot _slot;
    private readonly string _attribute;
    private readonly string[] _names;
    private readonly int _defaultIndex;

    public XmlEnumField(XmlSlot slot, string attribute, string[] names, string[] labels, string id, string group, string label, int defaultIndex = 0)
        : base(id, group, label)
    {
        _slot = slot;
        _attribute = attribute;
        _names = names;
        _defaultIndex = defaultIndex;
        Editor = FieldEditor.Choice;
        Choices = labels.Select((l, i) => new ChoiceOption(i, l)).ToArray();
    }

    public override long GetInteger()
    {
        var raw = (string?)_slot.Element?.Attribute(_attribute);
        if (raw is null)
        {
            return _defaultIndex;
        }

        var index = Array.IndexOf(_names, raw);
        if (index >= 0)
        {
            return index;
        }

        // .NET's Enum.TryParse, which XWVM uses, also accepts numbers.
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : _defaultIndex;
    }

    protected override void WriteInteger(long value) => _slot.Ensure().SetAttributeValue(_attribute, _names[value]);
}

/// <summary>Text attribute.</summary>
internal sealed class XmlTextField : EditableField
{
    private readonly XmlSlot _slot;
    private readonly string _attribute;
    private readonly bool _removeWhenEmpty;

    public XmlTextField(XmlSlot slot, string attribute, string id, string group, string label, int maxLength, bool removeWhenEmpty = false)
        : base(id, group, label)
    {
        _slot = slot;
        _attribute = attribute;
        _removeWhenEmpty = removeWhenEmpty;
        Editor = FieldEditor.Text;
        MaxLength = maxLength;
    }

    public override long GetInteger() => 0;

    public override string GetText() => (string?)_slot.Element?.Attribute(_attribute) ?? string.Empty;

    protected override void WriteInteger(long value) => throw new NotSupportedException();

    protected override void WriteText(string value)
    {
        if (_removeWhenEmpty && value.Length == 0)
        {
            _slot.Element?.Attribute(_attribute)?.Remove();
        }
        else
        {
            _slot.Ensure().SetAttributeValue(_attribute, value);
        }
    }
}

/// <summary>Whether an element exists (a tour is started, a record is kept). Unticking removes it.</summary>
internal sealed class XmlExistsField : EditableField
{
    private readonly XmlSlot _slot;

    public XmlExistsField(XmlSlot slot, string id, string group, string label)
        : base(id, group, label)
    {
        _slot = slot;
        Editor = FieldEditor.Flag;
        Minimum = 0;
        Maximum = 1;
    }

    public override long GetInteger() => _slot.Element is null ? 0 : 1;

    protected override void WriteInteger(long value)
    {
        if (value != 0)
        {
            _slot.Ensure();
        }
        else
        {
            _slot.Remove();
        }
    }
}

/// <summary>A named award with levels (PilotAwardRecord); "not awarded" removes the record.</summary>
internal sealed class XmlAwardField : EditableField
{
    private readonly XmlSlot _slot;

    public XmlAwardField(XmlSlot slot, string id, string group, string label, string[] levels)
        : base(id, group, label)
    {
        _slot = slot;
        Editor = FieldEditor.Choice;
        Choices = new[] { new ChoiceOption(-1, "Not awarded") }.Concat(levels.Select((l, i) => new ChoiceOption(i, l))).ToArray();
    }

    public override long GetInteger() =>
        _slot.Element is null
            ? -1
            : long.TryParse((string?)_slot.Element.Attribute("Level"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    protected override void WriteInteger(long value)
    {
        if (value < 0)
        {
            _slot.Remove();
        }
        else
        {
            _slot.Ensure().SetAttributeValue("Level", value.ToString(CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>"Completed" view of a tour's CurrentTourStep.</summary>
internal sealed class XmlTourCompletedField : EditableField
{
    private readonly XmlSlot _tour;
    private string _stepBeforeCompletion = "0";

    public XmlTourCompletedField(XmlSlot tour, string id, string group, string label)
        : base(id, group, label)
    {
        _tour = tour;
        Editor = FieldEditor.Flag;
        Minimum = 0;
        Maximum = 1;
    }

    private string Step => (string?)_tour.Element?.Attribute("CurrentTourStep") ?? "0";

    public override long GetInteger() =>
        Step == XwvmPilotDocument.CompletedStep.ToString(CultureInfo.InvariantCulture) ? 1 : 0;

    protected override void WriteInteger(long value)
    {
        if (value != 0)
        {
            _stepBeforeCompletion = Step;
            _tour.Ensure().SetAttributeValue("CurrentTourStep", XwvmPilotDocument.CompletedStep.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            _tour.Ensure().SetAttributeValue("CurrentTourStep", _stepBeforeCompletion);
        }
    }
}

/// <summary>The game record's ActiveTour attribute.</summary>
internal sealed class XmlActiveTourField : EditableField
{
    private readonly XmlSlot _game;
    private readonly string[] _tourIds;

    public XmlActiveTourField(XmlSlot game, string[] tourIds, string[] labels, string id, string group, string label)
        : base(id, group, label)
    {
        _game = game;
        _tourIds = tourIds;
        Editor = FieldEditor.Choice;
        Choices = new[] { new ChoiceOption(0, "(none)") }
            .Concat(labels.Select((t, i) => new ChoiceOption(i + 1, t)))
            .ToArray();
        Help = "The tour must be started (it needs a tour record), or XWVM cannot load the pilot.";
    }

    public override long GetInteger()
    {
        var current = (string?)_game.Element?.Attribute("ActiveTour");
        if (current is null)
        {
            return 0;
        }

        var index = Array.IndexOf(_tourIds, current);
        return index >= 0 ? index + 1 : -1;
    }

    protected override void WriteInteger(long value)
    {
        if (value == 0)
        {
            _game.Element?.Attribute("ActiveTour")?.Remove();
        }
        else
        {
            _game.Ensure().SetAttributeValue("ActiveTour", _tourIds[value - 1]);
        }
    }
}
