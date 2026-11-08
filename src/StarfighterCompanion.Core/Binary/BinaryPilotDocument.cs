using System.Buffers.Binary;
using System.Text;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Binary;

/// <summary>A value at a fixed offset inside a binary pilot record.</summary>
public sealed class BinaryField : EditableField
{
    private readonly BinaryPilotDocument _document;

    internal BinaryField(BinaryPilotDocument document, BinaryFieldDef def)
        : base(def.Id, def.Group, def.Label)
    {
        _document = document;
        Definition = def;
    }

    public BinaryFieldDef Definition { get; }

    public override long GetInteger()
    {
        var span = _document.Record(Definition.Offset, Definition.Size);
        return Definition.Kind switch
        {
            BinaryKind.UInt8 => span[0],
            BinaryKind.Int8 => (sbyte)span[0],
            BinaryKind.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(span),
            BinaryKind.Int16 => BinaryPrimitives.ReadInt16LittleEndian(span),
            BinaryKind.UInt32 => BinaryPrimitives.ReadUInt32LittleEndian(span),
            BinaryKind.Int32 => BinaryPrimitives.ReadInt32LittleEndian(span),
            _ => throw new NotSupportedException($"{Label} is a text field."),
        };
    }

    public override string GetText()
    {
        if (Definition.Kind != BinaryKind.Text)
        {
            return base.GetText();
        }

        var span = _document.Record(Definition.Offset, Definition.Size);
        var end = span.IndexOf((byte)0);
        if (end < 0)
        {
            end = span.Length;
        }

        return Encoding.Latin1.GetString(span[..end]);
    }

    protected override void WriteInteger(long value)
    {
        var span = _document.RecordForWrite(Definition.Offset, Definition.Size);
        switch (Definition.Kind)
        {
            case BinaryKind.UInt8:
            case BinaryKind.Int8:
                span[0] = unchecked((byte)value);
                break;
            case BinaryKind.UInt16:
            case BinaryKind.Int16:
                BinaryPrimitives.WriteUInt16LittleEndian(span, unchecked((ushort)value));
                break;
            case BinaryKind.UInt32:
            case BinaryKind.Int32:
                BinaryPrimitives.WriteUInt32LittleEndian(span, unchecked((uint)value));
                break;
            default:
                throw new NotSupportedException($"{Label} is a text field.");
        }
    }

    protected override void WriteText(string value)
    {
        if (Definition.Kind != BinaryKind.Text)
        {
            throw new NotSupportedException($"{Label} is not a text field.");
        }

        var span = _document.RecordForWrite(Definition.Offset, Definition.Size);
        var bytes = Encoding.ASCII.GetBytes(value);

        // Only the characters and the terminator change; whatever followed the old terminator is kept,
        // exactly as the games' own strcpy would do.
        bytes.CopyTo(span);
        span[bytes.Length] = 0;
    }
}

/// <summary>
/// A pilot file made of one fixed-size binary record, optionally stored more than once
/// (several games keep an identical backup copy right after the main record).
/// All bytes the editor does not understand are kept exactly as they were.
/// </summary>
public abstract class BinaryPilotDocument : PilotDocument
{
    private readonly byte[] _data;
    private readonly int[] _copyOffsets;

    protected BinaryPilotDocument(byte[] data, RecordSchema schema, int copies)
    {
        if (data.Length != schema.Size * copies)
        {
            throw new InvalidDataException($"Expected {schema.Size * copies} bytes, the file has {data.Length}.");
        }

        _data = (byte[])data.Clone();
        Schema = schema;
        _copyOffsets = Enumerable.Range(1, copies - 1).Select(i => i * schema.Size).ToArray();

        foreach (var def in schema.Fields)
        {
            AddField(new BinaryField(this, def)
            {
                Help = def.Help,
                Editor = def.Editor,
                Minimum = def.Minimum,
                Maximum = def.Maximum,
                Choices = def.Choices,
                MaxLength = def.Kind == BinaryKind.Text ? def.Length - 1 : 0,
                IsReadOnly = def.ReadOnly,
            });
        }

        if (copies > 1)
        {
            var identical = _copyOffsets.All(o => _data.AsSpan(0, schema.Size).SequenceEqual(_data.AsSpan(o, schema.Size)));
            Notes.Add(identical
                ? $"The file holds {copies} identical copies of the pilot record. Edits are written to all of them."
                : $"The file holds {copies} copies of the pilot record that differ; the first one (the one the game reads) is shown. When you save, the other copies are replaced with it, as the game itself does.");
        }
    }

    public RecordSchema Schema { get; }

    internal ReadOnlySpan<byte> Record(int offset, int length) => _data.AsSpan(offset, length);

    internal Span<byte> RecordForWrite(int offset, int length) => _data.AsSpan(offset, length);

    /// <summary>Raw access for rules that need to look at undecoded bytes.</summary>
    protected ReadOnlySpan<byte> RawRecord => _data.AsSpan(0, Schema.Size);

    protected override byte[] Serialize()
    {
        // Untouched files are written back byte for byte. Edited ones get every copy refreshed.
        if (IsDirty)
        {
            foreach (var offset in _copyOffsets)
            {
                Array.Copy(_data, 0, _data, offset, Schema.Size);
            }
        }

        return (byte[])_data.Clone();
    }
}
