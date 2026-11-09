using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Formats.Xwa;
using Xunit;

namespace StarfighterCompanion.Core.Tests;

/// <summary>The navigation tree: progression at the roots, one sub-tree per mission with several values.</summary>
public class LayoutTests
{
    private static string GroupOf(Model.PilotDocument doc, string id) => doc.DisplayFields.Single(f => f.Id == id).Group;

    [Fact]
    public void XWing_tours_hold_their_progress_and_scores()
    {
        var doc = PilotFormats.Open("LUKE.PLT", new byte[XWingPilotFormat.WindowsRecordSize]);

        Assert.Equal("Tours of Duty", GroupOf(doc, "tour.current"));
        Assert.Equal("Tours of Duty/Tour 4 (Imperial Pursuit)", GroupOf(doc, "tour.status[3]"));
        Assert.Equal("Tours of Duty/Tour 4 (Imperial Pursuit)", GroupOf(doc, "tour.score[3][6]"));
        Assert.Equal("Operation 6 score (mission B)", doc.DisplayFields.Single(f => f.Id == "tour.score[3][6]").Label);
        Assert.Equal("Historical missions/Y-wing/Mission 2", GroupOf(doc, "historic.patch[1][1]"));

        // Tours 6-8 and unused mission slots are hidden while empty, and still saved.
        Assert.DoesNotContain(doc.DisplayFields, f => f.Id == "tour.status[5]" || f.Id == "tour.score[0][20]");
        Assert.Equal(new byte[XWingPilotFormat.WindowsRecordSize], doc.Save());
    }

    [Fact]
    public void Hidden_slots_appear_when_they_hold_data()
    {
        var data = new byte[XWingPilotFormat.WindowsRecordSize];
        data[0x2F7 + (5 * 25 + 0) * 4] = 7; // a score in tour 6
        var doc = PilotFormats.Open("LUKE.PLT", data);
        Assert.Equal("Tours of Duty/Tour 6 (not in the game)", GroupOf(doc, "tour.score[5][0]"));
    }

    [Fact]
    public void Tie_battles_have_a_sub_tree_per_mission_with_objective_flags()
    {
        var doc = PilotFormats.Open("VADER.TFR", FormatTests.ValidTie());

        Assert.Equal("Battles", GroupOf(doc, "battle.current"));
        Assert.Equal("Battles/Battle 2", GroupOf(doc, "battle.status[1]"));
        Assert.Equal("Battles/Battle 2/Mission 3", GroupOf(doc, "battle.score[1][2]"));
        Assert.Equal(TiePilotFormat.BattleMissions[1], doc.DisplayFields.Count(f => f.Group.StartsWith("Battles/Battle 2/", StringComparison.Ordinal) && f.Id.StartsWith("battle.score")));
        Assert.DoesNotContain(doc.DisplayFields, f => f.Group.StartsWith("Battles/Battle 14", StringComparison.Ordinal));

        // The objective flags edit single bits of the battle's byte.
        Assert.Null(doc.Field("battle.secret[1][2]").TrySetInteger(1));
        Assert.Null(doc.Field("battle.bonus[1][0]").TrySetInteger(1));
        Assert.Equal(0b100, doc.Int("battle.secretBits[1]"));
        Assert.Equal(0b1, doc.Int("battle.bonusBits[1]"));
        Assert.Null(doc.Field("battle.secret[1][2]").TrySetInteger(0));
        Assert.Equal(0, doc.Int("battle.secretBits[1]"));

        var saved = doc.Save();
        Assert.Equal(1, saved[0x3A5 + 1]);
        Assert.Equal(1, saved[TiePilotFormat.RecordSize + 0x3A5 + 1]); // both copies
    }

