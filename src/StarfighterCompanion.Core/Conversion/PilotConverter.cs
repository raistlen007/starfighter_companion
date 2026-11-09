using System.Xml;
using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Formats.Xwa;
using StarfighterCompanion.Core.Formats.Xwvm;
using StarfighterCompanion.Core.Library;

namespace StarfighterCompanion.Core.Conversion;

/// <summary>Something a pilot can be converted into.</summary>
public sealed record ConversionTarget(string Label, Edition? Edition, bool IsTftc = false)
{
    /// <summary>The target is XWVM (no edition, not TFTC).</summary>
    public bool IsXwvm => Edition is null && !IsTftc;

    /// <summary>The library version whose folder the new file goes to.</summary>
    public string VariantId => IsTftc ? TftcBridge.VariantId : Edition?.Id ?? GameCatalog.Xwvm;

    internal static ConversionTarget Tftc { get; } = new("TIE Fighter Total Conversion Classic (into an existing pilot)", null, IsTftc: true);

    public override string ToString() => Label;
}

/// <summary>A conversion ready to be written: the bytes, where they go, and what the user should know.</summary>
public sealed record ConversionPlan(ConversionResult Result, string Path, bool Overwrites, string Summary);

/// <summary>Ties the edition converter and the XWVM bridge to pilot files on disk.</summary>
public static class PilotConverter
{
    /// <summary>The editions and games a pilot file can be converted into.</summary>
    /// <param name="sourceVariant">The library version the pilot was opened from (tells TFTC pilots from other XWA pilots).</param>
    public static IReadOnlyList<ConversionTarget> TargetsFor(PilotFormat format, Edition? source, byte[] data, string? sourceVariant = null)
    {
        var targets = new List<ConversionTarget>();
        switch (format)
        {
            case XWingPilotFormat or TiePilotFormat when source is not null:
                targets.AddRange(source.Siblings.Where(e => e != source).Select(e => new ConversionTarget(e.Name, e)));
                targets.Add(new ConversionTarget("XWVM", null));
                if (source.Family == GameFamily.Tie)
                {
                    targets.Add(ConversionTarget.Tftc);
                }

                break;

            case XwvmPilotFormat:
                foreach (var family in XwvmBridge.ExportableGames(data))
                {
                    targets.AddRange(Edition.All.Where(e => e.Family == family).Select(e => new ConversionTarget(e.Name, e)));
                    if (family == GameFamily.Tie)
                    {
                        targets.Add(ConversionTarget.Tftc);
                    }
                }

                break;

            case XwaPilotFormat when sourceVariant == TftcBridge.VariantId:
                targets.AddRange(Edition.All.Where(e => e.Family == GameFamily.Tie).Select(e => new ConversionTarget(e.Name, e)));
                targets.Add(new ConversionTarget("XWVM", null));
                break;
        }

        return targets;
    }

    /// <summary>The name the new file would get by default.</summary>
    public static string SuggestName(PilotFormat format, string pilotName, ConversionTarget target, byte[] data)
    {
        var name = format is XwvmPilotFormat ? XwvmBridge.PilotNameOf(data) : pilotName;
        if (target.IsTftc)
        {
            return name;
        }

        return target.Edition is { } edition ? edition.SuggestPilotName(name) : name;
    }

    /// <summary>Returns why <paramref name="name"/> cannot be used for the target, or null.</summary>
    public static string? CheckName(ConversionTarget target, string name)
    {
        if (target.Edition is { } edition)
        {
            return edition.CheckPilotName(name);
        }

        if (target.IsTftc)
        {
            return string.IsNullOrWhiteSpace(name) ? "Enter the name of the TFTC pilot to write into." : null;
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

        if (target.IsTftc)
        {
            var pilot = FindTftcPilot(folder, name)
                        ?? throw new ArgumentException($"There is no TFTC pilot named \"{name}\" in this folder. Create the pilot in TFTC first, then convert into it.");
            var existingPlt = File.ReadAllBytes(pilot);
            var converted = format is XwvmPilotFormat ? TftcBridge.FromXwvm(data, existingPlt) : TftcBridge.FromTie(data, existingPlt);
            return new ConversionPlan(converted, pilot, true,
                $"The TIE Fighter career will be written into the TFTC pilot \"{name}\" ({System.IO.Path.GetFileName(pilot)}); a backup is kept.");
        }

        if (format is XwaPilotFormat)
        {
            if (target.IsXwvm)
            {
                var existingVm = FindXwvmPilot(folder, name);
                var vmPath = existingVm ?? System.IO.Path.Combine(folder, XwvmBridge.VmpilotFileName(name));
                if (existingVm is null && File.Exists(vmPath))
                {
                    throw new IOException($"{System.IO.Path.GetFileName(vmPath)} already exists in the XWVM folder but belongs to another pilot.");
                }

                var vm = TftcBridge.ToXwvm(data, existingVm is null ? null : File.ReadAllBytes(existingVm), name);
                return new ConversionPlan(vm, vmPath, existingVm is not null, existingVm is null
                    ? $"A new XWVM pilot \"{name}\" will be created with this TIE Fighter career."
                    : $"XWVM already has a pilot named \"{name}\". Its TIE Fighter career will be replaced with this one; its other games stay as they are.");
            }

            var tie = target.Edition!;
            var tieFile = ExistingFile(folder, name + tie.Extension) ?? System.IO.Path.Combine(folder, name.ToUpperInvariant() + tie.Extension);
            var replaces = File.Exists(tieFile);
            return new ConversionPlan(TftcBridge.ToTie(data, tie), tieFile, replaces, replaces
                ? $"{System.IO.Path.GetFileName(tieFile)} already exists for the {tie.Name} and will be replaced (a backup is kept)."
                : $"{System.IO.Path.GetFileName(tieFile)} will be created for the {tie.Name}.");
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

    /// <summary>The TFTC pilot file in <paramref name="folder"/> whose stored name is <paramref name="name"/> (any case), if any.</summary>
    public static string? FindTftcPilot(string folder, string name)
    {
        if (!Directory.Exists(folder))
        {
            return null;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.plt").Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (new FileInfo(file).Length != XwaPilotFormat.RecordSize)
                {
                    continue;
                }

                var stored = PilotScanner.ReadName(file, File.ReadAllBytes(file), new XwaPilotFormat());
                if (string.Equals(stored, name, StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Not readable: skip it.
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
