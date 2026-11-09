using System.Text;
using StarfighterCompanion.Core.Binary;
using StarfighterCompanion.Core.Formats;
using StarfighterCompanion.Core.Formats.Tie;
using StarfighterCompanion.Core.Formats.XvT;
using StarfighterCompanion.Core.Formats.Xwa;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Model;

namespace StarfighterCompanion.Core.Tests;

public class FormatTests
{
    public static IEnumerable<object[]> BinaryFormats() => new[]
    {
        new object[] { "LUKE.PLT", XWingPilotFormat.WindowsRecordSize, 2 },
        new object[] { "LUKE.PLT", XWingPilotFormat.DosRecordSize, 2 },
        new object[] { "LUKE.PLT", XWingPilotFormat.WindowsRecordSize, 1 },
        new object[] { "VADER.TFR", TiePilotFormat.RecordSize, 2 },
        new object[] { "Keyan0.plt", XvtPilotFormat.RecordSize, 1 },
        new object[] { "Keyan0.pl2", BopPilotFormat.RecordSize, 1 },
        new object[] { "Ace0.plt", XwaPilotFormat.RecordSize, 1 },
    };

    [Fact]
    public void All_schemas_build_with_their_documented_sizes()
    {
        // Building a schema throws if a single byte is unaccounted for.
        Assert.Equal(1705, new XWingPilotFormat(XWingEdition.Windows98).Schema.Size);
        Assert.Equal(1706, new XWingPilotFormat(XWingEdition.Dos).Schema.Size);
        Assert.Equal(1928, TiePilotFormat.Schema.Size);
        Assert.Equal(253754, XvtPilotFormat.Schema.Size);
        Assert.Equal(296238, BopPilotFormat.Schema.Size);
        Assert.Equal(152076, XwaPilotFormat.Schema.Size);
    }

    [Fact]
    public void Tie_training_and_combat_slots_use_confirmed_ship_names()
    {
        var fields = TiePilotFormat.Schema.Fields.ToDictionary(f => f.Id);
        Assert.Equal("TIE Fighter (Craft 1) current level", fields["training.level[0]"].Label);
        Assert.Equal("Assault Gunboat (Craft 5) high score", fields["training.score[4]"].Label);
        Assert.Equal("TIE Defender (Craft 6) highest level reached", fields["training.maxLevel[5]"].Label);
        Assert.Equal("Missile Boat (Craft 7) current level", fields["training.level[6]"].Label);
        Assert.Equal("Combat chamber/Missile Boat (Craft 7)", fields["combat.score[6][0]"].Group);
        Assert.Equal("Combat chamber/Assault Gunboat (Craft 5)", fields["combat.complete[4][0]"].Group);
        Assert.Equal("Craft 8 current level", fields["training.level[7]"].Label);
        Assert.Equal("Combat chamber/Craft 12", fields["combat.score[11][0]"].Group);
    }

    [Fact]
    public void Xwa_original_names_are_bundled_and_unmapped_ids_stay_numeric()
    {
        var fields = XwaPilotFormat.Schema.Fields.ToDictionary(f => f.Id);
        Assert.Equal("X-wing (craft type 1) unlocked", fields["craftKnown[1]"].Label);
        Assert.Equal("YT-2000 (craft type 110)", fields["main.killsByCraft[0][110]"].Label);
        Assert.Equal("TIE Fighter (craft type 5)", fields["main.sharedKillsByCraft[1][5]"].Label);
        Assert.Equal("Craft type 184 unlocked", fields["craftKnown[184]"].Label);
        Assert.Equal("Craft type 511", fields["main.assistsByCraft[2][511]"].Label);
    }

    [Theory]
    [InlineData("xwa", "X-wing (craft type 1) unlocked")]
    [InlineData("openxwa", "X-wing (craft type 1) unlocked")]
    [InlineData("xwaup", "Craft type 1 unlocked")]
    [InlineData("tftc", "Craft type 1 unlocked")]
    public void Xwa_variant_determines_only_display_labels(string variantId, string expected)
    {
        var data = new byte[XwaPilotFormat.RecordSize];
        var document = PilotFormats.Open("Ace0.plt", data, variantId);
        Assert.Equal(expected, document.Field("craftKnown[1]").Label);
        Assert.Equal(data, document.Save());
    }

