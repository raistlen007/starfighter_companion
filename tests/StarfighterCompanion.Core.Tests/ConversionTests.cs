using System.Text;
using System.Xml.Linq;
using StarfighterCompanion.Core.Conversion;
using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Library;
using StarfighterCompanion.Core.Model;
using Xunit;

namespace StarfighterCompanion.Core.Tests;

public class ConversionTests
{
    // ---- helpers: build pilots through the editor, so offsets are checked against the schemas ----

    private static byte[] Edit(byte[] data, string fileName, params (string Id, long Value)[] values)
    {
        var doc = PilotFormats.Open(fileName, data);
        foreach (var (id, value) in values)
        {
            Assert.Null(doc.Field(id).TrySetInteger(value));
        }

        return doc.Save();
    }

    private static byte[] XWing98Pilot() => Edit(NewXWing98(), "LUKE.PLT",
        ("status", 0), ("rank", 3), ("score", 123456), ("skill", 5000),
        ("medal.corellianCross", 1), ("medal.kalidor", 3),
        ("training.bestScore[1]", 4242), ("training.level[1]", 4),
        ("historic.score[2][3]", 777), ("historic.patch[2][3]", 1), ("historic.bonusScore[1]", 99), ("historic.bonusCompleted[1]", 1),
        ("tour.status[0]", 3), ("tour.progress[0]", 12), ("tour.score[0][11]", 5000),
        ("tour.status[3]", 1), ("tour.progress[3]", 6), ("tour.current", 3), ("tour.currentOperation", 6),
        ("tour.score[3][6]", 900), // operation 5 offers missions 5 and 6; mission 6 was flown
        ("stats.kills", 30), ("stats.killsByType[3]", 30), ("stats.captures", 2), ("stats.capturesByType[10]", 2),
        ("stats.laserShots", 1000), ("stats.laserCraftHits", 400), ("stats.warheadsFired", 20), ("stats.warheadCraftHits", 10),
        ("stats.craftLost", 1), ("stats.surfaceVictories", 5));

    private static byte[] NewXWing98()
    {
        var r = new byte[XWingPilotFormat.WindowsRecordSize];
        r.AsSpan(0x2E7, 8).Fill(0xFF);
        return r;
    }

    private static byte[] TiePilot() => Edit(FormatTests.ValidTie(), "VADER.TFR",
        ("rank", 4), ("difficulty", 2), ("score", 300000), ("avgScore", 6000), ("secretOrder", 3), ("secretCompletions", 7), ("secretScore", 45000),
        ("training.score[5]", 1234), ("training.maxLevel[5]", 6),
        ("combat.score[1][2]", 888), ("combat.complete[1][2]", 1),
        ("battle.status[0]", 3), ("battle.mission[0]", 6), ("battle.score[0][5]", 4000), ("battle.secretBits[0]", 0b100001),
        ("battle.status[11]", 1), ("battle.mission[11]", 2), ("battle.current", 11), ("battle.bonusBits[11]", 0b10),
        ("stats.kills", 12), ("stats.killsByType[0]", 10), ("stats.killsByType[53]", 2),
        ("stats.laserShots", 500), ("stats.laserHits", 100), ("stats.ejections", 3));

    // ---- edition to edition ----

    [Fact]
    public void XWing_98_to_dos_and_back_is_lossless()
    {
        var original = XWing98Pilot();
        var dos = EditionConverter.Convert(original, Edition.XWing98, Edition.XWing94);
        Assert.Equal(XWingPilotFormat.DosRecordSize, dos.Data.Length);

        var doc = PilotFormats.Open("LUKE.PLT", dos.Data);
        Assert.Equal(123456, doc.Int("score"));
        Assert.Equal(3, doc.Int("tour.status[0]"));
        Assert.Equal(900, doc.Int("tour.score[3][6]"));
        Assert.Equal(400, doc.Int("stats.laserCraftHits"));

        var back = EditionConverter.Convert(dos.Data, Edition.XWing94, Edition.XWing98);
        Assert.Equal(original, back.Data);
    }

