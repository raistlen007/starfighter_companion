using System.Buffers.Binary;
using System.Text;

namespace StarfighterCompanion.Core.Media;

/// <summary>One resource of an LFD file.</summary>
public sealed record LfdResource(string Type, string Name, byte[] Data);

/// <summary>
/// A LucasArts LFD resource file (X-Wing and TIE Fighter DOS editions).
/// Layout (Landru res.c): a 16-byte header <c>"RMAP" name[8] length</c>, a map of 16-byte entries
/// <c>type[4] name[8] size</c>, then every resource as its own 16-byte header followed by its data.
/// </summary>
public sealed class LfdArchive
{
    private const int HeaderSize = 16;

    private LfdArchive(IReadOnlyList<LfdResource> resources) => Resources = resources;

    public IReadOnlyList<LfdResource> Resources { get; }

    public static LfdArchive Read(byte[] data)
    {
        if (data.Length < HeaderSize || Encoding.ASCII.GetString(data, 0, 4) != "RMAP")
        {
            throw new InvalidDataException("Not an LFD resource file.");
        }

        var mapLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        if (mapLength < 0 || mapLength % HeaderSize != 0 || HeaderSize + mapLength > data.Length)
        {
            throw new InvalidDataException("The LFD resource map is damaged.");
        }

        var resources = new List<LfdResource>();
        var offset = HeaderSize + mapLength;
        for (var i = 0; i < mapLength / HeaderSize; i++)
        {
            if (offset + HeaderSize > data.Length)
            {
                break;
            }

            var type = Encoding.ASCII.GetString(data, offset, 4);
            var name = Text(data.AsSpan(offset + 4, 8));
            var size = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 12));
            offset += HeaderSize;
            if (size < 0 || offset + size > data.Length)
            {
                break;
            }

            resources.Add(new LfdResource(type, name, data.AsSpan(offset, size).ToArray()));
            offset += size;
        }

        return new LfdArchive(resources);
    }

    public LfdResource? Find(string type, string name) =>
        Resources.FirstOrDefault(r => r.Type == type && string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<LfdResource> All(string type) => Resources.Where(r => r.Type == type);

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        var end = bytes.IndexOf((byte)0);
        return Encoding.ASCII.GetString(end < 0 ? bytes : bytes[..end]);
    }
}

/// <summary>A 256-colour VGA palette built from LFD <c>PLTT</c> resources.</summary>
public sealed class VgaPalette
{
    private readonly (byte R, byte G, byte B)[] _colors = new (byte, byte, byte)[256];

    public (byte R, byte G, byte B) this[int index] => _colors[index];

    /// <summary>
    /// Applies a <c>PLTT</c> resource: first index, last index, then 8-bit R, G, B per colour (Landru pal.c, which
    /// shifts them down to the VGA's 6 bits). Colour cycling information after the colours is ignored.
    /// </summary>
    public void Apply(byte[] pltt)
    {
        if (pltt.Length < 2)
        {
            return;
        }

        int first = pltt[0], last = pltt[1];
        for (var i = first; i <= last; i++)
        {
            var at = 2 + (i - first) * 3;
            if (at + 2 >= pltt.Length)
            {
                break;
            }

            // Drop to 6 bits like the VGA did, then stretch back to 8 bits.
            _colors[i] = (Six(pltt[at]), Six(pltt[at + 1]), Six(pltt[at + 2]));
        }
    }

    private static byte Six(byte value) => (byte)((value >> 2) * 255 / 63);
}

/// <summary>
/// LFD <c>ANIM</c> resources (Landru actanim.c): a 16-bit frame count, then for each frame a 32-bit length and a
/// delta image. A delta image (Landru delta.c) starts with its bounds <c>left, top, right, bottom</c> (inclusive,
/// 16-bit) followed by lines <c>count, x, y, pixels</c> until a zero count; bit 0 of the count marks run-length
/// coded pixels and the pixel count is <c>count &gt;&gt; 1</c>. Pixels that no line covers are transparent.
/// </summary>
public static class LfdAnimation
{
    private sealed record Line(int X, int Y, byte[] Pixels);

    private sealed record Frame(int Left, int Top, int Right, int Bottom, List<Line> Lines);

    /// <summary>
    /// Decodes every frame of an ANIM resource onto a canvas the size of all frames together, so frames line up
    /// the way the game draws them. Empty frames come back fully transparent.
    /// </summary>
    public static IReadOnlyList<GameImage> Decode(byte[] anim, VgaPalette palette)
    {
        var frames = ReadFrames(anim);
        var drawn = frames.Where(f => f is not null).Select(f => f!).ToList();
        if (drawn.Count == 0)
        {
            return Array.Empty<GameImage>();
        }

        // Bounds of every frame, widened to the pixels actually present in case a header is off.
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var f in drawn)
        {
            left = Math.Min(left, f.Left);
            top = Math.Min(top, f.Top);
            right = Math.Max(right, f.Right);
            bottom = Math.Max(bottom, f.Bottom);
            foreach (var line in f.Lines)
            {
                left = Math.Min(left, line.X);
                top = Math.Min(top, line.Y);
                right = Math.Max(right, line.X + line.Pixels.Length - 1);
                bottom = Math.Max(bottom, line.Y);
            }
        }