    [Fact]
    public void Fields_never_overlap()
    {
        var schemas = new[]
        {
            new XWingPilotFormat(XWingEdition.Windows98).Schema, new XWingPilotFormat(XWingEdition.Dos).Schema,
            TiePilotFormat.Schema, XvtPilotFormat.Schema, BopPilotFormat.Schema, XwaPilotFormat.Schema,
        };

        foreach (var schema in schemas)
        {
            var used = new bool[schema.Size];
            foreach (var f in schema.Fields)
            {
                for (var i = f.Offset; i < f.Offset + f.Size; i++)
                {
                    Assert.False(used[i], $"{f.Id} overlaps another field at byte {i}.");
                    used[i] = true;
                }
            }
        }
    }

    [Fact]
    public void Known_offsets_match_the_reference_implementations()
    {
        var xw = new XWingPilotFormat(XWingEdition.Windows98).Schema.Fields.ToDictionary(f => f.Id);
        Assert.Equal(0x3, xw["rank"].Offset);
        Assert.Equal(0x4, xw["score"].Offset);
        Assert.Equal(0x26, xw["training.bestScore[0]"].Offset);
        Assert.Equal(0x280, xw["tour.current"].Offset);
        Assert.Equal(0x2DF, xw["tour.status[0]"].Offset);
        Assert.Equal(0x2F7, xw["tour.score[0][0]"].Offset);
        Assert.Equal(0x62F, xw["stats.kills"].Offset);
        Assert.Equal(1685, xw["stats.laserShots"].Offset);

        var dos = new XWingPilotFormat(XWingEdition.Dos).Schema.Fields.ToDictionary(f => f.Id);
        Assert.Equal(0x2E0, dos["tour.status[0]"].Offset);

        var tie = TiePilotFormat.Schema.Fields.ToDictionary(f => f.Id);
        Assert.Equal(0x0A, tie["secretOrder"].Offset);
        Assert.Equal(0x268, tie["battle.current"].Offset);
        Assert.Equal(0x3DA, tie["battle.score[0][0]"].Offset);
        Assert.Equal(0x786, tie["stats.ejections"].Offset);

        var xwa = XwaPilotFormat.Schema.Fields.ToDictionary(f => f.Id);
        Assert.Equal(68762, xwa["craftKnown[0]"].Offset);
        Assert.Equal(69278, xwa["rating"].Offset);
    }

    [Theory]
    [MemberData(nameof(BinaryFormats))]
    public void Untouched_files_are_saved_byte_for_byte(string name, int recordSize, int copies)
    {
        var data = RandomRecord(recordSize, copies, seed: recordSize + copies);
        var doc = PilotFormats.Open(name, data);
        Assert.Equal(data, doc.Save());
    }

    [Theory]
    [MemberData(nameof(BinaryFormats))]
    public void Editing_one_value_changes_only_its_bytes(string name, int recordSize, int copies)
    {
        var data = new byte[recordSize * copies];
        var doc = PilotFormats.Open(name, data);
        var field = doc.Fields.OfType<BinaryField>().First(f => f.Editor == FieldEditor.Integer && !f.IsReadOnly && f.Maximum >= 5);

        Assert.Null(field.TrySetInteger(5));
        var saved = doc.Save();

        for (var c = 0; c < copies; c++)
        {
            for (var i = 0; i < recordSize; i++)
            {
                var inField = i >= field.Definition.Offset && i < field.Definition.Offset + field.Definition.Size;
                if (!inField)
                {
                    Assert.Equal(0, saved[c * recordSize + i]);
                }
            }

            Assert.Equal(5, saved[c * recordSize + field.Definition.Offset]);
        }

        var reopened = PilotFormats.Open(name, saved);
        Assert.Equal(5, reopened.Field(field.Id).GetInteger());
    }

    [Fact]
    public void Out_of_range_values_are_refused_and_nothing_changes()
    {
        var doc = PilotFormats.Open("VADER.TFR", ValidTie());
        var rank = doc.Field("rank");
        Assert.NotNull(rank.TrySetInteger(6));
        Assert.NotNull(rank.TrySetInteger(-1));
        Assert.Equal(0, rank.GetInteger());
        Assert.False(doc.IsDirty);

        Assert.NotNull(doc.Field("version").TrySetInteger(2)); // read-only
        Assert.NotNull(doc.Field("difficulty").TrySetInteger(3));
        Assert.Null(doc.Field("difficulty").TrySetInteger(2));
        Assert.True(doc.IsDirty);
    }

    [Fact]
    public void Wrong_sizes_are_not_opened()
    {
        Assert.Throws<UnsupportedPilotFileException>(() => PilotFormats.Open("x.plt", new byte[1000]));
        Assert.Throws<UnsupportedPilotFileException>(() => PilotFormats.Open("x.tfr", new byte[1929]));
        Assert.Throws<UnsupportedPilotFileException>(() => PilotFormats.Open("x.doc", new byte[1928]));
    }

