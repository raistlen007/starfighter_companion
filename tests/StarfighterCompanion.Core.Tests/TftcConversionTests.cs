using System.Text;
using StarfighterCompanion.Core.Conversion;
using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.Xwa;
using StarfighterCompanion.Core.Library;
using Xunit;

namespace StarfighterCompanion.Core.Tests;

public class TftcConversionTests
{
    private static byte[] TftcPilot(string name = "Vader")
    {
        var data = new byte[XwaPilotFormat.RecordSize];
        Encoding.ASCII.GetBytes(name).CopyTo(data, 0);
        var doc = PilotFormats.Open("Vader0.plt", data, "tftc");
        doc.Field("tod[3].completed").TrySetInteger(1); // pilot training, not part of TIE Fighter
        doc.Field("main.killsByCraft[2][40]").TrySetInteger(4); // corvettes outside the campaign
        return doc.Save();
    }

    [Fact]
    public void Tie_career_is_written_into_a_tftc_pilot()
    {
        var result = TftcBridge.FromTie(ConversionTests.TiePilot(), TftcPilot());
        var doc = PilotFormats.Open("Vader0.plt", result.Data, "tftc");

        // Battle 1 (all six missions, pilot file numbers 9-14) and the first two missions of battle 12 (74, 75).
        Assert.All(new[] { 9, 10, 11, 12, 13, 14, 74, 75 }, i => Assert.Equal(1, doc.Int($"tod[{i}].completed")));
        Assert.Equal(0, doc.Int("tod[76].completed"));
        Assert.Equal(4000, doc.Int("tod[14].bestScore"));

        // Combat chamber: TIE Interceptor mission 3 is "Clear Minefield" (number 11).
        Assert.Equal(888, doc.Int("combat[11].bestScore"));
        Assert.Equal(1, doc.Int("combat[11].completed"));

        Assert.Equal(10, doc.Int("main.killsByCraft[0][1]"));
        Assert.Equal(10, doc.Int("faction[0].stats.killsByCraft[0][1]"));
        Assert.Equal(500, doc.Int("main.energyFired[0]"));

        // Kept: TFTC's own pilot training and statistics outside the campaign.
        Assert.Equal(1, doc.Int("tod[3].completed"));
        Assert.Equal(4, doc.Int("main.killsByCraft[2][40]"));
        Assert.Equal("Vader", doc.PilotName);
    }

    [Fact]
    public void Tie_round_trips_through_tftc()
    {
        var original = PilotFormats.Open("VADER.TFR", ConversionTests.TiePilot());
        var tftc = TftcBridge.FromTie(ConversionTests.TiePilot(), TftcPilot()).Data;
        var back = PilotFormats.Open("VADER.TFR", TftcBridge.ToTie(tftc, Edition.Tie98).Data);

        foreach (var id in new[] { "battle.status[0]", "battle.mission[0]", "battle.score[0][5]", "battle.status[11]", "battle.mission[11]",
                                   "battle.current", "combat.score[1][2]", "combat.complete[1][2]", "stats.killsByType[0]", "stats.laserShots",
                                   "stats.laserHits", "stats.ejections" })
        {
            Assert.Equal(original.Int(id), back.Int(id));
        }

        // TFTC's corvette kills outside the campaign come along too.
        Assert.Equal(4, back.Int("stats.killsByType[39]"));
    }

    [Fact]
    public void Tftc_pilot_goes_to_xwvm_and_back()
    {
        var tftc = TftcBridge.FromTie(ConversionTests.TiePilot(), TftcPilot()).Data;
        var vm = TftcBridge.ToXwvm(tftc, null, "Vader");
        var again = TftcBridge.FromXwvm(vm.Data, TftcPilot()).Data;
        var a = PilotFormats.Open("a.plt", tftc, "tftc");
        var b = PilotFormats.Open("b.plt", again, "tftc");
        Assert.Empty(a.Fields.Where(f => (f.Id.StartsWith("tod[") || f.Id.StartsWith("combat[")) && f.GetInteger() != b.Field(f.Id).GetInteger()));
    }

    [Fact]
    public void Only_tftc_classic_pilots_offer_tie_targets()
    {
        var plt = TftcPilot();
        var format = new XwaPilotFormat();
        Assert.Contains(PilotConverter.TargetsFor(format, null, plt, "tftc"), t => t.Edition == Edition.Tie98);
        Assert.Contains(PilotConverter.TargetsFor(format, null, plt, "tftc"), t => t.IsXwvm);
        Assert.Empty(PilotConverter.TargetsFor(format, null, plt, "tftcr"));
        Assert.Empty(PilotConverter.TargetsFor(format, null, plt, "xwaup"));
        Assert.Contains(PilotConverter.TargetsFor(new TiePilotFormat(), Edition.Tie95, ConversionTests.TiePilot()), t => t.IsTftc);
    }

    [Fact]
    public void Converting_into_tftc_creates_or_updates_the_pilot()
    {
        var dir = Directory.CreateTempSubdirectory("sfc-tftc").FullName;
        try
        {
            var target = PilotConverter.TargetsFor(new TiePilotFormat(), Edition.Tie98, ConversionTests.TiePilot()).Single(t => t.IsTftc);

            var created = PilotConverter.Plan(ConversionTests.TiePilot(), new TiePilotFormat(), Edition.Tie98, target, "Vader", dir);
            Assert.Equal(Path.Combine(dir, "Vader0.plt"), created.Path);
            Assert.False(created.Overwrites);
            var doc = PilotFormats.Open("Vader0.plt", created.Result.Data, "tftc");
            Assert.Equal("Vader", doc.PilotName);
            Assert.Equal(1, doc.Int("tod[9].completed"));

            File.WriteAllBytes(Path.Combine(dir, "Vader0.plt"), TftcPilot());
            var updated = PilotConverter.Plan(ConversionTests.TiePilot(), new TiePilotFormat(), Edition.Tie98, target, "vader", dir);
            Assert.Equal(Path.Combine(dir, "Vader0.plt"), updated.Path);
            Assert.True(updated.Overwrites);

            Assert.NotNull(PilotConverter.CheckName(target, "AVeryLongPilotName"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_new_tftc_pilot_is_set_up_like_the_game_does()
    {
        var data = TftcBridge.NewPilot("Maarek");
        var doc = PilotFormats.Open("Maarek0.plt", data, "tftc");
        Assert.Equal("Maarek", doc.PilotName);
        Assert.Equal(2, doc.Int("rating"));
        Assert.Equal("Civilian", doc.Field("ratingName").GetText());
        Assert.Equal(3, doc.Int("skipsRemaining"));
        Assert.Equal("Maarek's Game", doc.Field("gameName").GetText());
        Assert.Equal(1, doc.Int("craftKnown[5]")); // TIE Fighter
        Assert.Empty(doc.Validate().Where(i => i.Severity == Model.IssueSeverity.Error));

        // Selected missions (OpenXWA Pilot_CreateNew): the first campaign mission (3) and the first melee mission (58).
        int I32(int offset) => BitConverter.ToInt32(data, offset);
        var gameName = XwaPilotFormat.Schema.Fields.Single(f => f.Id == "gameName").Offset;
        Assert.Equal(58, I32(gameName - 28));
        Assert.Equal(3, I32(gameName - 28 + 16));
        var faction0 = XwaPilotFormat.Schema.Fields.Single(f => f.Id == "faction[0].missionsPlayed").Offset;
        Assert.Equal(4, I32(faction0 + 8));
        Assert.Equal(3, I32(faction0 + 12 + 16));
    }
}
