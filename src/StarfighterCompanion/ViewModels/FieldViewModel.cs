using System.Globalization;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.ViewModels;

/// <summary>One editable row. All writes go through the core field, which refuses illegal values.</summary>
public sealed class FieldViewModel : ViewModelBase
{
    private string? _error;
    private string _integerText = string.Empty;

    public FieldViewModel(EditableField field, bool showGroup)
    {
        Field = field;
        ShowGroup = showGroup;
        if (field.Editor == FieldEditor.Choice)
        {
            ChoiceItems = field.Choices?.ToList() ?? new List<ChoiceOption>();
        }

        Refresh();
    }

    public EditableField Field { get; }

    public string Label => Field.Label;

    public string GroupPath => Field.Group.Replace("/", " › ");

    public bool ShowGroup { get; }

    public string? Help => Field.Help ?? DefaultHelp();

    public bool IsInteger => Field.Editor == FieldEditor.Integer;

    public bool IsChoice => Field.Editor == FieldEditor.Choice;

    public bool IsFlag => Field.Editor == FieldEditor.Flag;

    public bool IsText => Field.Editor == FieldEditor.Text;

    public bool IsEditable => !Field.IsReadOnly;

    public bool IsModified => Field.IsModified;

    public List<ChoiceOption> ChoiceItems { get; } = new();

    public string? Error
    {
        get => _error;
        private set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => !string.IsNullOrEmpty(_error);

    public string IntegerText
    {
        get => _integerText;
        set
        {
            if (!long.TryParse(value.Replace(",", string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                Error = $"\"{value}\" is not a whole number.";
                _integerText = value;
                Raise();
                return;
            }

            Apply(Field.TrySetInteger(number));
        }
    }

    public ChoiceOption? SelectedChoice
    {
        get
        {
            var v = Field.GetInteger();
            var found = ChoiceItems.FirstOrDefault(c => c.Value == v);
            if (found is null)
            {
                // A value from disk that is not a known choice is shown, but cannot be picked again.
                found = new ChoiceOption(v, $"Unknown value ({v})");
                ChoiceItems.Add(found);
            }

            return found;
        }

        set
        {
            if (value is not null)
            {
                Apply(Field.TrySetInteger(value.Value));
            }
        }
    }

    public bool FlagValue
    {
        get => Field.GetInteger() != 0;
        set => Apply(Field.TrySetInteger(value ? 1 : 0));
    }

    public string TextValue
    {
        get => Field.GetText();
        set => Apply(Field.TrySetText(value));
    }

    /// <summary>Re-reads the value (after an edit, or when a dependent field changed).</summary>
    public void Refresh()
    {
        _integerText = Field.Editor == FieldEditor.Integer ? Field.GetInteger().ToString(CultureInfo.InvariantCulture) : string.Empty;
        Raise(nameof(IntegerText));
        Raise(nameof(SelectedChoice));
        Raise(nameof(FlagValue));
        Raise(nameof(TextValue));
        Raise(nameof(IsModified));
    }

    private void Apply(string? error)
    {
        Error = error;
        Refresh();
    }

    private string? DefaultHelp()
    {
        return Field.Editor switch
        {
            FieldEditor.Integer => $"Allowed: {Field.Minimum.ToString("N0", CultureInfo.InvariantCulture)} to {Field.Maximum.ToString("N0", CultureInfo.InvariantCulture)}",
            FieldEditor.Text => $"Up to {Field.MaxLength} characters",
            _ => null,
        };
    }
}
