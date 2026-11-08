using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Binary;

/// <summary>Storage type of a field inside a binary pilot record (all little-endian).</summary>
public enum BinaryKind
{
    UInt8,
    Int8,
    UInt16,
    Int16,
    UInt32,
    Int32,

    /// <summary>Zero terminated ASCII text in a fixed size buffer.</summary>
    Text,
}

/// <summary>Describes one value at a fixed offset of a binary pilot record.</summary>
public sealed class BinaryFieldDef
{
    public required string Id { get; init; }
    public required string Group { get; init; }
    public required string Label { get; init; }
    public required int Offset { get; init; }
    public required BinaryKind Kind { get; init; }

    /// <summary>Buffer size in bytes for <see cref="BinaryKind.Text"/> (including the terminating zero).</summary>
    public int Length { get; init; }

    public FieldEditor Editor { get; init; }
    public long Minimum { get; init; }
    public long Maximum { get; init; }
    public IReadOnlyList<ChoiceOption>? Choices { get; init; }
    public string? Help { get; init; }
    public bool ReadOnly { get; init; }

    public int Size => Kind switch
    {
        BinaryKind.UInt8 or BinaryKind.Int8 => 1,
        BinaryKind.UInt16 or BinaryKind.Int16 => 2,
        BinaryKind.UInt32 or BinaryKind.Int32 => 4,
        BinaryKind.Text => Length,
        _ => throw new ArgumentOutOfRangeException(),
    };

    public static (long Min, long Max) StorageRange(BinaryKind kind) => kind switch
    {
        BinaryKind.UInt8 => (byte.MinValue, byte.MaxValue),
        BinaryKind.Int8 => (sbyte.MinValue, sbyte.MaxValue),
        BinaryKind.UInt16 => (ushort.MinValue, ushort.MaxValue),
        BinaryKind.Int16 => (short.MinValue, short.MaxValue),
        BinaryKind.UInt32 => (uint.MinValue, uint.MaxValue),
        BinaryKind.Int32 => (int.MinValue, int.MaxValue),
        _ => (0, 0),
    };
}

/// <summary>The complete description of a binary pilot record.</summary>
public sealed class RecordSchema
{
    public RecordSchema(int size, IReadOnlyList<BinaryFieldDef> fields)
    {
        Size = size;
        Fields = fields;
    }

    public int Size { get; }

    public IReadOnlyList<BinaryFieldDef> Fields { get; }
}

/// <summary>Optional constraints for a field added through <see cref="LayoutBuilder"/>.</summary>
public sealed record FieldOptions
{
    public long? Min { get; init; }
    public long? Max { get; init; }
    public IReadOnlyList<ChoiceOption>? Choices { get; init; }
    public string? Help { get; init; }
    public bool ReadOnly { get; init; }
    public bool Flag { get; init; }

    public static readonly FieldOptions None = new();
    public static readonly FieldOptions NonNegative = new() { Min = 0 };
    public static readonly FieldOptions AsFlag = new() { Flag = true };

    public static FieldOptions Range(long min, long max, string? help = null) => new() { Min = min, Max = max, Help = help };

    public static FieldOptions OneOf(IReadOnlyList<ChoiceOption> choices, string? help = null) => new() { Choices = choices, Help = help };
}

/// <summary>
/// Builds a <see cref="RecordSchema"/> by walking a packed C structure from start to end.
/// Every byte is either described or explicitly skipped, so the final size proves the layout is complete.
/// </summary>
public sealed class LayoutBuilder
{
    private readonly List<BinaryFieldDef> _fields = new();
    private readonly Stack<string> _idScopes = new();
    private readonly Stack<string> _groupScopes = new();

    public int Offset { get; private set; }

    /// <summary>Opens a nested scope: <paramref name="idPart"/> prefixes field ids, <paramref name="group"/> adds a tree level.</summary>
    public IDisposable Scope(string idPart, string? group = null)
    {
        _idScopes.Push(idPart);
        if (group is not null)
        {
            _groupScopes.Push(group);
        }

        return new ScopeHandle(this, group is not null);
    }

    /// <summary>Opens a tree level without changing ids.</summary>
    public IDisposable Group(string group)
    {
        _groupScopes.Push(group);
        return new ScopeHandle(this, popGroup: true, popId: false);
    }

