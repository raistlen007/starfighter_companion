using System.Text;
using System.Xml.Linq;
using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Tests;

public class XwvmTests
{
    private const string Sample = """
        <?xml version="1.0" encoding="utf-8"?>
        <PilotRecord Name="Keyan Farlander">
          <PilotGameRecord Id="xwing" Rank="Lieutenant" Status="Alive" Difficulty="Medium" ActiveTour="tour2" CurrentScore="1200" AiScore="4000" AvatarIndex="-1" FutureAttribute="kept">
            <PilotAwardRecord Award="Kalidor" Level="2" />
            <TrainingRecord CraftType="XWing" HighScore="900" HighestLevelCompleted="4" />
            <PilotTourRecord TourId="tour1" CurrentTourStep="2147483647">
              <PilotOperationRecord TourStep="0" Score="300" Complete="true" />
            </PilotTourRecord>
            <PilotTourRecord TourId="tour2" CurrentTourStep="3">
              <PilotOperationRecord TourStep="0" Score="100" Attempts="2" />
            </PilotTourRecord>
            <EntityVictoryRecord Id="TIEFighter" Kills="12" Captures="0" />
            <StatRecord Id="LasersFired" Value="5000" />
            <SomethingNew Value="1" />
          </PilotGameRecord>
          <PilotGameRecord Id="tf" Rank="FltCadet" Status="Dead" Difficulty="Hard" CurrentScore="0" AiScore="0" AvatarIndex="-1" />
        </PilotRecord>
        """;

    private static PilotDocument Open() => PilotFormats.Open("Keyan.vmpilot", Encoding.UTF8.GetBytes(Sample));

    [Fact]
    public void Reads_values()
    {
        var doc = Open();
        Assert.Equal("Keyan Farlander", doc.PilotName);
        Assert.Equal(2, doc.Int("game[xwing].rank"));
        Assert.Equal(2, doc.Int("game[tf].status"));
        Assert.Equal(2, doc.Int("game[xwing].activeTour"));
        Assert.Equal(1, doc.Int("game[xwing].tour[tour1].completed"));
        Assert.Equal(3, doc.Int("game[xwing].tour[tour2].step"));
        Assert.Equal(12, doc.Int("game[xwing].victory[TIEFighter].kills"));
        Assert.True(doc.CanSave(out _));
    }

    [Fact]
    public void Edits_keep_unknown_content()
    {
        var doc = Open();
        Assert.Null(doc.Field("game[xwing].rank").TrySetInteger(5));
        Assert.Null(doc.Field("game[tf].status").TrySetInteger(0));
        Assert.Null(doc.Field("game[xwing].tour[tour2].op[0].attempts").TrySetInteger(0));

        var xml = XDocument.Parse(Encoding.UTF8.GetString(doc.Save()).TrimStart('﻿'));
        var xwing = xml.Root!.Elements("PilotGameRecord").First();
        Assert.Equal("General", (string?)xwing.Attribute("Rank"));
        Assert.Equal("kept", (string?)xwing.Attribute("FutureAttribute"));
        Assert.NotNull(xwing.Element("SomethingNew"));
        Assert.Equal("Alive", (string?)xml.Root.Elements("PilotGameRecord").Last().Attribute("Status"));
    }

    [Fact]
    public void Active_tour_can_only_be_a_started_tour()
    {
        var doc = Open();
        var active = doc.Field("game[xwing].activeTour");
        Assert.Equal(6, active.Choices!.Count); // none + X-Wing's five tours
        Assert.Null(active.TrySetInteger(3));   // tour3 is not started:
        Assert.False(doc.CanSave(out var issues));
        Assert.Contains(issues, i => i.Message.Contains("not started"));
        Assert.Null(active.TrySetInteger(0));

        var xml = XDocument.Parse(Encoding.UTF8.GetString(doc.Save()).TrimStart('﻿'));
        Assert.Null(xml.Root!.Element("PilotGameRecord")!.Attribute("ActiveTour"));
    }

    [Fact]
    public void Completing_and_reopening_a_tour_restores_the_step()
    {
        var doc = Open();
        var completed = doc.Field("game[xwing].tour[tour2].completed");
        var step = doc.Field("game[xwing].tour[tour2].step");
        Assert.Null(completed.TrySetInteger(1));
        Assert.Equal(int.MaxValue, step.GetInteger());
        Assert.Null(completed.TrySetInteger(0));
        Assert.Equal(3, step.GetInteger());
    }

    [Fact]
    public void Missing_active_tour_record_is_reported()
    {
        var broken = Sample.Replace("ActiveTour=\"tour2\"", "ActiveTour=\"tour9\"");
        var doc = PilotFormats.Open("Keyan.vmpilot", Encoding.UTF8.GetBytes(broken));
        var issues = doc.Validate();
        Assert.Contains(issues, i => i.Message.Contains("tour9"));
    }

    [Fact]
    public void Empty_name_is_refused()
    {
        var doc = Open();
        Assert.Null(doc.Field("name").TrySetText(""));
        Assert.False(doc.CanSave(out _));
    }

