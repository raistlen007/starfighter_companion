using System.Buffers.Binary;

namespace StarfighterCompanion.Core.Media;

/// <summary>
/// Reads the icon of a Windows program (.exe/.dll resources) or an .ico file, choosing the largest picture up to
/// 256 pixels. Pictures are decoded from the Windows DIB format (1, 4, 8, 24 and 32 bits per pixel with the
/// transparency mask) or passed through as PNG.
/// </summary>
public static class IconReader
{
    private const int ResourceIcon = 3;
    private const int ResourceGroupIcon = 14;

    private sealed record Candidate(int Width, int Height, int BitCount, Func<byte[]?> Load);

    /// <summary>The main icon of a Windows program, or null when it has none (or is a DOS program).</summary>
    public static GameImage? FromExecutable(byte[] exe)
    {
        try
        {
            var resources = PeResources.Read(exe);
            if (resources is null)
            {
                return null;
            }

            // The first icon group is the one Explorer shows.
            var group = resources.First(ResourceGroupIcon);
            if (group is null || group.Length < 6)
            {
                return null;
            }

            var count = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(4));
            var candidates = new List<Candidate>();
            for (var i = 0; i < count; i++)
            {
                var e = 6 + i * 14;
                if (e + 14 > group.Length)
                {
                    break;
                }

                var id = BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(e + 12));
                candidates.Add(new Candidate(
                    group[e] == 0 ? 256 : group[e],
                    group[e + 1] == 0 ? 256 : group[e + 1],
                    BinaryPrimitives.ReadUInt16LittleEndian(group.AsSpan(e + 6)),
                    () => resources.Find(ResourceIcon, id)));
            }

            return Best(candidates);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>The largest picture of an .ico file, or null when it is not one.</summary>
    public static GameImage? FromIco(byte[] ico)
    {
        try
        {
            if (ico.Length < 6 || BinaryPrimitives.ReadUInt16LittleEndian(ico) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)) != 1)
            {
                return null;
            }