    [Fact]
    public void XWing_copies_are_all_converted()
    {
        var one = XWing98Pilot();
        var two = one.Concat(one).ToArray();
        var dos = EditionConverter.Convert(two, Edition.XWing98, Edition.XWing93);
        Assert.Equal(XWingPilotFormat.DosRecordSize * 2, dos.Data.Length);
        Assert.Equal(dos.Data.AsSpan(0, XWingPilotFormat.DosRecordSize).ToArray(), dos.Data.AsSpan(XWingPilotFormat.DosRecordSize).ToArray());
    }

    [Fact]
    public void XWing_93_warns_about_add_on_tours()
    {
        var result = EditionConverter.Convert(XWing98Pilot(), Edition.XWing98, Edition.XWing93);
        Assert.Contains(result.Warnings, w => w.Contains("Imperial Pursuit"));
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("Error"));
    }

    [Fact]
    public void Tie_to_floppy_moves_the_current_battle_to_one_that_exists()
    {
        var result = EditionConverter.Convert(TiePilot(), Edition.Tie98, Edition.Tie94);
        var doc = PilotFormats.Open("VADER.TFR", result.Data);
        Assert.Equal(0, doc.Int("battle.current"));
        Assert.Equal(1, doc.Int("battle.status[11]")); // kept, the game ignores it
        Assert.Contains(result.Notes, n => n.Contains("Battle 12"));
        Assert.Contains(result.Warnings, w => w.Contains("Battle 12 has progress"));
        Assert.DoesNotContain(result.Warnings, w => w.StartsWith("Error"));
    }

    [Fact]
    public void Tie_between_cd_and_windows_is_unchanged()
    {
        var data = TiePilot();
        Assert.Equal(data, EditionConverter.Convert(data, Edition.Tie95, Edition.Tie98).Data);
    }

    [Fact]
    public void Edition_rules_block_battles_the_floppy_does_not_have()
    {
        var doc = PilotFormats.Open("VADER.TFR", FormatTests.ValidTie());
        doc.Edition = Edition.Tie94;
        Assert.Null(doc.Field("battle.current").TrySetInteger(11));
        Assert.False(doc.CanSave(out var issues));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("does not exist"));

        doc.Edition = Edition.Tie98;
        Assert.True(doc.CanSave(out _));
    }

    [Fact]
    public void XWing_tours_beyond_five_cannot_be_started()
    {
        var doc = PilotFormats.Open("LUKE.PLT", NewXWing98());
        Assert.Null(doc.Field("tour.status[6]").TrySetInteger(1));
        Assert.False(doc.CanSave(out _));
    }

    [Theory]
    [InlineData("LUKE", "xwing94", null)]
    [InlineData("SKYWALKER", "xwing94", "at most 8")]
    [InlineData("SKYWALKER", "xwing98", null)]
    [InlineData("Luke S", "xwing98", "Only letters")]
    public void Pilot_names_follow_each_edition(string name, string edition, string? error)
    {
        var message = Edition.Find(edition)!.CheckPilotName(name);
        if (error is null)
        {
            Assert.Null(message);
        }
        else
        {
            Assert.Contains(error, message);
        }
    }

    // ---- XWVM ----

    [Fact]
    public void XWing_pilot_imports_into_a_new_xwvm_pilot()
    {
        var result = XwvmBridge.ToXwvm(XWing98Pilot(), Edition.XWing98, null, "Luke");
        var root = XDocument.Parse(Encoding.UTF8.GetString(result.Data).TrimStart('﻿')).Root!;

        Assert.Equal("Luke", (string?)root.Attribute("Name"));
        Assert.Equal(new[] { "xwing", "tf", "xvt", "xwa" }, root.Elements("PilotGameRecord").Select(e => (string?)e.Attribute("Id")));

        var game = root.Elements("PilotGameRecord").First();
        Assert.Equal("Captain", (string?)game.Attribute("Rank"));
        Assert.Equal("123456", (string?)game.Attribute("CurrentScore"));
        Assert.Equal("20000", (string?)game.Attribute("AiScore"));
        Assert.Equal("tour4", (string?)game.Attribute("ActiveTour"));

        var tour1 = game.Elements("PilotTourRecord").Single(t => (string?)t.Attribute("TourId") == "tour1");
        Assert.Equal("2147483647", (string?)tour1.Attribute("CurrentTourStep"));
        Assert.Equal(12, tour1.Elements().Count());

        var tour4 = game.Elements("PilotTourRecord").Single(t => (string?)t.Attribute("TourId") == "tour4");
        Assert.Equal("6", (string?)tour4.Attribute("CurrentTourStep"));
        Assert.Equal("900", (string?)tour4.Elements().Single(o => (string?)o.Attribute("TourStep") == "5").Attribute("Score"));

        Assert.Equal("2", (string?)game.Elements("PilotAwardRecord").Single(a => (string?)a.Attribute("Award") == "kalidor").Attribute("Level"));
        Assert.Equal("30", (string?)game.Elements("EntityVictoryRecord").Single(v => (string?)v.Attribute("Id") == "TIEFighter").Attribute("Kills"));
        Assert.Equal("400", (string?)game.Elements("StatRecord").Single(v => (string?)v.Attribute("Id") == "LaserHits").Attribute("Value"));
        Assert.Equal(5, game.Elements("HistoricTourRecord").Count());

        // The editor opens the result without problems.
        var doc = PilotFormats.Open("Luke.vmpilot", result.Data);
        Assert.True(doc.CanSave(out _));
    }

    [Fact]
    public void XWing_round_trips_through_xwvm()
    {
        var original = XWing98Pilot();
        var vm = XwvmBridge.ToXwvm(original, Edition.XWing98, null, "Luke");
        var back = XwvmBridge.FromXwvm(vm.Data, Edition.XWing98);
        Assert.DoesNotContain(back.Warnings, w => w.StartsWith("Error"));

        var a = PilotFormats.Open("LUKE.PLT", original);
        var b = PilotFormats.Open("LUKE.PLT", back.Data);
        var differing = a.Fields.Where(f => f.GetText() != b.Field(f.Id).GetText()).Select(f => f.Id).ToList();

        // The only difference: which of the two alternative missions of tour 4 operation 6 was flown.
        Assert.Equal(new[] { "tour.score[3][5]", "tour.score[3][6]" }, differing);
        Assert.Equal(900, b.Int("tour.score[3][5]"));
        Assert.Equal(6, b.Int("tour.currentOperation"));
    }

    [Fact]
    public void Exporting_to_dos_gives_a_dos_record()
    {
        var vm = XwvmBridge.ToXwvm(XWing98Pilot(), Edition.XWing98, null, "Luke");
        var dos = XwvmBridge.FromXwvm(vm.Data, Edition.XWing94);
        Assert.Equal(XWingPilotFormat.DosRecordSize, dos.Data.Length);
        Assert.Equal(123456, PilotFormats.Open("LUKE.PLT", dos.Data).Int("score"));
    }

    [Fact]
    public void Tie_round_trips_through_xwvm()
    {
        var original = TiePilot();
        var vm = XwvmBridge.ToXwvm(original, Edition.Tie98, null, "Vader");
        Assert.Contains(vm.Warnings, w => w.Contains("Super Star Destroyer"));

        var game = XDocument.Parse(Encoding.UTF8.GetString(vm.Data).TrimStart('﻿')).Root!
            .Elements("PilotGameRecord").Single(e => (string?)e.Attribute("Id") == "tf");
        Assert.Equal("battle12", (string?)game.Attribute("ActiveTour"));
        Assert.Equal("Hard", (string?)game.Attribute("Difficulty"));
        Assert.Equal("2", (string?)game.Elements("PilotAwardRecord").Single().Attribute("Level"));
        var b1 = game.Elements("PilotTourRecord").Single(t => (string?)t.Attribute("TourId") == "battle1");
        Assert.Equal("true", (string?)b1.Elements().Single(o => (string?)o.Attribute("TourStep") == "5").Attribute("SecondaryGoals"));

        var back = PilotFormats.Open("VADER.TFR", XwvmBridge.FromXwvm(vm.Data, Edition.Tie98).Data);
        var a = PilotFormats.Open("VADER.TFR", original);
        var differing = a.Fields.Where(f => f.GetText() != back.Field(f.Id).GetText()).Select(f => f.Id).ToList();

        // Lost on the way: kills of the Super Star Destroyer (XWVM has no such craft); unplayed craft get TIE's default level.
        Assert.Contains("stats.killsByType[53]", differing);
        Assert.Equal(10, back.Int("stats.kills"));
        Assert.Empty(differing.Where(id => id.StartsWith("battle.") || id.StartsWith("combat.") || id is "score" or "rank" or "secretOrder"));
        Assert.Equal(6, back.Int("training.maxLevel[5]"));
    }

    [Fact]
    public void Importing_into_an_existing_xwvm_pilot_keeps_the_other_games()
    {
        var existing = Encoding.UTF8.GetBytes(
            "<PilotRecord Name=\"Keyan\"><PilotGameRecord Id=\"xwing\" Rank=\"General\" Status=\"Alive\" Difficulty=\"Hard\" CurrentScore=\"9\" AiScore=\"9\" Droid=\"R2-X2\" AvatarIndex=\"4\" />" +
            "<PilotGameRecord Id=\"xvt\" Rank=\"General\" Status=\"Alive\" Difficulty=\"Medium\" CurrentScore=\"1\" AiScore=\"1\" AvatarIndex=\"-1\"><Custom /></PilotGameRecord></PilotRecord>");

        var result = XwvmBridge.ToXwvm(XWing98Pilot(), Edition.XWing98, existing, "ignored");
        var root = XDocument.Parse(Encoding.UTF8.GetString(result.Data)).Root!;

        Assert.Equal("Keyan", (string?)root.Attribute("Name"));
        var xwing = root.Elements("PilotGameRecord").Single(e => (string?)e.Attribute("Id") == "xwing");
        Assert.Equal("Captain", (string?)xwing.Attribute("Rank"));
        Assert.Equal("Hard", (string?)xwing.Attribute("Difficulty")); // X-Wing has no difficulty of its own
        Assert.Equal("R2-X2", (string?)xwing.Attribute("Droid"));
        Assert.Equal("4", (string?)xwing.Attribute("AvatarIndex"));
        Assert.NotNull(root.Elements("PilotGameRecord").Single(e => (string?)e.Attribute("Id") == "xvt").Element("Custom"));
        Assert.Equal(4, root.Elements("PilotGameRecord").Count());
    }

    [Fact]
    public void Xwvm_file_names_follow_xwvm()
    {
        Assert.Equal("Luke.vmpilot", XwvmBridge.VmpilotFileName("Luke"));
        Assert.Equal("A%3AB%25.vmpilot", XwvmBridge.VmpilotFileName("A:B%"));
    }

    [Fact]
    public void Plans_find_the_right_files()
    {
        var root = Directory.CreateTempSubdirectory("sfc-conv").FullName;
        try
        {
            var xwvm = Directory.CreateDirectory(Path.Combine(root, "xwvm")).FullName;
            File.WriteAllText(Path.Combine(xwvm, "something.vmpilot"), "<PilotRecord Name=\"Luke\" />");
            var dos = Directory.CreateDirectory(Path.Combine(root, "dos")).FullName;
            File.WriteAllBytes(Path.Combine(dos, "luke.plt"), new byte[XWingPilotFormat.DosRecordSize]);

            var format = PilotFormats.Detect("LUKE.PLT", XWing98Pilot())!;
            var targets = PilotConverter.TargetsFor(format, Edition.XWing98, XWing98Pilot());
            Assert.Equal(new[] { "xwing93", "xwing94", "xwvm" }, targets.Select(t => t.VariantId));

            // An XWVM pilot with that name already exists: its X-Wing career is replaced.
            var toXwvm = PilotConverter.Plan(XWing98Pilot(), format, Edition.XWing98, targets[2], "Luke", xwvm);
            Assert.Equal(Path.Combine(xwvm, "something.vmpilot"), toXwvm.Path);
            Assert.True(toXwvm.Overwrites);

            // DOS file names are matched in any case.
            var toDos = PilotConverter.Plan(XWing98Pilot(), format, Edition.XWing98, targets[1], "LUKE", dos);
            Assert.Equal(Path.Combine(dos, "luke.plt"), toDos.Path);
            Assert.True(toDos.Overwrites);

            Assert.Throws<ArgumentException>(() => PilotConverter.Plan(XWing98Pilot(), format, Edition.XWing98, targets[1], "LUKESKYWALKER", dos));

            // An XWVM pilot offers the editions of the games it has.
            var vm = File.ReadAllBytes(toXwvm.Path);
            var fromXwvm = PilotConverter.TargetsFor(PilotFormats.Detect("a.vmpilot", vm)!, null, vm);
            Assert.Empty(fromXwvm); // the existing file has no game records yet
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
