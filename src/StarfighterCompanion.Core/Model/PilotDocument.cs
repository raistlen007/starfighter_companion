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
