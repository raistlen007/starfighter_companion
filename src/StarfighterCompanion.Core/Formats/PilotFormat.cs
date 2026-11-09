using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Formats;

/// <summary>One kind of pilot file the editor can open.</summary>
public abstract class PilotFormat
{
    /// <summary>Game the format belongs to.</summary>
    public abstract string Game { get; }

    /// <summary>Description of the exact variant, shown to the user.</summary>
    public abstract string Name { get; }

    /// <summary>File extensions including the dot, lower case.</summary>
    public abstract IReadOnlyList<string> Extensions { get; }

    /// <summary>Human readable description of what a matching file looks like (for error messages).</summary>
    public abstract string Signature { get; }

    public abstract bool Matches(string fileName, ReadOnlySpan<byte> data);

    public abstract PilotDocument Open(string fileName, byte[] data);

    /// <summary>Allows edition-specific display metadata without changing the binary format.</summary>
    public virtual PilotDocument Open(string fileName, byte[] data, string? variantId) => Open(fileName, data);

    protected static bool HasExtension(string fileName, IReadOnlyList<string> extensions) =>
        extensions.Contains(Path.GetExtension(fileName).ToLowerInvariant());
}

public sealed class UnsupportedPilotFileException : Exception
{
    public UnsupportedPilotFileException(string message)
        : base(message)
    {
    }
}

/// <summary>Registry of all supported formats.</summary>
public static class PilotFormats
{
    public static IReadOnlyList<PilotFormat> All { get; } = new PilotFormat[]
    {
        new XWing.XWingPilotFormat(XWing.XWingEdition.Windows98),
        new XWing.XWingPilotFormat(XWing.XWingEdition.Dos),
        new Tie.TiePilotFormat(),
        new XvT.XvtPilotFormat(),
        new XvT.BopPilotFormat(),
        new Xwa.XwaPilotFormat(),
        new Xwvm.XwvmPilotFormat(),
    };

    /// <summary>All extensions the editor understands, for file dialogs.</summary>
    public static IReadOnlyList<string> AllExtensions { get; } =
        All.SelectMany(f => f.Extensions).Distinct().ToArray();

    public static PilotFormat? Detect(string fileName, ReadOnlySpan<byte> data)
    {
        foreach (var format in All)
        {
            if (format.Matches(fileName, data))
            {
                return format;
            }
        }

        return null;
    }

    /// <summary>Opens a pilot file. Throws <see cref="UnsupportedPilotFileException"/> with a helpful message if it is not recognised.</summary>
    public static PilotDocument Open(string fileName, byte[] data, string? variantId = null)
    {
        var format = Detect(fileName, data);
        if (format is null)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var candidates = All.Where(f => f.Extensions.Contains(ext)).ToList();
            var message = candidates.Count == 0
                ? $"\"{Path.GetFileName(fileName)}\" is not a pilot file this editor knows. Supported extensions: {string.Join(", ", AllExtensions)}."
                : $"\"{Path.GetFileName(fileName)}\" ({data.Length:N0} bytes) does not match any known {ext} layout. " +
                  "To avoid damaging it, it was not opened. Expected: " +
                  string.Join("; ", candidates.Select(c => $"{c.Name}: {c.Signature}")) + ".";
            throw new UnsupportedPilotFileException(message);
        }

        return format.Open(fileName, data, variantId);
    }

    public static PilotDocument Open(string path) => Open(path, File.ReadAllBytes(path));
}

internal static class Choices
{
    /// <summary>Choices numbered 0..n-1.</summary>
    public static IReadOnlyList<ChoiceOption> Indexed(params string[] labels) =>
        labels.Select((l, i) => new ChoiceOption(i, l)).ToArray();

    public static IReadOnlyList<ChoiceOption> Of(params (long Value, string Label)[] options) =>
        options.Select(o => new ChoiceOption(o.Value, o.Label)).ToArray();

    public static IReadOnlyList<ChoiceOption> Numbered(int count, Func<int, string> label) =>
        Enumerable.Range(0, count).Select(i => new ChoiceOption(i, label(i))).ToArray();
}
