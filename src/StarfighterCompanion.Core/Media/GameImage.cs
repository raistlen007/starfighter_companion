namespace StarfighterCompanion.Core.Media;

/// <summary>
/// A picture read from an installed game: either decoded pixels (RGBA, 8 bits per channel, rows top to bottom) or,
/// for icons that are stored as PNG, the PNG file itself. Nothing is ever shipped with the program; these are only
/// read from the player's own installation.
/// </summary>
public sealed class GameImage
{
    private GameImage(int width, int height, byte[]? rgba, byte[]? png)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
        Png = png;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Pixels as R, G, B, A bytes, or null when the image is <see cref="Png"/>.</summary>
    public byte[]? Rgba { get; }

    /// <summary>An encoded PNG file, or null when the image is <see cref="Rgba"/>.</summary>
    public byte[]? Png { get; }

    public static GameImage FromRgba(int width, int height, byte[] rgba)
    {
        if (width <= 0 || height <= 0 || rgba.Length != width * height * 4)
        {
            throw new ArgumentException("The pixel buffer does not match the size.", nameof(rgba));
        }

        return new GameImage(width, height, rgba, null);
    }

    /// <summary>Wraps PNG data; returns null when it is not a PNG.</summary>
    public static GameImage? FromPng(byte[] png)
    {
        if (!IsPng(png) || png.Length < 24)
        {
            return null;
        }

        // IHDR is always the first chunk: width and height are big-endian at 16 and 20.
        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return width > 0 && height > 0 ? new GameImage(width, height, null, png) : null;
    }

    public static bool IsPng(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[0] == 0x89 && data[1] == (byte)'P' && data[2] == (byte)'N' && data[3] == (byte)'G' &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    /// <summary>Colour of one pixel (R, G, B, A); only for <see cref="Rgba"/> images.</summary>
    public (byte R, byte G, byte B, byte A) Pixel(int x, int y)
    {
        var i = (y * Width + x) * 4;
        var p = Rgba ?? throw new InvalidOperationException("The image is a PNG.");
        return (p[i], p[i + 1], p[i + 2], p[i + 3]);
    }
}
