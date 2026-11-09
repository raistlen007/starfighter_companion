namespace StarfighterCompanion.Core.Model;

public enum IssueSeverity
{
    /// <summary>Something looks unusual but the game will cope with it. Saving is allowed.</summary>
    Warning,

    /// <summary>The game would misbehave or crash. Saving is blocked until it is fixed.</summary>
    Error,
}

public sealed record ValidationIssue(IssueSeverity Severity, string Message, EditableField? Field = null)
{
    public override string ToString() => $"{Severity}: {Message}";
}

/// <summary>An opened pilot file of any supported game.</summary>
public abstract class PilotDocument
{
    private readonly List<EditableField> _fields = new();
    private Dictionary<string, EditableField>? _byId;

    /// <summary>Name of the game (e.g. "TIE Fighter").</summary>
    public abstract string GameName { get; }

    /// <summary>Exact file variant (e.g. "TIE Fighter pilot (.tfr), CD-ROM / 1998").</summary>
    public abstract string FormatName { get; }

    /// <summary>The pilot's name as shown in the game.</summary>
    public abstract string PilotName { get; }

    /// <summary>Notes about the file that the user should know (shown under the title).</summary>
    public List<string> Notes { get; } = new();

    public IReadOnlyList<EditableField> Fields => _fields;

    private List<EditableField>? _display;

    /// <summary>
    /// The fields to show, in display order, with their place in the navigation tree. Formats that arrange their
    /// fields leave out slots that do not exist in the game and are empty; those bytes are still kept and saved.
    /// </summary>
    public IReadOnlyList<EditableField> DisplayFields => _display ?? _fields;

    /// <summary>
    /// Starts a display layout. Fields are shown in the order they are placed; fields never placed are hidden.
    /// Layout rules: progression of the whole career at the top level; progression between tours at the root
    /// of the tours; a tour's own progress at the root of that tour; missions with several values get their
    /// own sub-tree, missions with a single value (a score) are listed at the tour's root.
    /// </summary>
    protected DisplayLayout BeginLayout()
    {
        _display = new List<EditableField>();
        return new DisplayLayout(this, _display);
    }

    protected sealed class DisplayLayout
    {
        private readonly PilotDocument _document;
        private readonly List<EditableField> _display;
        private readonly HashSet<EditableField> _placed = new();

        internal DisplayLayout(PilotDocument document, List<EditableField> display)
        {
            _document = document;
            _display = display;
        }

        /// <summary>Shows <paramref name="id"/> under <paramref name="group"/>, optionally with a new label.</summary>
        public DisplayLayout Show(string id, string group, string? label = null)
        {
            if (_document.TryGetField(id, out var field))
            {
                Show(field, group, label);
            }

            return this;
        }

        public DisplayLayout Show(EditableField field, string group, string? label = null)
        {
            field.Group = group;
            if (label is not null)
            {
                field.Label = label;
            }

            if (_placed.Add(field))
            {
                _display.Add(field);
            }

            return this;
        }

        /// <summary>Shows every field whose id starts with <paramref name="idPrefix"/> where it already is.</summary>
        public DisplayLayout ShowAsIs(string idPrefix)
        {
            foreach (var field in _document.Fields.Where(f => f.Id.StartsWith(idPrefix, StringComparison.Ordinal)))
            {
                Show(field, field.Group);
            }

            return this;
        }

        /// <summary>
        /// Shows fields labelled "Mission 3: best score" as "best score" inside a "Mission 3" sub-tree of their group.
        /// </summary>
        public DisplayLayout ShowSplitByLabel(string idPrefix, string? group = null)
        {
            foreach (var field in _document.Fields.Where(f => f.Id.StartsWith(idPrefix, StringComparison.Ordinal)).ToList())
            {
                var cut = field.Label.IndexOf(": ", StringComparison.Ordinal);
                var target = group ?? field.Group;
                if (cut > 0)
                {
                    var rest = field.Label[(cut + 2)..];
                    Show(field, $"{target}/{field.Label[..cut]}", char.ToUpperInvariant(rest[0]) + rest[1..]);
                }
                else
                {
                    Show(field, target);
                }
            }

            return this;
        }

