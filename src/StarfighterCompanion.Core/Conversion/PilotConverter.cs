using System.Xml;
using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Formats.Xwvm;
using StarfighterCompanion.Core.Library;

namespace StarfighterCompanion.Core.Conversion;

/// <summary>Something a pilot can be converted into.</summary>
public sealed record ConversionTarget(string Label, Edition? Edition)
{
    /// <summary>The target is XWVM (Edition is null).</summary>
    public bool IsXwvm => Edition is null;

    /// <summary>The library version whose folder the new file goes to.</summary>
    public string VariantId => Edition?.Id ?? GameCatalog.Xwvm;

    public override string ToString() => Label;
}

/// <summary>A conversion ready to be written: the bytes, where they go, and what the user should know.</summary>
public sealed record ConversionPlan(ConversionResult Result, string Path, bool Overwrites, string Summary);

/// <summary>Ties the edition converter and the XWVM bridge to pilot files on disk.</summary>
public static class PilotConverter
{
    /// <summary>The editions and games a pilot file can be converted into.</summary>
    public static IReadOnlyList<ConversionTarget> TargetsFor(PilotFormat format, Edition? source, byte[] data)
    {
        var targets = new List<ConversionTarget>();
        switch (format)
        {
            case XWingPilotFormat or TiePilotFormat when source is not null:
                targets.AddRange(source.Siblings.Where(e => e != source).Select(e => new ConversionTarget(e.Name, e)));
                targets.Add(new ConversionTarget("XWVM", null));
                break;

            case XwvmPilotFormat:
                foreach (var family in XwvmBridge.ExportableGames(data))
                {
                    targets.AddRange(Edition.All.Where(e => e.Family == family).Select(e => new ConversionTarget(e.Name, e)));
                }

                break;
        }

        return targets;
    }

    /// <summary>The name the new file would get by default.</summary>
    public static string SuggestName(PilotFormat format, string pilotName, ConversionTarget target, byte[] data)
    {
        var name = format is XwvmPilotFormat ? XwvmBridge.PilotNameOf(data) : pilotName;
        return target.Edition is { } edition ? edition.SuggestPilotName(name) : name;
    }

    /// <summary>Returns why <paramref name="name"/> cannot be used for the target, or null.</summary>
    public static string? CheckName(ConversionTarget target, string name)
    {
        if (target.Edition is { } edition)
        {
            return edition.CheckPilotName(name);
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a name.";
        }

        return name.Length > 64 ? "Use a shorter name." : null;
    }

    /// <summary>Converts the pilot and works out the file it goes to. Nothing is written.</summary>
    public static ConversionPlan Plan(byte[] data, PilotFormat format, Edition? source, ConversionTarget target, string name, string folder)
    {
        var nameError = CheckName(target, name);
        if (nameError is not null)
        {
            throw new ArgumentException(nameError, nameof(name));
        }

        if (target.IsXwvm)
        {
            if (source is null)
            {
                throw new ArgumentException("The pilot's edition is not known.", nameof(source));
            }

            var existing = FindXwvmPilot(folder, name);
            var path = existing ?? System.IO.Path.Combine(folder, XwvmBridge.VmpilotFileName(name));
            if (existing is null && File.Exists(path))
            {
                throw new IOException($"{System.IO.Path.GetFileName(path)} already exists in the XWVM folder but belongs to another pilot.");
            }

            var result = XwvmBridge.ToXwvm(data, source, existing is null ? null : File.ReadAllBytes(existing), name);
            var game = source.Family == GameFamily.XWing ? "X-Wing" : "TIE Fighter";
            var summary = existing is null
                ? $"A new XWVM pilot \"{name}\" will be created with this {game} career."
                : $"XWVM already has a pilot named \"{name}\". Its {game} career will be replaced with this one; its other games stay as they are.";
            return new ConversionPlan(result, path, existing is not null, summary);
        }

        var edition = target.Edition!;
        var output = format is XwvmPilotFormat
            ? XwvmBridge.FromXwvm(data, edition)
            : EditionConverter.Convert(data, source ?? throw new ArgumentException("The pilot's edition is not known.", nameof(source)), edition);
        var file = ExistingFile(folder, name + edition.Extension) ?? System.IO.Path.Combine(folder, name.ToUpperInvariant() + edition.Extension);
        var overwrites = File.Exists(file);
        var text = overwrites
            ? $"{System.IO.Path.GetFileName(file)} already exists for the {edition.Name} and will be replaced (a backup is kept)."
            : $"{System.IO.Path.GetFileName(file)} will be created for the {edition.Name}.";
        return new ConversionPlan(output, file, overwrites, text);
    }

    /// <summary>The .vmpilot file in <paramref name="folder"/> that holds the pilot called <paramref name="name"/>, if any.</summary>
    public static string? FindXwvmPilot(string folder, string name)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.vmpilot"))
        {
            try
            {
                using var stream = File.OpenRead(file);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
                reader.MoveToContent();
                // XWVM tells pilots apart by exact name.
                if (reader.LocalName == "PilotRecord" && reader.GetAttribute("Name") == name)
                {
                    return file;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
            {
                // Not readable: XWVM skips such files too.
            }
        }

        return null;
    }

    /// <summary>A file with this name in any letter case (the DOS games do not care about case).</summary>
    private static string? ExistingFile(string folder, string fileName)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        return Directory.EnumerateFiles(folder).FirstOrDefault(f => string.Equals(System.IO.Path.GetFileName(f), fileName, StringComparison.OrdinalIgnoreCase));
    }
}