    public void Skip(int bytes)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        Offset += bytes;
    }

    /// <summary>Fails fast if the layout drifted from a known offset.</summary>
    public void Expect(int offset, string what)
    {
        if (Offset != offset)
        {
            throw new InvalidOperationException($"Layout error at {what}: expected offset 0x{offset:X}, got 0x{Offset:X}.");
        }
    }

    public void U8(string id, string label, FieldOptions? o = null) => Add(id, label, BinaryKind.UInt8, 0, o);
    public void I8(string id, string label, FieldOptions? o = null) => Add(id, label, BinaryKind.Int8, 0, o);
    public void U16(string id, string label, FieldOptions? o = null) => Add(id, label, BinaryKind.UInt16, 0, o);
    public void I16(string id, string label, FieldOptions? o = null) => Add(id, label, BinaryKind.Int16, 0, o);
    public void U32(string id, string label, FieldOptions? o = null) => Add(id, label, BinaryKind.UInt32, 0, o);
    public void I32(string id, string label, FieldOptions? o = null) => Add(id, label, BinaryKind.Int32, 0, o);
    public void Text(string id, string label, int length, FieldOptions? o = null) => Add(id, label, BinaryKind.Text, length, o);

    /// <summary>Adds <paramref name="count"/> consecutive values of the same kind.</summary>
    public void Array(string id, BinaryKind kind, int count, Func<int, string> label, FieldOptions? o = null, Func<int, bool>? include = null)
    {
        for (var i = 0; i < count; i++)
        {
            if (include is null || include(i))
            {
                Add($"{id}[{i}]", label(i), kind, 0, o);
            }
            else
            {
                Skip(new BinaryFieldDef { Id = "", Group = "", Label = "", Offset = 0, Kind = kind }.Size);
            }
        }
    }

    /// <summary>Repeats a structure <paramref name="count"/> times, each with its own id scope.</summary>
    public void Repeat(int count, string idPart, Func<int, string?> group, Action<int> body)
    {
        for (var i = 0; i < count; i++)
        {
            using (Scope($"{idPart}[{i}]", group(i)))
            {
                body(i);
            }
        }
    }

    public RecordSchema Build(int expectedSize)
    {
        if (Offset != expectedSize)
        {
            throw new InvalidOperationException($"Layout error: record is {Offset} bytes, expected {expectedSize}.");
        }

        if (_fields.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() != _fields.Count)
        {
            var dup = _fields.GroupBy(f => f.Id).First(g => g.Count() > 1).Key;
            throw new InvalidOperationException($"Layout error: duplicate field id '{dup}'.");
        }

        return new RecordSchema(Offset, _fields.ToArray());
    }

    private void Add(string id, string label, BinaryKind kind, int length, FieldOptions? o)
    {
        o ??= FieldOptions.None;
        var (storageMin, storageMax) = BinaryFieldDef.StorageRange(kind);
        var editor = kind == BinaryKind.Text ? FieldEditor.Text
            : o.Flag ? FieldEditor.Flag
            : o.Choices is not null ? FieldEditor.Choice
            : FieldEditor.Integer;

        var def = new BinaryFieldDef
        {
            Id = string.Join(".", _idScopes.Reverse().Append(id)),
            Group = string.Join("/", _groupScopes.Reverse()),
            Label = label,
            Offset = Offset,
            Kind = kind,
            Length = length,
            Editor = editor,
            Minimum = Math.Max(storageMin, o.Min ?? storageMin),
            Maximum = Math.Min(storageMax, o.Max ?? storageMax),
            Choices = o.Choices,
            Help = o.Help,
            ReadOnly = o.ReadOnly,
        };

        _fields.Add(def);
        Offset += def.Size;
    }

    private sealed class ScopeHandle : IDisposable
    {
        private readonly LayoutBuilder _owner;
        private readonly bool _popGroup;
        private readonly bool _popId;
        private bool _disposed;

        public ScopeHandle(LayoutBuilder owner, bool popGroup, bool popId = true)
        {
            _owner = owner;
            _popGroup = popGroup;
            _popId = popId;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_popId)
            {
                _owner._idScopes.Pop();
            }

            if (_popGroup)
            {
                _owner._groupScopes.Pop();
            }
        }
    }
}
