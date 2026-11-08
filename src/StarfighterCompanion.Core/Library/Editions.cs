using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XWing;

namespace StarfighterCompanion.Core.Library;

public enum GameFamily
{
    XWing,
    Tie,
}

/// <summary>
/// One retail edition of X-Wing or TIE Fighter. Pilot files of the editions of one game are close enough to be converted
/// into each other; what differs is listed here.
/// Edition names and add-ons follow XWVM's GameVariant list (Assets/Code/GameMetadata/Enums.cs).
/// </summary>
public sealed class Edition
{
    private Edition(string id, GameFamily family, string name, bool dosLayout, int nameMaxLength)
    {
        Id = id;
        Family = family;
        Name = name;
        DosLayout = dosLayout;
        NameMaxLength = nameMaxLength;
    }

    /// <summary>Also the id of the library variant for this edition.</summary>
    public string Id { get; }

    public GameFamily Family { get; }

    public string Name { get; }

    /// <summary>X-Wing only: the DOS record (1,706 bytes, one more reserved byte at 0x289).</summary>
    public bool DosLayout { get; }

    /// <summary>
    /// The pilot name is the file name. The DOS editions use 8.3 file names; the 1998 editions accept 16 characters
    /// (OpenXW register.h, OpenTIE register.c).
    /// </summary>
    public int NameMaxLength { get; }

    public string Extension => Family == GameFamily.XWing ? ".PLT" : ".TFR";

    public static Edition XWing93 { get; } = new("xwing93", GameFamily.XWing, "X-Wing 1993 floppy edition", dosLayout: true, nameMaxLength: 8);
    public static Edition XWing94 { get; } = new("xwing94", GameFamily.XWing, "X-Wing 1994 Collector's CD-ROM", dosLayout: true, nameMaxLength: 8);
    public static Edition XWing98 { get; } = new("xwing98", GameFamily.XWing, "X-Wing 1998 Collector Series (Windows)", dosLayout: false, nameMaxLength: 16);
    public static Edition Tie94 { get; } = new("tie94", GameFamily.Tie, "TIE Fighter 1994 floppy edition", dosLayout: false, nameMaxLength: 8);
    public static Edition Tie95 { get; } = new("tie95", GameFamily.Tie, "TIE Fighter 1995 Collector's CD-ROM", dosLayout: false, nameMaxLength: 8);
    public static Edition Tie98 { get; } = new("tie98", GameFamily.Tie, "TIE Fighter 1998 Collector Series (Windows)", dosLayout: false, nameMaxLength: 16);

    public static IReadOnlyList<Edition> All { get; } = new[] { XWing93, XWing94, XWing98, Tie94, Tie95, Tie98 };

    public static Edition? Find(string? id) => All.FirstOrDefault(e => e.Id == id);

    public IEnumerable<Edition> Siblings => All.Where(e => e.Family == Family);

    /// <summary>
    /// The edition a pilot file belongs to: the library version it was found in, or failing that its layout.
    /// OpenXW uses the 1998 layout and OpenTIE the CD-ROM one.
    /// </summary>
    public static Edition? For(string? variantId, PilotFormat format)
    {
        var byVariant = Find(variantId);
        if (byVariant is not null && byVariant.Accepts(format))
        {
            return byVariant;
        }

        return format switch
        {
            XWingPilotFormat { RecordSize: XWingPilotFormat.DosRecordSize } => XWing94,
            XWingPilotFormat => XWing98,
            TiePilotFormat => Tie95,
            _ => null,
        };
    }

    public bool Accepts(PilotFormat format) => format switch
    {
        XWingPilotFormat x => Family == GameFamily.XWing && (x.RecordSize == XWingPilotFormat.DosRecordSize) == DosLayout,
        TiePilotFormat => Family == GameFamily.Tie,
        _ => false,
    };

    /// <summary>Returns why a pilot name cannot be used as a file name in this edition, or null.</summary>
    public string? CheckPilotName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Enter a name.";
        }

        if (name.Length > NameMaxLength)
        {
            return $"{Name} pilot names can be at most {NameMaxLength} characters.";
        }

        // The registration desk accepts letters (stored in upper case), digits, '_' and '-' for file names.
        if (!name.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_' or '-'))
        {
            return "Only letters, digits, '_' and '-' can be used.";
        }

        return null;
    }

    /// <summary>A valid pilot name for this edition based on <paramref name="name"/>.</summary>
    public string SuggestPilotName(string name)
    {
        var cleaned = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-').ToArray()).ToUpperInvariant();
        if (cleaned.Length == 0)
        {
            cleaned = "PILOT";
        }

        return cleaned.Length > NameMaxLength ? cleaned[..NameMaxLength] : cleaned;
    }

    // ---- What each edition contains -------------------------------------------------------------------------------

    /// <summary>X-Wing: tours in the base game. The 1993 floppy has 3; Imperial Pursuit (tour 4) and B-Wing (tour 5) were add-on disks.</summary>
    public int BaseTours => Id == XWing93.Id ? 3 : 5;

    /// <summary>
    /// TIE Fighter: battles in the base game. The 1994 floppy has 7, plus 8-10 with the Defender of the Empire add-on;
    /// battles 11-13 (Enemies of the Empire) only exist on the CD-ROM and 1998 editions.
    /// </summary>
    public int BaseBattles => Id == Tie94.Id ? 7 : 13;

    /// <summary>TIE Fighter: battles that can exist at all, add-ons included.</summary>
    public int MaxBattles => Id == Tie94.Id ? 10 : 13;

    public override string ToString() => Name;
}