    [Fact]
    public void Everything_xwvm_reads_is_listed_even_when_missing()
    {
        var doc = Open();

        // X-Wing: all five tours with all their operations, historical groups, all four training craft, the Kalidor Crescent.
        Assert.Equal(0, doc.Int("game[xwing].tour[tour5].started"));
        Assert.True(doc.TryGetField("game[xwing].tour[tour5].op[19].score", out _));
        Assert.True(doc.TryGetField("game[xwing].historic[bcombat].op[5].complete", out _));
        Assert.True(doc.TryGetField("game[xwing].training[BWing].level", out _));
        Assert.Equal(-1, doc.Int("game[xwing].award[kalidor]"));
        Assert.True(doc.TryGetField("game[xwing].stat[SurfaceVictories]", out _));

        // TIE Fighter: 13 battles with their missions, secret order, objective pins; xvt and xwa records are expected too.
        Assert.True(doc.TryGetField("game[tf].tour[battle13].op[7].bonus", out _));
        Assert.False(doc.TryGetField("game[tf].tour[battle13].op[8].bonus", out _));
        Assert.True(doc.TryGetField("game[tf].award[secretOrderRank]", out _));
        Assert.True(doc.TryGetField("game[tf].victory[TIEDefender].kills", out _));
        Assert.True(doc.TryGetField("game[xwa].rank", out _));

        // Unknown records already in the file are kept and shown.
        Assert.Equal(5000, doc.Int("game[xwing].stat[LasersFired]"));
    }

    [Fact]
    public void Records_are_created_only_when_a_value_is_set()
    {
        var doc = Open();
        var untouched = Encoding.UTF8.GetString(doc.Save());

        Assert.Null(doc.Field("game[xwing].tour[tour3].op[2].score").TrySetInteger(1234));
        Assert.Null(doc.Field("game[tf].award[secretOrderRank]").TrySetInteger(2));
        Assert.Null(doc.Field("game[xwing].training[YWing].level").TrySetInteger(8));
        Assert.Null(doc.Field("game[xwa].difficulty").TrySetInteger(2));

        var xml = XDocument.Parse(Encoding.UTF8.GetString(doc.Save()).TrimStart('\uFEFF')).Root!;
        var xwing = xml.Elements("PilotGameRecord").Single(e => (string?)e.Attribute("Id") == "xwing");
        var tour3 = xwing.Elements("PilotTourRecord").Single(t => (string?)t.Attribute("TourId") == "tour3");
        Assert.Equal("0", (string?)tour3.Attribute("CurrentTourStep")); // XWVM requires it
        Assert.Equal("1234", (string?)tour3.Elements("PilotOperationRecord").Single().Attribute("Score"));
        Assert.Equal("8", (string?)xwing.Elements("TrainingRecord").Single(t => (string?)t.Attribute("CraftType") == "YWing").Attribute("HighestLevelCompleted"));
        Assert.Equal("2", (string?)xml.Elements("PilotGameRecord").Single(e => (string?)e.Attribute("Id") == "tf")
            .Elements("PilotAwardRecord").Single(a => (string?)a.Attribute("Award") == "secretOrderRank").Attribute("Level"));

        // A new game record carries every attribute XWVM writes, and sits in XWVM's game order.
        Assert.Equal(new[] { "xwing", "tf", "xwa" }, xml.Elements("PilotGameRecord").Select(e => (string?)e.Attribute("Id")));
        var xwa = xml.Elements("PilotGameRecord").Last();
        Assert.Equal("Hard", (string?)xwa.Attribute("Difficulty"));
        Assert.Equal("-1", (string?)xwa.Attribute("AvatarIndex"));

        // Nothing else was added; the untouched save was the file as it was.
        Assert.DoesNotContain("tour5", untouched);
        Assert.Equal(1, xwing.Elements("PilotTourRecord").Count(t => (string?)t.Attribute("TourId") == "tour3"));
    }

    [Fact]
    public void Awards_and_started_flags_remove_their_records()
    {
        var doc = Open();
        Assert.Null(doc.Field("game[xwing].tour[tour1].started").TrySetInteger(0));
        Assert.Null(doc.Field("game[xwing].award[kalidor]").TrySetInteger(3));
        Assert.Null(doc.Field("game[xwing].award[kalidor]").TrySetInteger(-1));

        var xwing = XDocument.Parse(Encoding.UTF8.GetString(doc.Save()).TrimStart('\uFEFF')).Root!.Element("PilotGameRecord")!;
        Assert.DoesNotContain(xwing.Elements("PilotTourRecord"), t => (string?)t.Attribute("TourId") == "tour1");
        Assert.DoesNotContain(xwing.Elements("PilotAwardRecord"), a => (string?)a.Attribute("Award") == "kalidor");
    }

    [Fact]
    public void A_step_beyond_the_last_operation_cannot_be_saved()
    {
        var doc = Open();
        Assert.NotNull(doc.Field("game[xwing].tour[tour2].step").TrySetInteger(12)); // tour 2 has 12 operations (0-11)
        Assert.Null(doc.Field("game[xwing].tour[tour2].step").TrySetInteger(11));
    }
}
