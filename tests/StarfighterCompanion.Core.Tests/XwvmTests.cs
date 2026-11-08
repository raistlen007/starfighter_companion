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
        Assert.Equal(3, active.Choices!.Count); // none, tour1, tour2
        Assert.NotNull(active.TrySetInteger(3));
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
}
