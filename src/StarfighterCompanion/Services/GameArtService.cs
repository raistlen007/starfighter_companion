using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Library;
using StarfighterCompanion.Core.Media;

namespace StarfighterCompanion.Services;

/// <summary>
/// Game icons and X-Wing pilot portraits, read from the player's installed games and turned into bitmaps.
/// Everything is cached; <see cref="Reset"/> forgets it when the game folders change.
/// </summary>
public sealed class GameArtService
{
    private readonly Dictionary<(string Variant, string Folder), Bitmap?> _icons = new();
    private readonly object _lock = new();
    private Task<PortraitSet?>? _portraits;

    /// <summary>The faces of the DOS X-Wing pilot selection screen, as bitmaps.</summary>
    public sealed class PortraitSet
    {
        public PortraitSet(XWingPortraits source)
        {
            Source = source.Source;
            Faces = source.Faces.Select(ToBitmap).ToList();
            Width = source.Faces[0].Width;
            Height = source.Faces[0].Height;
        }

        public string Source { get; }

        public IReadOnlyList<Bitmap?> Faces { get; }

        public int Width { get; }

        public int Height { get; }

        public Bitmap? For(string pilotName) => Faces[XWingPortraits.FaceIndex(pilotName) % Faces.Count];
    }

    /// <summary>The icon of the game in <paramref name="folder"/>, or null (the UI then shows its placeholder).</summary>
    public Task<Bitmap?> IconAsync(string variantId, string? folder)
    {
        // OpenXW-family and XWVM icons come from their program, wherever it is, not from the pilot folder.
        if (GameIcons.IsPilotOnlyVersion(variantId))
        {
            folder = string.Empty;
        }
        else if (string.IsNullOrEmpty(folder))
        {
            return Task.FromResult<Bitmap?>(null);
        }

        lock (_lock)
        {
            if (_icons.TryGetValue((variantId, folder), out var cached))
            {
                return Task.FromResult(cached);
            }
        }

        return Task.Run(() =>
        {
            Bitmap? bitmap;
            try
            {
                var image = GameIcons.Find(variantId, folder);
                bitmap = image is null ? null : ToBitmap(image);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                bitmap = null;
            }

            lock (_lock)
            {
                _icons[(variantId, folder)] = bitmap;
            }

            return bitmap;
        });
    }

    /// <summary>Looks for PILOT.LFD in the X-Wing folders (DOS editions first); found once and then reused.</summary>
    public Task<PortraitSet?> PortraitsAsync(IEnumerable<string> folders)
    {
        lock (_lock)
        {
            if (_portraits is not null)
            {
                return _portraits;
            }

            var list = folders.ToList();
            return _portraits = Task.Run(() =>
            {
                try
                {
                    var found = XWingPortraits.Find(list);
                    return found is null ? null : new PortraitSet(found);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
                {
                    return null;
                }
            });
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _icons.Clear();
            _portraits = null;
            GameIcons.ResetProgramFolders();
        }
    }

    public static Bitmap? ToBitmap(GameImage image)
    {
        try
        {
            if (image.Png is { } png)
            {
                using var stream = new MemoryStream(png);
                return new Bitmap(stream);
            }

            var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
            using (var buffer = bitmap.Lock())
            {
                var rowBytes = image.Width * 4;
                for (var y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(image.Rgba!, y * rowBytes, buffer.Address + (y * buffer.RowBytes), rowBytes);
                }
            }

            return bitmap;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
