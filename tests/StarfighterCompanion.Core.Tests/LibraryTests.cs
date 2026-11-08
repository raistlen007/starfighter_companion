using System.Text;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XvT;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Library;

namespace StarfighterCompanion.Core.Tests;

public sealed class LibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sfc-tests-" + Guid.NewGuid().ToString("N"));

    public LibraryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData("STAR WARS X-Wing", "XWing")]
    [InlineData("Star Wars - X-Wing (1994)", "XWing")]
    [InlineData("XWING", "XWing")]
    [InlineData("STAR WARS TIE Fighter", "Tie")]
    [InlineData("TIE", "Tie")]
    [InlineData("STAR WARS X-Wing vs TIE Fighter", "XvT")]
    [InlineData("Star Wars - X-Wing vs. TIE Fighter", "XvT")]
    [InlineData("STAR WARS X-Wing Alliance", "Xwa")]
    [InlineData("XWA", "Xwa")]
    [InlineData("TFTC", "Tftc")]
    [InlineData("TIE Fighter Total Conversion", "Tftc")]
    [InlineData("Program Files", "None")]
    [InlineData("Windows", "None")]
    public void Install_folders_are_recognised_by_name(string name, string expected) =>
        Assert.Equal(expected, InstallFinder.Classify(name).ToString());

    [Fact]
    public void Scanner_lists_only_pilots_of_the_variant()
    {
        var game = Directory.CreateDirectory(Path.Combine(_root, "STAR WARS X-Wing")).FullName;
        File.WriteAllBytes(Path.Combine(game, "LUKE.PLT"), new byte[XWingPilotFormat.WindowsRecordSize * 2]);
        Directory.CreateDirectory(Path.Combine(game, "dos"));
        File.WriteAllBytes(Path.Combine(game, "dos", "WEDGE.PLT"), new byte[XWingPilotFormat.DosRecordSize]);
        File.WriteAllBytes(Path.Combine(game, "BROKEN.PLT"), new byte[100]);
        File.WriteAllBytes(Path.Combine(game, "LUKE.PLT.bak"), new byte[XWingPilotFormat.WindowsRecordSize]);

        var win = PilotScanner.Scan(GameCatalog.Variant("xwing98"), game);
        var dos = PilotScanner.Scan(GameCatalog.Variant("xwingdos"), game);
        Assert.Equal(new[] { "LUKE" }, win.Select(p => p.Name));
        Assert.Equal(new[] { "WEDGE" }, dos.Select(p => p.Name));
    }

    [Fact]
    public void Scanner_reads_names_stored_inside_the_file()
    {
        var data = new byte[XvtPilotFormat.RecordSize];
        Encoding.ASCII.GetBytes("Keyan").CopyTo(data, 0);
        File.WriteAllBytes(Path.Combine(_root, "Keyan0.plt"), data);

        var pilots = PilotScanner.Scan(GameCatalog.Variant("xvt"), _root);
        Assert.Equal("Keyan", Assert.Single(pilots).Name);
    }

    [Fact]
    public void Detection_finds_games_in_extra_folders()
    {
        Directory.CreateDirectory(Path.Combine(_root, "STAR WARS TIE Fighter"));
        Directory.CreateDirectory(Path.Combine(_root, "STAR WARS X-Wing vs TIE Fighter", "BalanceOfPower"));

        var finder = new InstallFinder();
        finder.ExtraSearchRoots.Add(_root);
        var found = finder.DetectAll();

        Assert.Contains(found, d => d.VariantId == "tie" && d.Folder.EndsWith("STAR WARS TIE Fighter"));
        Assert.Contains(found, d => d.VariantId == "xvt");
        Assert.Contains(found, d => d.VariantId == "bop" && d.Folder.EndsWith("BalanceOfPower"));
    }

    [Fact]
    public void Both_x_wing_editions_are_found_by_their_files()
    {
        // Layout like the Steam/GOG release: one install holding the 1998 edition and the 1994 DOS edition.
        var install = Path.Combine(_root, "STAR WARS X-Wing");
        var win98 = Directory.CreateDirectory(Path.Combine(install, "XWING98")).FullName;
        Directory.CreateDirectory(Path.Combine(win98, "X-Wing Data", "MISSION"));
        File.WriteAllBytes(Path.Combine(win98, "XWING95.EXE"), Array.Empty<byte>());
        var dos = Directory.CreateDirectory(Path.Combine(install, "XWING94")).FullName;
        File.WriteAllBytes(Path.Combine(dos, "game.gog"), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(dos, "BWING.EXE"), Array.Empty<byte>());

        var finder = new InstallFinder();
        finder.ExtraSearchRoots.Add(_root);
        var found = finder.DetectAll();

        Assert.Equal(win98, found.Single(d => d.VariantId == "xwing98").Folder);
        Assert.Equal(dos, found.Single(d => d.VariantId == "xwingdos").Folder);
    }

    private static string Touch(string folder, params string[] entries)
    {
        Directory.CreateDirectory(folder);
        foreach (var entry in entries)
        {
            var path = Path.Combine(folder, entry);
            if (Path.HasExtension(entry))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, Array.Empty<byte>());
            }
            else
            {
                Directory.CreateDirectory(path);
            }
        }

        return Path.GetFullPath(folder);
    }

    [Fact]
    public void Tie_windows_edition_is_preferred_over_the_dos_edition()
    {
        var install = Path.Combine(_root, "STAR WARS TIE Fighter Special Edition");
        Touch(Path.Combine(install, "TIE95"), "MISSION", "RESOURCE", "Z_TIE__.EXE", "TIE.EXE", "game.gog");
        var win98 = Touch(Path.Combine(install, "TIE98"), "MISSION", "RESOURCE", "TIE95.EXE");

        var finder = new InstallFinder();
        finder.ExtraSearchRoots.Add(_root);

        Assert.Equal(win98, finder.DetectAll().Single(d => d.VariantId == "tie").Folder);
    }

    [Fact]
    public void Tie_editions_are_told_apart_by_their_files()
    {
        var finder = new InstallFinder();
        var tie98 = Touch(Path.Combine(_root, "a"), "MISSION", "RESOURCE", "TIE95.EXE");
        var tie95 = Touch(Path.Combine(_root, "b"), "mission", "resource", "z_tie__.exe", "tie.exe");
        var tie94 = Touch(Path.Combine(_root, "c"), "MISSION", "RESOURCE", "TIE.EXE", "FLIGHT.OVL");
        var both = Touch(Path.Combine(_root, "d"), "MISSION", "RESOURCE", "TIE95.EXE", "TIE.EXE");

        Assert.True(finder.IsTie98Folder(tie98));
        Assert.False(finder.IsTie95Folder(tie98));
        Assert.True(finder.IsTie95Folder(tie95));
        Assert.False(finder.IsTie98Folder(tie95));
        Assert.True(finder.IsTie94Folder(tie94));
        Assert.False(finder.IsTie98Folder(both));
    }

    [Fact]
    public void X_wing_editions_are_told_apart_by_their_files()
    {
        var finder = new InstallFinder();
        var cd98 = Touch(Path.Combine(_root, "cd98"), "XWINGTIE.EXE", Path.Combine("XWINGCD", "XWING95.EXE"), Path.Combine("XWINGCD", "X-Wing Data", "MISSION"));
        var digital98 = Touch(Path.Combine(_root, "dig98"), "XWING95.EXE", Path.Combine("X-Wing Data", "MISSION"));
        var cd94 = Touch(Path.Combine(_root, "cd94"), "MISSION", "RESOURCE", "CLASSIC", "TALK", "BWING.EXE", "BFLIGHT.OVL");
        var floppy93 = Touch(Path.Combine(_root, "fl93"), "MISSION", "RESOURCE", "XWING.EXE", "FLIGHT.OVL");
        var notDos = Touch(Path.Combine(_root, "x"), "MISSION", "RESOURCE", "CLASSIC", "XWING.EXE", "FLIGHT.OVL");

        Assert.True(finder.IsXWing98Folder(cd98));
        Assert.True(finder.IsXWing98Folder(digital98));
        Assert.False(finder.IsXWing98Folder(cd94));
        Assert.True(finder.IsXWingDosFolder(cd94));
        Assert.True(finder.IsXWingDosFolder(floppy93));
        Assert.False(finder.IsXWingDosFolder(notDos));
        Assert.False(finder.IsXWingDosFolder(digital98));
    }

    [Theory]
    [InlineData("xwing98_steam", "xwing98")]
    [InlineData("xwing98_gog", "xwing98")]
    [InlineData("xwing94_gog_german", "xwingdos")]
    [InlineData("xwing93", "xwingdos")]
    [InlineData("tie95_steam", "tie")]
    [InlineData("tie98", "tie")]
    [InlineData("none", null)]
    public void Xwvm_game_variants_map_to_editor_versions(string xwvmVariant, string? expected) =>
        Assert.Equal(expected, InstallFinder.MapXwvmVariant(xwvmVariant));

    [Fact]
    public void User_choice_beats_detection_and_survives_reload()
    {
        var settingsPath = Path.Combine(_root, "settings.json");
        var settings = LibrarySettings.Load(settingsPath);
        var library = new GameLibrary(settings);
        library.ApplyDetection(new[] { new DetectedFolder("tie", "/detected/tie", "Steam") });

        Assert.Equal("/detected/tie", library.FolderFor("tie"));
        Assert.True(library.IsAutoDetected("tie"));

        settings.SetFolder("tie", "/my/tie");
        settings.SetFolder("xwa", "");
        settings.Save();

        var reloaded = new GameLibrary(LibrarySettings.Load(settingsPath));
        reloaded.ApplyDetection(new[] { new DetectedFolder("tie", "/detected/tie", "Steam"), new DetectedFolder("xwa", "/detected/xwa", "Steam") });
        Assert.Equal("/my/tie", reloaded.FolderFor("tie"));
        Assert.Null(reloaded.FolderFor("xwa"));

        reloaded.Settings.UseAutoDetection("xwa");
        Assert.Equal("/detected/xwa", reloaded.FolderFor("xwa"));
    }

    [Fact]
    public void Every_variant_accepts_at_least_one_format()
    {
        foreach (var variant in GameCatalog.AllVariants)
        {
            Assert.True(Formats.PilotFormats.All.Any(variant.Accepts), variant.Id);
        }

        Assert.True(GameCatalog.Variant("tie").Accepts(new TiePilotFormat()));
    }
}