        /// <summary>
        /// Shows every field not placed yet in file order; those matching <paramref name="split"/> are split by label
        /// ("Mission 3: best score" becomes "Best score" in a "Mission 3" sub-tree).
        /// </summary>
        public void ShowRestSplitting(Func<EditableField, bool> split)
        {
            foreach (var field in _document.Fields.ToList())
            {
                if (_placed.Contains(field))
                {
                    continue;
                }

                var cut = field.Label.IndexOf(": ", StringComparison.Ordinal);
                if (split(field) && cut > 0)
                {
                    var rest = field.Label[(cut + 2)..];
                    Show(field, $"{field.Group}/{field.Label[..cut]}", char.ToUpperInvariant(rest[0]) + rest[1..]);
                }
                else
                {
                    Show(field, field.Group);
                }
            }
        }

        /// <summary>True when none of the fields has a value other than zero (or than <paramref name="defaultValue"/>).</summary>
        public bool AllZero(IEnumerable<string> ids, long defaultValue = 0) =>
            ids.All(id => !_document.TryGetField(id, out var f) || f.GetInteger() == 0 || f.GetInteger() == defaultValue);

        /// <summary>Shows every field not placed yet, where it already is (so nothing is lost by accident).</summary>
        public void ShowRest(Func<EditableField, bool>? except = null)
        {
            foreach (var field in _document.Fields)
            {
                if (!_placed.Contains(field) && (except is null || !except(field)))
                {
                    Show(field, field.Group);
                }
            }
        }
    }

    /// <summary>
    /// The retail edition the pilot is used with, when known (from the library version it was opened from).
    /// Rules use it for things that only exist in some editions, such as add-on tours and battles.
    /// </summary>
    public Library.Edition? Edition { get; set; }

    /// <summary>The library version the pilot was opened from, when known.</summary>
    public string? VariantId { get; set; }

    public bool IsDirty { get; private set; }

    public event EventHandler<EditableField>? FieldChanged;

    public EditableField Field(string id)
    {
        _byId ??= _fields.ToDictionary(f => f.Id, StringComparer.Ordinal);
        return _byId.TryGetValue(id, out var field)
            ? field
            : throw new KeyNotFoundException($"No field with id '{id}'.");
    }

    public bool TryGetField(string id, out EditableField field)
    {
        _byId ??= _fields.ToDictionary(f => f.Id, StringComparer.Ordinal);
        return _byId.TryGetValue(id, out field!);
    }

    public long Int(string id) => Field(id).GetInteger();

    protected void AddField(EditableField field)
    {
        field.Owner = this;
        _fields.Add(field);
        _byId = null;
    }

    protected void AddFields(IEnumerable<EditableField> fields)
    {
        foreach (var field in fields)
        {
            AddField(field);
        }
    }

    /// <summary>Checks every value and every rule that ties values together.</summary>
    public IReadOnlyList<ValidationIssue> Validate()
    {
        var issues = new List<ValidationIssue>();
        foreach (var field in _fields)
        {
            var error = field.CheckCurrentValue();
            if (error is not null)
            {
                // A value that came from disk this way is reported, but only blocks saving if the user touched it.
                issues.Add(new ValidationIssue(
                    field.IsModified ? IssueSeverity.Error : IssueSeverity.Warning,
                    field.IsModified ? error : $"{error} (value was already in the file)",
                    field));
            }
        }

        issues.AddRange(CheckRules());
        return issues;
    }

    public bool CanSave(out IReadOnlyList<ValidationIssue> issues)
    {
        issues = Validate();
        return issues.All(i => i.Severity != IssueSeverity.Error);
    }

    /// <summary>Produces the bytes to write to disk. Throws if the document has errors.</summary>
    public byte[] Save()
    {
        if (!CanSave(out var issues))
        {
            throw new InvalidOperationException(
                "The pilot cannot be saved: " + string.Join(" ", issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.Message)));
        }

        var bytes = Serialize();
        IsDirty = false;
        return bytes;
    }

    protected abstract byte[] Serialize();

    /// <summary>Rules that involve more than one field. Override per game.</summary>
    protected virtual IEnumerable<ValidationIssue> CheckRules() => Array.Empty<ValidationIssue>();

    /// <summary>Lets a document update dependent values after an edit (e.g. a rating name after the rating).</summary>
    protected virtual void AfterFieldChanged(EditableField field)
    {
    }

    internal void OnFieldChanged(EditableField field)
    {
        IsDirty = true;
        AfterFieldChanged(field);
        FieldChanged?.Invoke(this, field);
    }
}