    [Fact]
    public void Xwa_campaigns_follow_a_mission_list()
    {
        var list = XwaMissionList.Parse(new[] { "[Family Missions]", "1", "1b0m1.tie", "The Mining Guild", "[Battle 1]", "7", "* 1b1m1.tie", "!M0101!" });
        var doc = new XwaPilotDocument(new XwaPilotFormat(), new byte[XwaPilotFormat.RecordSize], XwaPilotFormat.Schema, true, list, null);

        Assert.Equal("Campaign", GroupOf(doc, "skipsRemaining"));
        Assert.Equal("Campaign/Family Missions/1. The Mining Guild", GroupOf(doc, "tod[1].bestScore"));
        Assert.Equal("Campaign/Battle 1/1. 1b1m1", GroupOf(doc, "tod[7].bestScore"));
        Assert.DoesNotContain(doc.DisplayFields, f => f.Id == "tod[2].bestScore"); // not listed and empty
    }

    [Fact]
    public void The_xwa_mission_list_is_built_in()
    {
        var doc = PilotFormats.Open("PILOT.plt", new byte[XwaPilotFormat.RecordSize], "xwa");

        Assert.Equal("Campaign/Prologue: Family Business/1. Aeron's Lesson: Transport Operations", GroupOf(doc, "tod[0].bestScore"));
        Assert.Equal("Campaign/Battle 1: Clearing the Way/1. Convoy Attack", GroupOf(doc, "tod[9].bestScore"));
        Assert.Equal("Combat chamber/Battle 1: Clearing the Way/1. Convoy Attack", GroupOf(doc, "combat[9].bestScore"));
    }

    [Fact]
    public void Localisation_keys_are_skipped()
    {
        var list = XwaMissionList.Parse(new[] { "!BATTLE_1_HEADER![Battle 1]", "9", "* 1b1m1.tie", "!MISSION_9_DESC!Convoy Attack" });
        Assert.Equal(("Battle 1", 9, "1b1m1.tie", "Convoy Attack"), (list[0].Section, list[0].MissionIndex, list[0].FileName, list[0].Title));
    }

    [Fact]
    public void Game_files_next_to_the_pilot_are_never_read()
    {
        var dir = Directory.CreateTempSubdirectory("sfc-xwa").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "MISSIONS"));
            File.WriteAllText(Path.Combine(dir, "MISSIONS", "MISSION.LST"), "[From the install]\r\n1\r\n1b0m1.tie\r\nShould not appear\r\n");
            var doc = PilotFormats.Open(Path.Combine(dir, "PILOT.plt"), new byte[XwaPilotFormat.RecordSize], "tftc");

            Assert.DoesNotContain(doc.DisplayFields, f => f.Group.Contains("From the install"));
            Assert.Equal("Campaign/Mission 1", GroupOf(doc, "tod[1].bestScore"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Mission_list_is_read_like_the_game_does()
    {
        var list = XwaMissionList.Parse(new[] { "// c", "[A]", "3", "& x.tie", "Title X", "[B]", "4", "y.tie", "Title Y" });
        Assert.Equal(new[] { ("A", 3, "x.tie", "Title X"), ("B", 4, "y.tie", "Title Y") },
            list.Select(e => (e.Section, e.MissionIndex, e.FileName, e.Title)));
    }

    [Fact]
    public void Xvt_missions_are_sub_trees()
    {
        var doc = PilotFormats.Open("Keyan0.plt", new byte[Formats.XvT.XvtPilotFormat.RecordSize]);
        var field = doc.DisplayFields.Single(f => f.Id == "faction[0].spMelee[4].bestScore");
        Assert.Equal("Faction record 1/Single player/Melee missions/Mission 5", field.Group);
        Assert.Equal("Best score", field.Label);
    }

    [Fact]
    public void Every_shown_field_is_a_real_field_and_none_twice()
    {
        foreach (var (name, data) in new[]
                 {
                     ("A.PLT", new byte[XWingPilotFormat.WindowsRecordSize]), ("A.TFR", FormatTests.ValidTie()),
                     ("A0.plt", new byte[Formats.XvT.XvtPilotFormat.RecordSize]), ("X.plt", new byte[XwaPilotFormat.RecordSize]),
                 })
        {
            var doc = PilotFormats.Open(name, data);
            Assert.Equal(doc.DisplayFields.Count, doc.DisplayFields.Distinct().Count());
            Assert.All(doc.DisplayFields, f => Assert.Same(f, doc.Field(f.Id)));
        }
    }
}
