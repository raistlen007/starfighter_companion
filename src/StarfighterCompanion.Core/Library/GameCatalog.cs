using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XvT;
using StarfighterCompanion.Core.Formats.Xwa;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Formats.Xwvm;

namespace StarfighterCompanion.Core.Library;

/// <summary>A game in the left-hand menu.</summary>
public sealed class GameDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<VariantDefinition> Variants { get; init; }
}

/// <summary>
/// One place pilots of a game can live: an edition, a mod or a reimplementation.
/// Each variant has its own folder setting.
/// </summary>
public sealed class VariantDefinition
{
    public required string Id { get; init; }
    public required string GameId { get; init; }
    public required string Name { get; init; }

    /// <summary>Short explanation shown in settings.</summary>
    public string? Description { get; init; }

    /// <summary>Which pilot files belong to this variant.</summary>
    public required Func<PilotFormat, bool> Accepts { get; init; }

    /// <summary>How many folder levels below the configured folder are searched for pilots.</summary>
    public int SearchDepth { get; init; } = 2;
}

/// <summary>All games and variants the editor knows about.</summary>
public static class GameCatalog
{
    public const string XWing = "xwing";
    public const string Tie = "tie";
    public const string XvT = "xvt";
    public const string Xwa = "xwa";
    public const string Xwvm = "xwvm";

    public static IReadOnlyList<GameDefinition> Games { get; } = new[]
    {
        new GameDefinition
        {
            Id = XWing,
            Name = "X-Wing",
            Variants = new[]
            {
                new VariantDefinition
                {
                    Id = "xwing98", GameId = XWing, Name = "1998 Windows edition", SearchDepth = 3,
                    Description = "The 1998 Collector Series, also the 1998 version included on Steam and GOG.",
                    Accepts = f => f is XWingPilotFormat { RecordSize: XWingPilotFormat.WindowsRecordSize },
                },
                new VariantDefinition
                {
                    Id = "xwingdos", GameId = XWing, Name = "1993 / 1994 DOS edition", SearchDepth = 3,
                    Description = "The original floppy and the 1994 Collector's CD-ROM, also included on Steam and GOG.",
                    Accepts = f => f is XWingPilotFormat { RecordSize: XWingPilotFormat.DosRecordSize },
                },
                new VariantDefinition
                {
                    Id = "openxw", GameId = XWing, Name = "OpenXW",
                    Description = "The OpenXW reimplementation keeps its pilots in its own user folder.",
                    Accepts = f => f is XWingPilotFormat,
                },
            },
        },
        new GameDefinition
        {
            Id = Tie,
            Name = "TIE Fighter",
            Variants = new[]
            {
                new VariantDefinition
                {
                    Id = "tie", GameId = Tie, Name = "TIE Fighter", SearchDepth = 3,
                    Description = "Floppy, 1995 CD-ROM or 1998 Windows edition (Steam and GOG include both).",
                    Accepts = f => f is TiePilotFormat,
                },
                new VariantDefinition
                {
                    Id = "opentie", GameId = Tie, Name = "OpenTIE",
                    Description = "The OpenTIE reimplementation keeps its pilots in its own user folder.",
                    Accepts = f => f is TiePilotFormat,
                },
            },
        },
        new GameDefinition
        {
            Id = XvT,
            Name = "X-Wing vs. TIE Fighter",
            Variants = new[]
            {
                new VariantDefinition
                {
                    Id = "xvt", GameId = XvT, Name = "X-Wing vs. TIE Fighter",
                    Accepts = f => f is XvtPilotFormat,
                    SearchDepth = 0,
                },
                new VariantDefinition
                {
                    Id = "bop", GameId = XvT, Name = "Balance of Power",
                    Description = "Usually the BalanceOfPower folder inside the XvT installation.",
                    Accepts = f => f is BopPilotFormat,
                    SearchDepth = 1,
                },
                new VariantDefinition
                {
                    Id = "openxvt", GameId = XvT, Name = "OpenXvT",
                    Description = "The OpenXvT reimplementation keeps its pilots in its own user folder.",
                    Accepts = f => f is XvtPilotFormat or BopPilotFormat,
                },
            },
        },
        new GameDefinition
        {
            Id = Xwa,
            Name = "X-Wing Alliance",
            Variants = new[]
            {
                new VariantDefinition
                {
                    Id = "xwa", GameId = Xwa, Name = "X-Wing Alliance",
                    Accepts = f => f is XwaPilotFormat,
                    SearchDepth = 0,
                },
                new VariantDefinition
                {
                    Id = "xwaup", GameId = Xwa, Name = "X-Wing Alliance Upgrade (XWAUP)",
                    Description = "An XWA installation with the XWA Upgrade applied.",
                    Accepts = f => f is XwaPilotFormat,
                    SearchDepth = 0,
                },
                new VariantDefinition
                {
                    Id = "tftc", GameId = Xwa, Name = "TIE Fighter Total Conversion (TFTC)",
                    Description = "The TFTC installation (a separate copy of XWA).",
                    Accepts = f => f is XwaPilotFormat,
                    SearchDepth = 0,
                },
                new VariantDefinition
                {
                    Id = "openxwa", GameId = Xwa, Name = "OpenXWA",
                    Description = "The OpenXWA reimplementation keeps its pilots in its own user folder.",
                    Accepts = f => f is XwaPilotFormat,
                },
            },
        },
        new GameDefinition
        {
            Id = Xwvm,
            Name = "XWVM",
            Variants = new[]
            {
                new VariantDefinition
                {
                    Id = "xwvm", GameId = Xwvm, Name = "XWVM",
                    Description = "XWVM keeps its .vmpilot files in its user folder: %APPDATA%\\XWVM\\pilots on Windows, ~/.config/XWVM/pilots on Linux and macOS.",
                    Accepts = f => f is XwvmPilotFormat,
                    SearchDepth = 0,
                },
            },
        },
    };

    public static IEnumerable<VariantDefinition> AllVariants => Games.SelectMany(g => g.Variants);

    public static VariantDefinition Variant(string id) => AllVariants.First(v => v.Id == id);

    public static GameDefinition Game(string id) => Games.First(g => g.Id == id);
}
