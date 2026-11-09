using System.Buffers.Binary;
using System.Text;

namespace StarfighterCompanion.Core.Media;

/// <summary>
/// Reads single files from an ISO 9660 CD image, plain (2,048-byte sectors) or raw (2,352-byte sectors, mode 1 or
/// mode 2). The GOG and Steam DOS editions keep the game's files in such an image (<c>game.gog</c>, <c>game.fil</c>).
/// </summary>
public sealed class DiscImage : IDisposable
{
    private const int LogicalSector = 2048;

    private readonly Stream _stream;
    private readonly int _sectorSize;
    private readonly int _dataOffset;
    private readonly (uint Lba, uint Size) _root;

    private DiscImage(Stream stream, int sectorSize, int dataOffset, (uint, uint) root)
    {
        _stream = stream;
        _sectorSize = sectorSize;
        _dataOffset = dataOffset;
        _root = root;
    }

    /// <summary>Opens a CD image, or returns null when the file is not one.</summary>
    public static DiscImage? Open(string path)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        foreach (var (sectorSize, dataOffset) in new[] { (2048, 0), (2352, 16), (2352, 24) })
        {
            var pvd = Read(stream, (16L * sectorSize) + dataOffset, LogicalSector);
            if (pvd is not null && pvd[0] == 1 && Encoding.ASCII.GetString(pvd, 1, 5) == "CD001")
            {
                // The root directory record is at offset 156 of the primary volume descriptor.
                var root = (BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(156 + 2)), BinaryPrimitives.ReadUInt32LittleEndian(pvd.AsSpan(156 + 10)));
                return new DiscImage(stream, sectorSize, dataOffset, root);
            }
        }

        stream.Dispose();
        return null;
    }

    /// <summary>Reads a file by its path (folders separated by '/' or '\', any case), or returns null.</summary>
    public byte[]? ReadFile(string path, int maxSize = 16 * 1024 * 1024)
    {
        var entry = _root;
        var parts = path.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var found = List(entry).FirstOrDefault(e => string.Equals(e.Name, parts[i], StringComparison.OrdinalIgnoreCase));
            if (found.Name is null || found.IsDirectory != (i < parts.Length - 1))
            {
                return null;
            }

            entry = (found.Lba, found.Size);
        }

        return entry.Size > maxSize ? null : ReadExtent(entry.Lba, (int)entry.Size);
    }

    /// <summary>Finds a file by name anywhere up to <paramref name="depth"/> folders deep; returns its path.</summary>
    public string? Locate(string fileName, int depth = 3)
    {
        return Search(_root, string.Empty, depth);

        string? Search((uint Lba, uint Size) dir, string prefix, int left)
        {
            var entries = List(dir);
            var hit = entries.FirstOrDefault(e => !e.IsDirectory && string.Equals(e.Name, fileName, StringComparison.OrdinalIgnoreCase));
            if (hit.Name is not null)
            {
                return prefix + hit.Name;
            }

            if (left == 0)
            {
                return null;
            }

            foreach (var sub in entries.Where(e => e.IsDirectory))
            {
                var found = Search((sub.Lba, sub.Size), prefix + sub.Name + "/", left - 1);
                if (found is not null)
                {
                    return found;
                }
            }

            return null;
        }
    }

    public void Dispose() => _stream.Dispose();

    private List<(string Name, uint Lba, uint Size, bool IsDirectory)> List((uint Lba, uint Size) dir)
    {
        var list = new List<(string, uint, uint, bool)>();
        var data = dir.Size > 1024 * 1024 ? null : ReadExtent(dir.Lba, (int)dir.Size);
        if (data is null)
        {
            return list;
        }

        var p = 0;
        while (p < data.Length)
        {
            var length = data[p];
            if (length == 0)
            {
                // Records never cross a sector boundary: continue at the next sector.
                p = ((p / LogicalSector) + 1) * LogicalSector;
                continue;
            }

            if (p + 33 > data.Length || p + length > data.Length)
            {
                break;
            }

            var nameLength = data[p + 32];
            if (p + 33 + nameLength <= data.Length)
            {
                var name = Encoding.ASCII.GetString(data, p + 33, nameLength);
                if (nameLength != 1 || (data[p + 33] != 0 && data[p + 33] != 1))
                {
                    var semicolon = name.IndexOf(';');
                    if (semicolon >= 0)
                    {
                        name = name[..semicolon];
                    }

                    name = name.TrimEnd('.');
                    list.Add((name,
                        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 2)),
                        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 10)),
                        (data[p + 25] & 2) != 0));
                }
            }

            p += length;
        }

        return list;
    }

    private byte[]? ReadExtent(uint lba, int size)
    {
        var result = new byte[size];
        var done = 0;
        var sector = (long)lba;
        while (done < size)
        {
            var chunk = Math.Min(LogicalSector, size - done);
            var data = Read(_stream, (sector * _sectorSize) + _dataOffset, chunk);
            if (data is null)
            {
                return null;
            }

            data.CopyTo(result, done);
            done += chunk;
            sector++;
        }

        return result;
    }

    private static byte[]? Read(Stream stream, long position, int count)
    {
        try
        {
            if (position < 0 || position + count > stream.Length)
            {
                return null;
            }

            stream.Position = position;
            var buffer = new byte[count];
            stream.ReadExactly(buffer);
            return buffer;
        }
        catch (Exception ex) when (ex is IOException or EndOfStreamException)
        {
            return null;
        }
    }
}