            var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
            var candidates = new List<Candidate>();
            for (var i = 0; i < count; i++)
            {
                var e = 6 + i * 16;
                if (e + 16 > ico.Length)
                {
                    break;
                }

                var size = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(e + 8));
                var offset = BinaryPrimitives.ReadInt32LittleEndian(ico.AsSpan(e + 12));
                candidates.Add(new Candidate(
                    ico[e] == 0 ? 256 : ico[e],
                    ico[e + 1] == 0 ? 256 : ico[e + 1],
                    BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(e + 6)),
                    () => offset >= 0 && size > 0 && offset + size <= ico.Length ? ico.AsSpan(offset, size).ToArray() : null));
            }

            return Best(candidates);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException or OverflowException)
        {
            return null;
        }
    }

    private static GameImage? Best(List<Candidate> candidates)
    {
        foreach (var c in candidates.OrderByDescending(c => c.Width * c.Height).ThenByDescending(c => c.BitCount))
        {
            var data = c.Load();
            if (data is null)
            {
                continue;
            }

            var image = GameImage.IsPng(data) ? GameImage.FromPng(data) : DecodeDib(data);
            if (image is not null)
            {
                return image;
            }
        }

        return null;
    }

    /// <summary>Decodes an icon picture stored as a DIB: BITMAPINFOHEADER, palette, colour rows, then the 1-bit mask.</summary>
    internal static GameImage? DecodeDib(byte[] dib)
    {
        if (dib.Length < 40)
        {
            return null;
        }

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(4));
        var height = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(8)) / 2; // colour rows + mask rows
        int bpp = BinaryPrimitives.ReadUInt16LittleEndian(dib.AsSpan(14));
        var compression = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(16));
        var colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(dib.AsSpan(32));
        if (headerSize < 40 || width <= 0 || height <= 0 || width > 1024 || height > 1024 || compression != 0 ||
            bpp is not (1 or 4 or 8 or 24 or 32))
        {
            return null;
        }

        var paletteCount = bpp <= 8 ? (colorsUsed > 0 ? colorsUsed : 1 << bpp) : 0;
        var paletteAt = headerSize;
        var pixelsAt = paletteAt + paletteCount * 4;
        var stride = ((width * bpp) + 31) / 32 * 4;
        var maskStride = (width + 31) / 32 * 4;
        var maskAt = pixelsAt + stride * height;
        var hasMask = maskAt + maskStride * height <= dib.Length;
        if (pixelsAt + stride * height > dib.Length)
        {
            return null;
        }

        var rgba = new byte[width * height * 4];
        var anyAlpha = false;
        for (var y = 0; y < height; y++)
        {
            var src = pixelsAt + (height - 1 - y) * stride; // rows are stored bottom-up
            for (var x = 0; x < width; x++)
            {
                byte r, g, b, a = 255;
                if (bpp <= 8)
                {
                    var bit = x * bpp;
                    var index = (dib[src + bit / 8] >> (8 - bpp - bit % 8)) & ((1 << bpp) - 1);
                    var p = paletteAt + Math.Min(index, paletteCount - 1) * 4;
                    (b, g, r) = (dib[p], dib[p + 1], dib[p + 2]);
                }
                else
                {
                    var p = src + x * (bpp / 8);
                    (b, g, r) = (dib[p], dib[p + 1], dib[p + 2]);
                    if (bpp == 32)
                    {
                        a = dib[p + 3];
                        anyAlpha |= a != 0;
                    }
                }

                var at = (y * width + x) * 4;
                (rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) = (r, g, b, a);
            }
        }

        // Older icons have no alpha channel: the mask says which pixels are see-through.
        if (hasMask && (bpp != 32 || !anyAlpha))
        {
            for (var y = 0; y < height; y++)
            {
                var src = maskAt + (height - 1 - y) * maskStride;
                for (var x = 0; x < width; x++)
                {
                    var transparent = (dib[src + x / 8] & (0x80 >> (x % 8))) != 0;
                    var at = (y * width + x) * 4;
                    if (transparent)
                    {
                        Array.Clear(rgba, at, 4);
                    }
                    else
                    {
                        rgba[at + 3] = 255;
                    }
                }
            }
        }

        return GameImage.FromRgba(width, height, rgba);
    }

    /// <summary>Just enough of the PE format to reach the resource tree.</summary>
    private sealed class PeResources
    {
        private readonly byte[] _exe;
        private readonly int _rootOffset;
        private readonly (uint Va, uint Size, uint Raw)[] _sections;

        private PeResources(byte[] exe, int rootOffset, (uint, uint, uint)[] sections)
        {
            _exe = exe;
            _rootOffset = rootOffset;
            _sections = sections;
        }

        public static PeResources? Read(byte[] exe)
        {
            if (exe.Length < 0x40 || exe[0] != 'M' || exe[1] != 'Z')
            {
                return null;
            }

            var pe = BinaryPrimitives.ReadInt32LittleEndian(exe.AsSpan(0x3C));
            if (pe <= 0 || pe + 24 > exe.Length || exe[pe] != 'P' || exe[pe + 1] != 'E' || exe[pe + 2] != 0 || exe[pe + 3] != 0)
            {
                return null; // a DOS program
            }

            var sectionCount = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe + 6));
            var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(pe + 20));
            var optional = pe + 24;
            var magic = BinaryPrimitives.ReadUInt16LittleEndian(exe.AsSpan(optional));
            var directories = optional + (magic == 0x20B ? 112 : 96);
            var resourceRva = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(directories + 2 * 8));
            if (resourceRva == 0)
            {
                return null;
            }

            var sections = new (uint, uint, uint)[sectionCount];
            var table = optional + optionalSize;
            for (var i = 0; i < sectionCount; i++)
            {
                var s = table + i * 40;
                var virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(s + 8));
                var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(s + 16));
                sections[i] = (BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(s + 12)),
                    Math.Max(virtualSize, rawSize),
                    BinaryPrimitives.ReadUInt32LittleEndian(exe.AsSpan(s + 20)));
            }

            var resources = new PeResources(exe, 0, sections);
            var root = resources.ToOffset(resourceRva);
            return root < 0 ? null : new PeResources(exe, root, sections);
        }

        /// <summary>The data of the first resource of <paramref name="type"/>, in any language.</summary>
        public byte[]? First(int type)
        {
            var typeDir = Child(_rootOffset, type);
            if (typeDir is null)
            {
                return null;
            }

            var nameDir = FirstChild(typeDir.Value);
            return nameDir is null ? null : Leaf(nameDir.Value);
        }

        /// <summary>The data of resource <paramref name="type"/>/<paramref name="id"/>, in any language.</summary>
        public byte[]? Find(int type, int id)
        {
            var typeDir = Child(_rootOffset, type);
            var nameDir = typeDir is null ? null : Child(typeDir.Value, id);
            return nameDir is null ? null : Leaf(nameDir.Value);
        }

        private byte[]? Leaf(int directory)
        {
            // Go down to the first language (and any further levels) until a data entry is reached.
            var entry = (int?)directory;
            for (var depth = 0; depth < 4 && entry is not null; depth++)
            {
                var (offset, isDirectory) = FirstEntry(entry.Value);
                if (offset < 0)
                {
                    return null;
                }

                if (!isDirectory)
                {
                    var at = _rootOffset + offset;
                    var rva = BinaryPrimitives.ReadUInt32LittleEndian(_exe.AsSpan(at));
                    var size = BinaryPrimitives.ReadInt32LittleEndian(_exe.AsSpan(at + 4));
                    var file = ToOffset(rva);
                    return file < 0 || size <= 0 || file + size > _exe.Length ? null : _exe.AsSpan(file, size).ToArray();
                }

                entry = _rootOffset + offset;
            }

            return null;
        }

        private (int Offset, bool IsDirectory) FirstEntry(int directory)
        {
            var count = Count(directory);
            if (count == 0)
            {
                return (-1, false);
            }

            var target = BinaryPrimitives.ReadUInt32LittleEndian(_exe.AsSpan(directory + 16 + 4));
            return ((int)(target & 0x7FFFFFFF), (target & 0x80000000) != 0);
        }

        private int? FirstChild(int directory)
        {
            var (offset, isDirectory) = FirstEntry(directory);
            return offset >= 0 && isDirectory ? _rootOffset + offset : null;
        }

        private int? Child(int directory, int id)
        {
            var count = Count(directory);
            for (var i = 0; i < count; i++)
            {
                var e = directory + 16 + i * 8;
                var name = BinaryPrimitives.ReadUInt32LittleEndian(_exe.AsSpan(e));
                var target = BinaryPrimitives.ReadUInt32LittleEndian(_exe.AsSpan(e + 4));
                if ((name & 0x80000000) == 0 && name == id && (target & 0x80000000) != 0)
                {
                    return _rootOffset + (int)(target & 0x7FFFFFFF);
                }
            }

            return null;
        }

        private int Count(int directory) =>
            BinaryPrimitives.ReadUInt16LittleEndian(_exe.AsSpan(directory + 12)) +
            BinaryPrimitives.ReadUInt16LittleEndian(_exe.AsSpan(directory + 14));

        private int ToOffset(uint rva)
        {
            foreach (var (va, size, raw) in _sections)
            {
                if (rva >= va && rva < va + size)
                {
                    var offset = (long)rva - va + raw;
                    return offset < _exe.Length ? (int)offset : -1;
                }
            }

            return -1;
        }
    }
}
