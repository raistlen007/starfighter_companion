using System.Xml;
using StarfighterCompanion.Core.Formats;

namespace StarfighterCompanion.Core.Library;

/// <summary>A pilot file found in a variant's folder.</summary>
public sealed record PilotEntry(string Path, string Name, PilotFormat Format, DateTime LastWrite)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>Finds the pilots of a variant inside its folder.</summary>
public static class PilotScanner
{
    private const long MaxPilotSize = 1024 * 1024;

    public static IReadOnlyList<PilotEntry> Scan(VariantDefinition variant, string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            return Array.Empty<PilotEntry>();
        }

        var extensions = PilotFormats.All.Where(variant.Accepts).SelectMany(f => f.Extensions).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = variant.SearchDepth > 0,
            MaxRecursionDepth = variant.SearchDepth,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System,
        };

        var result = new List<PilotEntry>();
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder, "*", options).Where(p => extensions.Contains(System.IO.Path.GetExtension(p))).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return result;
        }

        foreach (var path in files)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Length > MaxPilotSize)
                {
                    continue;
                }

                var data = File.ReadAllBytes(path);
                var format = PilotFormats.Detect(path, data);
                if (format is null || !variant.Accepts(format))
                {
                    continue;
                }

                result.Add(new PilotEntry(path, ReadName(path, data, format), format, info.LastWriteTime));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Locked or unreadable: skip it.
            }
        }

        return result.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Gets the pilot's display name cheaply, without decoding the whole file.</summary>
    internal static string ReadName(string path, byte[] data, PilotFormat format)
    {
        switch (format)
        {
            case Formats.XvT.XvtPilotFormat or Formats.XvT.BopPilotFormat or Formats.Xwa.XwaPilotFormat:
                var end = Array.IndexOf(data, (byte)0, 0, Math.Min(14, data.Length));
                var name = System.Text.Encoding.Latin1.GetString(data, 0, end < 0 ? 14 : end);
                return name.Length > 0 ? name : System.IO.Path.GetFileNameWithoutExtension(path);

            case Formats.Xwvm.XwvmPilotFormat:
                try
                {
                    using var stream = new MemoryStream(data);
                    using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                    reader.MoveToContent();
                    return reader.GetAttribute("Name") ?? System.IO.Path.GetFileNameWithoutExtension(path);
                }
                catch (XmlException)
                {
                    return System.IO.Path.GetFileNameWithoutExtension(path);
                }

            default:
                return System.IO.Path.GetFileNameWithoutExtension(path);
        }
    }
}
