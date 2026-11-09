using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
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

    internal static readonly string[] Ranks = { "FltCadet", "FltOfficer", "Lieutenant", "Captain", "Commander", "General" };
    internal static readonly string[] RankLabels = { "Flight Cadet", "Flight Officer", "Lieutenant", "Captain", "Commander", "General" };
    internal static readonly string[] Statuses = { "Alive", "Captured", "Dead" };
    internal static readonly string[] Difficulties = { "Easy", "Medium", "Hard" };

    private readonly XwvmPilotFormat _format;
    private readonly XDocument _xml;
    private readonly bool _hadBom;
    private readonly List<(XElement Game, XmlActiveTourField Field)> _activeTourFields = new();

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
        Notes.Add("Close XWVM before saving: it rewrites every pilot file when it exits.");

        // Each operation of a tour and each proving-ground craft keeps several values: one sub-tree each.
        BeginLayout().ShowRestSplitting(f =>
            f.Group.Contains("/Tours of Duty/", StringComparison.Ordinal) ||
            f.Group.Contains("/Historical missions/", StringComparison.Ordinal) ||
            f.Group.EndsWith("/Proving grounds", StringComparison.Ordinal));
    }

    public override string GameName => _format.Game;

    public override string FormatName => _format.Name;

    public override string PilotName => Field("name").GetText();

    private void Build(XElement root)
    {
        AddField(new XmlTextField(root, "Name", "name", "Pilot", "Name", 32)
        {
            Help = "XWVM identifies pilots by this name. Two pilot files with the same name cannot both be loaded.",
        });

        foreach (var game in root.Elements("PilotGameRecord"))
        {
            var id = (string?)game.Attribute("Id") ?? "?";
            var g = GameNames.TryGetValue(id, out var n) ? n : id;
            var p = $"game[{id}]";

            AddField(new XmlEnumField(game, "Rank", Ranks, RankLabels, $"{p}.rank", $"{g}/Pilot", "Rank"));
            AddField(new XmlEnumField(game, "Status", Statuses, Statuses, $"{p}.status", $"{g}/Pilot", "Status")
            {
                Help = "Setting a captured or dead pilot back to Alive is what XWVM's revive does; XWVM also resets the current score and caps the rank at Flight Officer when it revives a pilot.",
            });
            AddField(new XmlEnumField(game, "Difficulty", Difficulties, Difficulties, $"{p}.difficulty", $"{g}/Pilot", "Difficulty", defaultIndex: 1));
            AddField(new XmlIntField(game, "CurrentScore", $"{p}.score", $"{g}/Pilot", "Current score") { Minimum = 0, Maximum = int.MaxValue });
            AddField(new XmlIntField(game, "AiScore", $"{p}.aiScore", $"{g}/Pilot", "Best score (AI skill)")
            {
                Minimum = 0,
                Maximum = int.MaxValue,
                Help = "XWVM derives the AI skill of your craft from this value.",
            });
            AddField(new XmlIntField(game, "AvatarIndex", $"{p}.avatar", $"{g}/Pilot", "Avatar index", defaultValue: -1)
            {
                Minimum = -1,
                Maximum = 999,
                Help = "-1 = chosen automatically.",
            });

            var tours = game.Elements("PilotTourRecord").ToList();
            var activeTour = new XmlActiveTourField(game, tours, $"{p}.activeTour", $"{g}/Tours of Duty", "Active tour");
            AddField(activeTour);
            _activeTourFields.Add((game, activeTour));

            foreach (var tour in tours)
            {
                AddTour(tour, p, $"{g}/Tours of Duty", "tour");
            }

            foreach (var tour in game.Elements("HistoricTourRecord"))
            {
                AddTour(tour, p, $"{g}/Historical missions", "historic");
            }

            foreach (var award in game.Elements("PilotAwardRecord"))
            {
                var name = (string?)award.Attribute("Award") ?? "?";
                AddField(new XmlIntField(award, "Level", $"{p}.award[{name}]", $"{g}/Awards", name) { Minimum = 0, Maximum = 99 });
            }

            foreach (var training in game.Elements("TrainingRecord"))
            {
                var craft = (string?)training.Attribute("CraftType") ?? "?";
                var group = $"{g}/Proving grounds";
                AddField(new XmlIntField(training, "HighScore", $"{p}.training[{craft}].score", group, $"{craft}: high score") { Minimum = 0, Maximum = int.MaxValue });
                AddField(new XmlIntField(training, "HighestLevelCompleted", $"{p}.training[{craft}].level", group, $"{craft}: highest level completed", defaultValue: -1)
                {
                    Minimum = -1,
                    Maximum = 999,
                    Help = "-1 = none.",
                });
            }

            foreach (var victory in game.Elements("EntityVictoryRecord"))
            {
                var entity = (string?)victory.Attribute("Id") ?? "?";
                AddField(new XmlIntField(victory, "Kills", $"{p}.victory[{entity}].kills", $"{g}/Kills and captures", $"{entity}: kills") { Minimum = 0, Maximum = int.MaxValue });
                AddField(new XmlIntField(victory, "Captures", $"{p}.victory[{entity}].captures", $"{g}/Kills and captures", $"{entity}: captures") { Minimum = 0, Maximum = int.MaxValue });
            }

            foreach (var stat in game.Elements("StatRecord"))
            {
                var statId = (string?)stat.Attribute("Id") ?? "?";
                AddField(new XmlIntField(stat, "Value", $"{p}.stat[{statId}]", $"{g}/Statistics", statId) { Minimum = 0, Maximum = int.MaxValue });
            }
        }
    }

    private void AddTour(XElement tour, string gamePrefix, string group, string kind)
    {
        var tourId = (string?)tour.Attribute("TourId") ?? "?";
        var p = $"{gamePrefix}.{kind}[{tourId}]";
        var g = $"{group}/{tourId}";

        AddField(new XmlTourCompletedField(tour, $"{p}.completed", g, "Tour completed"));
        AddField(new XmlIntField(tour, "CurrentTourStep", $"{p}.step", g, "Current step")
        {
            Minimum = 0,
            Maximum = CompletedStep,
            Help = "Zero-based operation the pilot is on. 2147483647 means the tour is completed.",
        });

        foreach (var op in tour.Elements("PilotOperationRecord"))
        {
            var step = (string?)op.Attribute("TourStep") ?? "?";
            var o = $"{p}.op[{step}]";
            var label = int.TryParse(step, NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? $"Operation {s + 1}" : $"Operation {step}";
            var name = (string?)op.Attribute("Name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                label += $" ({name})";
            }

            AddField(new XmlIntField(op, "Score", $"{o}.score", g, $"{label}: score") { Minimum = int.MinValue, Maximum = int.MaxValue });
            AddField(new XmlBoolField(op, "Complete", $"{o}.complete", g, $"{label}: completed"));
            AddField(new XmlIntField(op, "Attempts", $"{o}.attempts", g, $"{label}: attempts", removeWhenAbsentAndDefault: true) { Minimum = 0, Maximum = int.MaxValue });
            AddField(new XmlBoolField(op, "SecondaryGoals", $"{o}.secondary", g, $"{label}: secondary goals"));
            AddField(new XmlBoolField(op, "BonusGoals", $"{o}.bonus", g, $"{label}: bonus goals"));
        }
    }

    protected override IEnumerable<ValidationIssue> CheckRules()
    {
        if (string.IsNullOrWhiteSpace(PilotName))
        {
            yield return CommonRules.Broken("The pilot needs a name.", Field("name"));
        }

        foreach (var (game, field) in _activeTourFields)
        {
            var active = (string?)game.Attribute("ActiveTour");
            if (active is null)
            {
                continue;
            }

            var tour = game.Elements("PilotTourRecord").FirstOrDefault(t => (string?)t.Attribute("TourId") == active);
            if (tour is null)
            {
                // XWVM throws while loading such a pilot.
                yield return CommonRules.Broken($"The active tour \"{active}\" has no tour record; XWVM would fail to load this pilot.", field);
            }
            else if ((string?)tour.Attribute("CurrentTourStep") == CompletedStep.ToString(CultureInfo.InvariantCulture))
            {
                yield return CommonRules.Odd($"The active tour \"{active}\" is already completed; XWVM will clear it.", field);
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

/// <summary>Integer attribute.</summary>
internal sealed class XmlIntField : EditableField
{
    private readonly XElement _element;
    private readonly string _attribute;
    private readonly long _default;
    private readonly bool _removeWhenAbsentAndDefault;

    public XmlIntField(XElement element, string attribute, string id, string group, string label, long defaultValue = 0, bool removeWhenAbsentAndDefault = false)
        : base(id, group, label)
    {
        _element = element;
        _attribute = attribute;
        _default = defaultValue;
        _removeWhenAbsentAndDefault = removeWhenAbsentAndDefault;
    }

    public override long GetInteger() =>
        long.TryParse((string?)_element.Attribute(_attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : _default;

    protected override void WriteInteger(long value) =>
        _element.SetAttributeValue(_attribute, value.ToString(CultureInfo.InvariantCulture));
}

/// <summary>Optional boolean attribute ("true"/"false").</summary>
internal sealed class XmlBoolField : EditableField
{
    private readonly XElement _element;
    private readonly string _attribute;

    public XmlBoolField(XElement element, string attribute, string id, string group, string label)
        : base(id, group, label)
    {
        _element = element;
        _attribute = attribute;
        Editor = FieldEditor.Flag;
        Minimum = 0;
        Maximum = 1;
    }

    public override long GetInteger() =>
        bool.TryParse((string?)_element.Attribute(_attribute), out var v) && v ? 1 : 0;

    protected override void WriteInteger(long value) =>
        _element.SetAttributeValue(_attribute, value != 0 ? "true" : "false");
}

/// <summary>Enum attribute stored by name.</summary>
internal sealed class XmlEnumField : EditableField
{
    private readonly XElement _element;
    private readonly string _attribute;
    private readonly string[] _names;
    private readonly int _defaultIndex;

    public XmlEnumField(XElement element, string attribute, string[] names, string[] labels, string id, string group, string label, int defaultIndex = 0)
        : base(id, group, label)
    {
        _element = element;
        _attribute = attribute;
        _names = names;
        _defaultIndex = defaultIndex;
        Editor = FieldEditor.Choice;
        Choices = labels.Select((l, i) => new ChoiceOption(i, l)).ToArray();
    }

    public override long GetInteger()
    {
        var raw = (string?)_element.Attribute(_attribute);
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

    protected override void WriteInteger(long value) => _element.SetAttributeValue(_attribute, _names[value]);
}

/// <summary>Text attribute.</summary>
internal sealed class XmlTextField : EditableField
{
    private readonly XElement _element;
    private readonly string _attribute;

    public XmlTextField(XElement element, string attribute, string id, string group, string label, int maxLength)
        : base(id, group, label)
    {
        _element = element;
        _attribute = attribute;
        Editor = FieldEditor.Text;
        MaxLength = maxLength;
    }

    public override long GetInteger() => 0;

    public override string GetText() => (string?)_element.Attribute(_attribute) ?? string.Empty;

    protected override void WriteInteger(long value) => throw new NotSupportedException();

    protected override void WriteText(string value) => _element.SetAttributeValue(_attribute, value);
}

/// <summary>"Tour completed" view of a tour's CurrentTourStep.</summary>
internal sealed class XmlTourCompletedField : EditableField
{
    private readonly XElement _tour;
    private string _stepBeforeCompletion = "0";

    public XmlTourCompletedField(XElement tour, string id, string group, string label)
        : base(id, group, label)
    {
        _tour = tour;
        Editor = FieldEditor.Flag;
        Minimum = 0;
        Maximum = 1;
    }

    private string Step => (string?)_tour.Attribute("CurrentTourStep") ?? "0";

    public override long GetInteger() =>
        Step == XwvmPilotDocument.CompletedStep.ToString(CultureInfo.InvariantCulture) ? 1 : 0;

    protected override void WriteInteger(long value)
    {
        if (value != 0)
        {
            _stepBeforeCompletion = Step;
            _tour.SetAttributeValue("CurrentTourStep", XwvmPilotDocument.CompletedStep.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            _tour.SetAttributeValue("CurrentTourStep", _stepBeforeCompletion);
        }
    }
}

/// <summary>The game record's ActiveTour attribute, limited to tours the pilot has started.</summary>
internal sealed class XmlActiveTourField : EditableField
{
    private readonly XElement _game;
    private readonly string[] _tourIds;

    public XmlActiveTourField(XElement game, IReadOnlyList<XElement> tours, string id, string group, string label)
        : base(id, group, label)
    {
        _game = game;
        _tourIds = tours.Select(t => (string?)t.Attribute("TourId") ?? "").Where(t => t.Length > 0).ToArray();
        Editor = FieldEditor.Choice;
        Choices = new[] { new ChoiceOption(0, "(none)") }
            .Concat(_tourIds.Select((t, i) => new ChoiceOption(i + 1, t)))
            .ToArray();
        Help = "Only tours the pilot has started can be made active.";
    }

    public override long GetInteger()
    {
        var current = (string?)_game.Attribute("ActiveTour");
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
            _game.Attribute("ActiveTour")?.Remove();
        }
        else
        {
            _game.SetAttributeValue("ActiveTour", _tourIds[value - 1]);
        }
    }
}