    [Fact]
    public void Backup_copy_is_refreshed_when_the_pilot_is_edited()
    {
        var data = ValidTie();
        data[TiePilotFormat.RecordSize + 4] = 99; // backup copy differs
        var doc = PilotFormats.Open("VADER.TFR", data);
        Assert.Contains(doc.Notes, n => n.Contains("differ"));

        Assert.Null(doc.Field("score").TrySetInteger(1234));
        var saved = doc.Save();
        Assert.Equal(saved.AsSpan(0, TiePilotFormat.RecordSize).ToArray(), saved.AsSpan(TiePilotFormat.RecordSize).ToArray());

        // Saving again keeps both copies in step.
        Assert.Equal(saved, doc.Save());
    }

    [Fact]
    public void Two_active_tours_block_saving_only_when_caused_by_the_user()
    {
        var data = new byte[XWingPilotFormat.WindowsRecordSize];
        var doc = PilotFormats.Open("LUKE.PLT", data);
        Assert.Null(doc.Field("tour.status[0]").TrySetInteger(1));
        Assert.True(doc.CanSave(out _));

        Assert.Null(doc.Field("tour.status[1]").TrySetInteger(1));
        Assert.False(doc.CanSave(out var issues));
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.Contains("Only one tour"));
        Assert.Throws<InvalidOperationException>(() => doc.Save());

        // The same state coming from disk is only a warning, so an untouched pilot can always be saved.
        data[0x2DF] = 1;
        data[0x2E0] = 1;
        var fromDisk = PilotFormats.Open("LUKE.PLT", data);
        Assert.True(fromDisk.CanSave(out var diskIssues));
        Assert.Contains(diskIssues, i => i.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void Xvt_rating_name_follows_rating()
    {
        var data = new byte[XvtPilotFormat.RecordSize];
        Encoding.ASCII.GetBytes("Keyan").CopyTo(data, 0);
        var doc = PilotFormats.Open("Keyan0.plt", data);
        Assert.Equal("Keyan", doc.PilotName);
        Assert.NotNull(doc.Field("name").TrySetText("Other"));
        Assert.Null(doc.Field("rating").TrySetInteger(24));
        Assert.Equal("Jedi Master", doc.Field("ratingName").GetText());
        Assert.NotNull(doc.Field("rating").TrySetInteger(25));
    }

    [Fact]
    public void Text_fields_only_accept_ascii_that_fits()
    {
        var doc = PilotFormats.Open("Keyan0.pl2", new byte[BopPilotFormat.RecordSize]);
        var game = doc.Field("gameName");
        Assert.NotNull(game.TrySetText(new string('a', 32)));
        Assert.NotNull(game.TrySetText("Ünicode"));
        Assert.Null(game.TrySetText(new string('a', 31)));
        Assert.Equal(new string('a', 31), game.GetText());
    }

    [Fact]
    public void Xwa_rating_is_limited_to_the_per_rating_table()
    {
        var doc = PilotFormats.Open("Ace0.plt", new byte[XwaPilotFormat.RecordSize]);
        Assert.Null(doc.Field("rating").TrySetInteger(24));
        Assert.NotNull(doc.Field("rating").TrySetInteger(25));
        Assert.NotNull(doc.Field("rank").TrySetInteger(9));
        Assert.NotNull(doc.Field("skipsRemaining").TrySetInteger(4));
    }

    [Fact]
    public void Real_sample_files_round_trip()
    {
        // Drop real pilot files into tests/samples/<game>/ to have them checked.
        var root = FindSamples();
        if (root is null)
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var data = File.ReadAllBytes(path);
            var format = PilotFormats.Detect(path, data);
            if (format is null)
            {
                continue;
            }

            var doc = format.Open(path, data);
            Assert.True(doc.CanSave(out _), $"{path} cannot be saved untouched.");
            var saved = doc.Save();
            if (format is not Formats.Xwvm.XwvmPilotFormat)
            {
                Assert.True(data.SequenceEqual(saved), $"{path} changed although nothing was edited.");
            }
        }
    }

    internal static byte[] ValidTie()
    {
        var data = new byte[TiePilotFormat.RecordSize * 2];
        data[0] = 1;
        data[TiePilotFormat.RecordSize] = 1;
        return data;
    }

    private static byte[] RandomRecord(int recordSize, int copies, int seed)
    {
        var rng = new Random(seed);
        var record = new byte[recordSize];
        rng.NextBytes(record);
        var data = new byte[recordSize * copies];
        for (var c = 0; c < copies; c++)
        {
            record.CopyTo(data, c * recordSize);
        }

        return data;
    }

    private static string? FindSamples()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "samples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return null;
    }
}
