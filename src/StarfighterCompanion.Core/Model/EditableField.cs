using System.Globalization;

namespace StarfighterCompanion.Core.Model;

/// <summary>How a field should be presented and edited.</summary>
public enum FieldEditor
{
    /// <summary>A whole number constrained to <see cref="EditableField.Minimum"/>..<see cref="EditableField.Maximum"/>.</summary>
    Integer,

    /// <summary>One value out of <see cref="EditableField.Choices"/>.</summary>
    Choice,

    /// <summary>A yes/no value. Stored as 0 / 1 (any non-zero value reads as "yes").</summary>
    Flag,

    /// <summary>Free text limited to <see cref="EditableField.MaxLength"/> printable ASCII characters.</summary>
    Text,
}

/// <summary>One allowed value of a <see cref="FieldEditor.Choice"/> field.</summary>
public sealed record ChoiceOption(long Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// A single value inside a pilot file that the user may look at and change.
/// Every write goes through <see cref="TrySetInteger"/> / <see cref="TrySetText"/>, which refuse values
/// that are out of range, so the editor can never put an illegal value into the file.
/// </summary>
public abstract class EditableField
{
    protected EditableField(string id, string group, string label)
    {
        Id = id;
        Group = group;
        Label = label;
    }

    /// <summary>Stable identifier used by validation rules (e.g. <c>career.rank</c>).</summary>
    public string Id { get; }

    /// <summary>Slash separated group path used to build the navigation tree (e.g. <c>Career/Kills</c>).</summary>
    public string Group { get; internal set; }

    /// <summary>Human readable name.</summary>
    public string Label { get; internal set; }

    /// <summary>Optional longer explanation shown as a tooltip.</summary>
    public string? Help { get; init; }

    public FieldEditor Editor { get; init; } = FieldEditor.Integer;

    public long Minimum { get; init; } = long.MinValue;

    public long Maximum { get; init; } = long.MaxValue;

    public IReadOnlyList<ChoiceOption>? Choices { get; init; }

    public int MaxLength { get; init; }

    public bool IsReadOnly { get; init; }

    /// <summary>True once the user changed the value in this session.</summary>
    public bool IsModified { get; private set; }

    internal PilotDocument? Owner { get; set; }

    public abstract long GetInteger();

    public virtual string GetText() => GetInteger().ToString(CultureInfo.InvariantCulture);

    protected abstract void WriteInteger(long value);

    protected virtual void WriteText(string value) =>
        throw new NotSupportedException($"{Label} is not a text field.");

    /// <summary>A short display form of the current value.</summary>
    public string DisplayValue
    {
        get
        {
            switch (Editor)
            {
                case FieldEditor.Text:
                    return GetText();
                case FieldEditor.Flag:
                    return GetInteger() != 0 ? "Yes" : "No";
                case FieldEditor.Choice:
                    var v = GetInteger();
                    var choice = Choices?.FirstOrDefault(c => c.Value == v);
                    return choice?.Label ?? $"Unknown ({v})";
                default:
                    return GetInteger().ToString("N0", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Checks whether <paramref name="value"/> would be legal, without writing it.</summary>
    public string? CheckInteger(long value)
    {
        if (Editor == FieldEditor.Text)
        {
            return $"{Label} is a text field.";
        }

        if (Editor == FieldEditor.Flag)
        {
            return value is 0 or 1 ? null : $"{Label} must be Yes or No.";
        }

        if (Editor == FieldEditor.Choice)
        {
            return Choices is not null && Choices.Any(c => c.Value == value)
                ? null
                : $"{value} is not an allowed value for {Label}.";
        }

        if (value < Minimum || value > Maximum)
        {
            return $"{Label} must be between {Minimum:N0} and {Maximum:N0}.";
        }

        return null;
    }

    /// <summary>Checks whether <paramref name="value"/> would be legal text for this field.</summary>
    public string? CheckText(string value)
    {
        if (Editor != FieldEditor.Text)
        {
            return $"{Label} is not a text field.";
        }

        if (value.Length > MaxLength)
        {
            return $"{Label} can be at most {MaxLength} characters long.";
        }

        if (value.Any(ch => ch < 0x20 || ch > 0x7E))
        {
            return $"{Label} may only contain plain (ASCII) letters, digits and punctuation.";
        }

        return null;
    }

    /// <summary>Writes the value if it is legal. Returns an error message otherwise (and changes nothing).</summary>
    public string? TrySetInteger(long value)
    {
        if (IsReadOnly)
        {
            return $"{Label} cannot be changed.";
        }

        var error = CheckInteger(value);
        if (error is not null)
        {
            return error;
        }

        if (GetInteger() == value)
        {
            return null;
        }

        WriteInteger(value);
        MarkModified();
        return null;
    }

    /// <summary>Writes the text if it is legal. Returns an error message otherwise (and changes nothing).</summary>
    public string? TrySetText(string value)
    {
        if (IsReadOnly)
        {
            return $"{Label} cannot be changed.";
        }

        var error = CheckText(value);
        if (error is not null)
        {
            return error;
        }

        if (GetText() == value)
        {
            return null;
        }

        WriteText(value);
        MarkModified();
        return null;
    }

    /// <summary>Reports a problem with the value currently stored (for example a corrupted value from disk).</summary>
    public string? CheckCurrentValue() =>
        Editor == FieldEditor.Text ? CheckText(GetText()) : CheckInteger(Editor == FieldEditor.Flag ? (GetInteger() != 0 ? 1 : 0) : GetInteger());

    /// <summary>Used by documents that keep derived fields in step with an edited one.</summary>
    internal void ForceInteger(long value)
    {
        if (GetInteger() != value)
        {
            WriteInteger(value);
            IsModified = true;
        }
    }

    internal void ForceText(string value)
    {
        if (GetText() != value)
        {
            WriteText(value);
            IsModified = true;
        }
    }

    private void MarkModified()
    {
        IsModified = true;
        Owner?.OnFieldChanged(this);
    }

    public override string ToString() => $"{Group}/{Label} = {DisplayValue}";
}

/// <summary>One bit of another (byte) field shown as its own yes/no value, e.g. "secret objective of mission 3".</summary>
public sealed class BitFlagField : EditableField
{
    private readonly EditableField _source;
    private readonly int _bit;

    public BitFlagField(string id, string group, string label, EditableField source, int bit)
        : base(id, group, label)
    {
        _source = source;
        _bit = bit;
        Editor = FieldEditor.Flag;
        Minimum = 0;
        Maximum = 1;
        IsReadOnly = source.IsReadOnly;
    }

    public override long GetInteger() => (_source.GetInteger() >> _bit) & 1;

    protected override void WriteInteger(long value)
    {
        var current = _source.GetInteger();
        var updated = value != 0 ? current | (1L << _bit) : current & ~(1L << _bit);
        var error = _source.TrySetInteger(updated);
        if (error is not null)
        {
            throw new InvalidOperationException(error);
        }
    }
}