        var width = right - left + 1;
        var height = bottom - top + 1;
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
        {
            throw new InvalidDataException("The animation's frame bounds are not plausible.");
        }

        var images = new List<GameImage>(frames.Count);
        foreach (var frame in frames)
        {
            var rgba = new byte[width * height * 4];
            foreach (var line in frame?.Lines ?? new List<Line>())
            {
                var row = (line.Y - top) * width;
                for (var i = 0; i < line.Pixels.Length; i++)
                {
                    var at = (row + line.X - left + i) * 4;
                    var (r, g, b) = palette[line.Pixels[i]];
                    rgba[at] = r;
                    rgba[at + 1] = g;
                    rgba[at + 2] = b;
                    rgba[at + 3] = 255;
                }
            }

            images.Add(GameImage.FromRgba(width, height, rgba));
        }

        return images;
    }

    private static List<Frame?> ReadFrames(byte[] anim)
    {
        if (anim.Length < 2)
        {
            throw new InvalidDataException("The animation is empty.");
        }

        var count = BinaryPrimitives.ReadInt16LittleEndian(anim);
        if (count < 0)
        {
            throw new InvalidDataException("The animation's frame count is damaged.");
        }

        var frames = new List<Frame?>(count);
        var offset = 2;
        for (var i = 0; i < count; i++)
        {
            if (offset + 4 > anim.Length)
            {
                throw new InvalidDataException("The animation ends too early.");
            }

            var length = BinaryPrimitives.ReadInt32LittleEndian(anim.AsSpan(offset));
            offset += 4;
            if (length < 0 || offset + length > anim.Length)
            {
                throw new InvalidDataException("An animation frame runs past the end of the resource.");
            }

            frames.Add(length == 0 ? null : ReadDelta(anim.AsSpan(offset, length)));
            offset += length;
        }

        return frames;
    }

    private static Frame? ReadDelta(ReadOnlySpan<byte> d)
    {
        if (d.Length < 8)
        {
            return null;
        }

        var left = BinaryPrimitives.ReadInt16LittleEndian(d);
        var top = BinaryPrimitives.ReadInt16LittleEndian(d[2..]);
        var right = BinaryPrimitives.ReadInt16LittleEndian(d[4..]);
        var bottom = BinaryPrimitives.ReadInt16LittleEndian(d[6..]);
        if (left == -1 && top == -1)
        {
            return null;
        }

        var lines = new List<Line>();
        var p = 8;
        while (p + 2 <= d.Length)
        {
            var header = BinaryPrimitives.ReadUInt16LittleEndian(d[p..]);
            if (header == 0)
            {
                break;
            }

            if (p + 6 > d.Length)
            {
                throw new InvalidDataException("A delta line header is cut off.");
            }

            int x = BinaryPrimitives.ReadInt16LittleEndian(d[(p + 2)..]);
            int y = BinaryPrimitives.ReadInt16LittleEndian(d[(p + 4)..]);
            p += 6;
            var count = header >> 1;
            var pixels = new byte[count];
            if ((header & 1) == 0)
            {
                if (p + count > d.Length)
                {
                    throw new InvalidDataException("A delta line runs past the end of the frame.");
                }

                d.Slice(p, count).CopyTo(pixels);
                p += count;
            }
            else
            {
                var filled = 0;
                while (filled < count)
                {
                    if (p >= d.Length)
                    {
                        throw new InvalidDataException("A compressed delta line runs past the end of the frame.");
                    }

                    var control = d[p++];
                    var run = Math.Min(control >> 1, count - filled);
                    if ((control & 1) != 0)
                    {
                        if (p >= d.Length)
                        {
                            throw new InvalidDataException("A compressed delta line runs past the end of the frame.");
                        }

                        pixels.AsSpan(filled, run).Fill(d[p++]);
                    }
                    else
                    {
                        if (p + (control >> 1) > d.Length)
                        {
                            throw new InvalidDataException("A compressed delta line runs past the end of the frame.");
                        }

                        d.Slice(p, run).CopyTo(pixels.AsSpan(filled));
                        p += control >> 1;
                    }

                    filled += run;
                    if (control >> 1 == 0)
                    {
                        // A zero-length control byte would never finish the line.
                        throw new InvalidDataException("A compressed delta line is damaged.");
                    }
                }
            }

            lines.Add(new Line(x, y, pixels));
        }

        return new Frame(left, top, right, bottom, lines);
    }
}
