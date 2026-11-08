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
