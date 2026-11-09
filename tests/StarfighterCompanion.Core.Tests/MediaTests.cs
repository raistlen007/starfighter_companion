using System.Buffers.Binary;
using System.Text;
using StarfighterCompanion.Core.Formats.XWing;
using StarfighterCompanion.Core.Library;
using StarfighterCompanion.Core.Media;

namespace StarfighterCompanion.Core.Tests;

/// <summary>
/// Icons and portraits are read from the player's game files. These tests build small files in the same formats,
/// so no game data is needed.
/// </summary>
public sealed class MediaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "sfc-media-" + Guid.NewGuid().ToString("N"));

    public MediaTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ---- Portrait choice ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("LUKE", 16)] // 76+85+75+69 = 305; (305 - 1) % 24 = 16
    [InlineData("luke", 16)] // the registration desk stores names in upper case
    [InlineData("A", 16)] // 65 - 1 = 64; 64 % 24 = 16
    [InlineData("B", 17)]
    [InlineData("WEDGE", 3)] // 87+69+68+71+69 = 364; 363 % 24 = 3
    [InlineData("", 1)] // abs(-1 % 24)
    public void Face_follows_the_name_like_the_dos_game(string name, int expected)
    {
        var sum = name.ToUpperInvariant().Sum(c => (int)c);
        var reference = Math.Abs((sum - 1) % 24);
        Assert.Equal(reference, XWingPortraits.FaceIndex(name));
        Assert.Equal(expected, XWingPortraits.FaceIndex(name));
    }

    [Fact]
    public void Every_face_is_reachable_and_changes_with_each_letter()
    {
        var seen = Enumerable.Range(0, 200).Select(i => XWingPortraits.FaceIndex("P" + (char)('A' + (i % 26)) + i)).ToHashSet();
        Assert.Equal(24, seen.Count);
        Assert.All(seen, i => Assert.InRange(i, 0, 23));
        Assert.NotEqual(XWingPortraits.FaceIndex("KEYAN"), XWingPortraits.FaceIndex("KEYAM"));
    }

    // ---- LFD / ANIM / DELT -----------------------------------------------------------------------------------------

    [Fact]
    public void Faces_are_decoded_with_the_palette_and_frames_line_up()
    {
        var (pilot, xwing) = BuildPortraitFiles();
        var portraits = XWingPortraits.FromResources(pilot, xwing, "test")!;

        Assert.Equal(24, portraits.Faces.Count);
        var face = portraits.Faces[3];
        Assert.Equal(4, face.Width);
        Assert.Equal(3, face.Height);

        // Frame 3 is: row 0 raw pixels 10,11 at x=1; row 2 an RLE run of 4 × colour 12 and nothing on row 1.
        Assert.Equal((0, 0, 0, 0), Rgba(face, 0, 0)); // not covered: transparent
        Assert.Equal(Color(10, standard: true), Rgba(face, 1, 0));
        Assert.Equal(Color(11, standard: true), Rgba(face, 2, 0));
        Assert.Equal((0, 0, 0, 0), Rgba(face, 1, 1));
        Assert.All(Enumerable.Range(0, 4), x => Assert.Equal(Color(200, standard: false), Rgba(face, x, 2)));

        // PILOT.LFD's own palette overrides the standard one where both define a colour.
        Assert.Equal(Color(200, standard: false), Rgba(portraits.Faces[0], 0, 2));
        Assert.Same(portraits.Faces[XWingPortraits.FaceIndex("LUKE")], portraits.For("Luke"));
    }

    [Fact]
    public void Portraits_are_found_in_the_resource_folder_of_a_dos_install()
    {
        var (pilot, xwing) = BuildPortraitFiles();
        var install = Path.Combine(_root, "XWING", "resource");
        Directory.CreateDirectory(install);
        File.WriteAllBytes(Path.Combine(install, "pilot.lfd"), pilot);
        File.WriteAllBytes(Path.Combine(install, "XWING.LFD"), xwing);

        var found = XWingPortraits.Find(new[] { Path.Combine(_root, "missing"), Path.Combine(_root, "XWING") });

        Assert.NotNull(found);
        Assert.Equal(24, found!.Faces.Count);
        Assert.EndsWith("pilot.lfd", found.Source);
    }

    [Theory]
    [InlineData(2048, 0)]
    [InlineData(2352, 16)]
    public void Portraits_are_found_inside_the_cd_image_of_the_gog_and_steam_editions(int sectorSize, int dataOffset)
    {
        var (pilot, xwing) = BuildPortraitFiles();
        var folder = Path.Combine(_root, "STAR WARS X-Wing", "xwing94");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "game.gog"),
            BuildIso(sectorSize, dataOffset, ("RESOURCE", "PILOT.LFD", pilot), ("RESOURCE", "XWING.LFD", xwing)));

        var found = XWingPortraits.Find(new[] { Path.Combine(_root, "STAR WARS X-Wing") });

        Assert.NotNull(found);
        Assert.Contains("RESOURCE/PILOT.LFD", found!.Source);
        Assert.Equal(Color(10, standard: true), Rgba(found.Faces[3], 1, 0));
    }

    [Fact]
    public void No_portraits_without_an_install()
    {
        Assert.Null(XWingPortraits.Find(new[] { _root }));
        Assert.Throws<InvalidDataException>(() => LfdArchive.Read(Encoding.ASCII.GetBytes("not an lfd file at all")));
    }

    // ---- Icons -----------------------------------------------------------------------------------------------------

    [Fact]
    public void Ico_files_are_decoded_with_their_transparency_mask()
    {
        var ico = BuildIco(BuildDib4Bit());
        var image = IconReader.FromIco(ico)!;

        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal((255, 0, 0, 255), Rgba(image, 0, 0)); // top row first (DIB rows are stored bottom-up)
        Assert.Equal((0, 0, 0, 0), Rgba(image, 1, 0)); // masked out
        Assert.Equal((0, 0, 255, 255), Rgba(image, 0, 1));
    }

    [Fact]
    public void The_largest_icon_of_a_windows_program_is_used()
    {
        var exe = BuildPe(BuildDib4Bit(), BuildDib32Bit(4));
        var image = IconReader.FromExecutable(exe)!;

        Assert.Equal(4, image.Width);
        Assert.Equal((10, 20, 30, 128), Rgba(image, 0, 0));
    }

    [Fact]
    public void Png_icons_are_passed_through()
    {
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 1, 0, 0, 0, 1, 0, 8, 6, 0, 0, 0 };
        var image = IconReader.FromIco(BuildIco(png))!;

        Assert.Equal(256, image.Width);
        Assert.Null(image.Rgba);
        Assert.Equal(png, image.Png);
    }

    [Fact]
    public void Dos_programs_have_no_icon()
    {
        var dos = new byte[0x200];
        dos[0] = (byte)'M';
        dos[1] = (byte)'Z';
        Assert.Null(IconReader.FromExecutable(dos));
        Assert.Null(IconReader.FromExecutable(Array.Empty<byte>()));
    }

    [Fact]
    public void A_games_icon_comes_from_its_own_program_or_falls_back_to_none()
    {
        var xwa = Path.Combine(_root, "XWA");
        Directory.CreateDirectory(xwa);
        File.WriteAllBytes(Path.Combine(xwa, "unins000.exe"), BuildPe(BuildDib32Bit(8)));
        File.WriteAllBytes(Path.Combine(xwa, "xwingalliance.exe"), BuildPe(BuildDib32Bit(4)));

        Assert.Equal(4, GameIcons.Find("xwa", xwa)!.Width);
        Assert.Equal(4, GameIcons.Find("embers", xwa)!.Width);
        Assert.Null(GameIcons.Find("xwa", Path.Combine(_root, "missing")));
        Assert.Null(GameIcons.Find("xwa", null));

        // A DOS edition has no icon of its own; next to the 1998 edition the 1998 program's icon is used.
        var dos = Path.Combine(xwa, "DOS");
        Directory.CreateDirectory(dos);
        Assert.Null(GameIcons.Find("xwing94", dos));
        File.WriteAllBytes(Path.Combine(xwa, "XWING95.EXE"), BuildPe(BuildDib32Bit(4)));
        Assert.NotNull(GameIcons.Find("xwing94", dos));

        var gog = Path.Combine(_root, "GOG");
        Directory.CreateDirectory(gog);
        File.WriteAllBytes(Path.Combine(gog, "goggame-1234.ico"), BuildIco(BuildDib4Bit()));
        Assert.Equal(2, GameIcons.Find("tie95", gog)!.Width);
    }

    [Fact]
    public void Every_xwa_version_looks_for_the_xwa_program()
    {
        var xwa = Path.Combine(_root, "Mod");
        Directory.CreateDirectory(xwa);
        File.WriteAllBytes(Path.Combine(xwa, "unins000.exe"), BuildPe(BuildDib32Bit(8)));
        File.WriteAllBytes(Path.Combine(xwa, "XWingAlliance.exe"), BuildPe(BuildDib32Bit(4)));
        foreach (var variant in GameCatalog.Game(GameCatalog.Xwa).Variants.Where(v => v.Id != "openxwa"))
        {
            Assert.Equal(4, GameIcons.Find(variant.Id, xwa)!.Width);
        }
    }

    // ---- Builders --------------------------------------------------------------------------------------------------

    private static (int, int, int, int) Rgba(GameImage image, int x, int y)
    {
        var (r, g, b, a) = image.Pixel(x, y);
        return (r, g, b, a);
    }

    /// <summary>Colour <paramref name="index"/> of the test palettes, as decoded (8 bits → 6 bits → 8 bits).</summary>
    private static (int, int, int, int) Color(int index, bool standard)
    {
        static int Six(int v) => (v >> 2) * 255 / 63;
        return standard ? (Six(index), Six(255 - index), Six(100), 255) : (Six(250), Six(index), Six(5), 255);
    }

    private static (byte[] Pilot, byte[] XWing) BuildPortraitFiles()
    {
        // Standard palette: every colour defined. PILOT.LFD: colours 200-210 only.
        var standard = new List<byte> { 0, 255 };
        for (var i = 0; i < 256; i++)
        {
            standard.AddRange(new[] { (byte)i, (byte)(255 - i), (byte)100 });
        }

        standard.Add(0); // no colour cycles
        var own = new List<byte> { 200, 210 };
        for (var i = 200; i <= 210; i++)
        {
            own.AddRange(new byte[] { 250, (byte)i, 5 });
        }

        own.Add(0);

        var anim = new List<byte>();
        anim.AddRange(LE16(24));
        for (var frame = 0; frame < 24; frame++)
        {
            var delt = new List<byte>();
            delt.AddRange(LE16(0)); // left
            delt.AddRange(LE16(0)); // top
            delt.AddRange(LE16(3)); // right (inclusive)
            delt.AddRange(LE16(2)); // bottom
            delt.AddRange(LE16(2 << 1)); // raw line of two pixels
            delt.AddRange(LE16(1));
            delt.AddRange(LE16(0));
            delt.AddRange(new byte[] { 10, 11 });
            delt.AddRange(LE16((4 << 1) | 1)); // RLE line of four pixels
            delt.AddRange(LE16(0));
            delt.AddRange(LE16(2));
            delt.AddRange(new byte[] { (3 << 1) | 1, 200, 1 << 1, 200 }); // run of 3, then 1 literal
            delt.AddRange(LE16(0));
            anim.AddRange(LE32(delt.Count));
            anim.AddRange(delt);
        }

        var pilot = BuildLfd(("PLTT", "pilot", own.ToArray()), ("ANIM", "faces", anim.ToArray()), ("ANIM", "smlships", LE16(0)));
        var xwing = BuildLfd(("PLTT", "standard", standard.ToArray()));
        return (pilot, xwing);
    }

    private static byte[] BuildLfd(params (string Type, string Name, byte[] Data)[] resources)
    {
        var map = new List<byte>();
        var body = new List<byte>();
        foreach (var (type, name, data) in resources)
        {
            var header = Header(type, name, data.Length);
            map.AddRange(header);
            body.AddRange(header);
            body.AddRange(data);
        }

        var file = new List<byte>();
        file.AddRange(Header("RMAP", "resource", map.Count));
        file.AddRange(map);
        file.AddRange(body);
        return file.ToArray();

        static byte[] Header(string type, string name, int size)
        {
            var h = new byte[16];
            Encoding.ASCII.GetBytes(type).CopyTo(h, 0);
            Encoding.ASCII.GetBytes(name).CopyTo(h, 4);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(12), size);
            return h;
        }
    }

    /// <summary>2×2, 4 bits per pixel: red, (masked), blue, green.</summary>
    private static byte[] BuildDib4Bit()
    {
        var dib = new List<byte>();
        dib.AddRange(InfoHeader(2, 2, 4, colors: 16));
        for (var i = 0; i < 16; i++)
        {
            dib.AddRange(i switch
            {
                1 => new byte[] { 0, 0, 255, 0 }, // B, G, R: red
                2 => new byte[] { 255, 0, 0, 0 }, // blue
                3 => new byte[] { 0, 255, 0, 0 }, // green
                _ => new byte[4],
            });
        }

        // Rows bottom-up, padded to 4 bytes: bottom row blue, green; top row red, red.
        dib.AddRange(new byte[] { 0x23, 0, 0, 0 });
        dib.AddRange(new byte[] { 0x11, 0, 0, 0 });

        // Mask, bottom-up: bottom row opaque; top row: second pixel transparent.
        dib.AddRange(new byte[] { 0x00, 0, 0, 0 });
        dib.AddRange(new byte[] { 0x40, 0, 0, 0 });
        return dib.ToArray();
    }

    private static byte[] BuildDib32Bit(int size)
    {
        var dib = new List<byte>();
        dib.AddRange(InfoHeader(size, size, 32, colors: 0));
        for (var i = 0; i < size * size; i++)
        {
            dib.AddRange(new byte[] { 30, 20, 10, 128 });
        }

        dib.AddRange(new byte[((size + 31) / 32 * 4) * size]);
        return dib.ToArray();
    }

    private static byte[] InfoHeader(int width, int height, int bpp, int colors)
    {
        var h = new byte[40];
        BinaryPrimitives.WriteInt32LittleEndian(h, 40);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(8), height * 2);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(h.AsSpan(14), (short)bpp);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(32), colors);
        return h;
    }

    private static byte[] BuildIco(byte[] image)
    {
        var ico = new List<byte>();
        ico.AddRange(LE16(0));
        ico.AddRange(LE16(1));
        ico.AddRange(LE16(1));
        ico.AddRange(new byte[] { 2, 2, 0, 0 });
        ico.AddRange(LE16(1));
        ico.AddRange(LE16(4));
        ico.AddRange(LE32(image.Length));
        ico.AddRange(LE32(6 + 16));
        ico.AddRange(image);
        return ico.ToArray();
    }

    /// <summary>A minimal PE32 file whose .rsrc section holds one icon group with the given icons (ids 1, 2, …).</summary>
    private static byte[] BuildPe(params byte[][] icons)
    {
        const int sectionRaw = 0x200;
        const int sectionRva = 0x1000;

        // Resource tree: root → (RT_ICON, RT_GROUP_ICON) → id → language → data entry.
        var rsrc = new byte[0x2000];
        var dataAt = 0x400; // where resource data starts inside the section
        var next = 0; // where the next directory goes
        var entries = new List<(int Type, int Id, byte[] Data)>();
        for (var i = 0; i < icons.Length; i++)
        {
            entries.Add((3, i + 1, icons[i]));
        }

        var group = new List<byte>();
        group.AddRange(LE16(0));
        group.AddRange(LE16(1));
        group.AddRange(LE16(icons.Length));
        for (var i = 0; i < icons.Length; i++)
        {
            var width = BinaryPrimitives.ReadInt32LittleEndian(icons[i].AsSpan(4));
            group.AddRange(new[] { (byte)width, (byte)width, (byte)0, (byte)0 });
            group.AddRange(LE16(1));
            group.AddRange(LE16(BinaryPrimitives.ReadInt16LittleEndian(icons[i].AsSpan(14))));
            group.AddRange(LE32(icons[i].Length));
            group.AddRange(LE16(i + 1));
        }

        entries.Add((14, 1, group.ToArray()));

        int Directory(int count)
        {
            var at = next;
            BinaryPrimitives.WriteUInt16LittleEndian(rsrc.AsSpan(at + 14), (ushort)count);
            next += 16 + count * 8;
            return at;
        }

        void Entry(int directory, int index, int id, int target, bool isDirectory)
        {
            var e = directory + 16 + index * 8;
            BinaryPrimitives.WriteUInt32LittleEndian(rsrc.AsSpan(e), (uint)id);
            BinaryPrimitives.WriteUInt32LittleEndian(rsrc.AsSpan(e + 4), (uint)target | (isDirectory ? 0x80000000 : 0));
        }

        var types = entries.GroupBy(e => e.Type).OrderBy(g => g.Key).ToList();
        var root = Directory(types.Count);
        for (var t = 0; t < types.Count; t++)
        {
            var items = types[t].ToList();
            var typeDir = Directory(items.Count);
            Entry(root, t, types[t].Key, typeDir, true);
            for (var i = 0; i < items.Count; i++)
            {
                var langDir = Directory(1);
                Entry(typeDir, i, items[i].Id, langDir, true);
                var leaf = next;
                next += 16;
                Entry(langDir, 0, 0x409, leaf, false);
                BinaryPrimitives.WriteUInt32LittleEndian(rsrc.AsSpan(leaf), (uint)(sectionRva + dataAt));
                BinaryPrimitives.WriteInt32LittleEndian(rsrc.AsSpan(leaf + 4), items[i].Data.Length);
                items[i].Data.CopyTo(rsrc, dataAt);
                dataAt += (items[i].Data.Length + 7) & ~7;
            }
        }

        var exe = new byte[sectionRaw + rsrc.Length];
        exe[0] = (byte)'M';
        exe[1] = (byte)'Z';
        const int pe = 0x80;
        BinaryPrimitives.WriteInt32LittleEndian(exe.AsSpan(0x3C), pe);
        Encoding.ASCII.GetBytes("PE\0\0").CopyTo(exe, pe);
        BinaryPrimitives.WriteUInt16LittleEndian(exe.AsSpan(pe + 4), 0x14C);
        BinaryPrimitives.WriteUInt16LittleEndian(exe.AsSpan(pe + 6), 1); // one section
        BinaryPrimitives.WriteUInt16LittleEndian(exe.AsSpan(pe + 20), 224); // optional header size (PE32)
        var optional = pe + 24;
        BinaryPrimitives.WriteUInt16LittleEndian(exe.AsSpan(optional), 0x10B);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(optional + 96 + 16), sectionRva); // resource directory
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(optional + 96 + 20), (uint)rsrc.Length);
        var section = optional + 224;
        Encoding.ASCII.GetBytes(".rsrc").CopyTo(exe, section);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(section + 8), (uint)rsrc.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(section + 12), sectionRva);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(section + 16), (uint)rsrc.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(exe.AsSpan(section + 20), sectionRaw);
        rsrc.CopyTo(exe, sectionRaw);
        return exe;
    }

    /// <summary>A minimal ISO 9660 image with one folder level, in 2,048- or raw 2,352-byte sectors.</summary>
    private static byte[] BuildIso(int sectorSize, int dataOffset, params (string Folder, string Name, byte[] Data)[] files)
    {
        var sectors = new Dictionary<int, byte[]>();
        const int rootLba = 18, folderLba = 19;
        var lba = 20;
        var fileRecords = new List<byte>();
        fileRecords.AddRange(Record(folderLba, 2048, true, "\0"));
        fileRecords.AddRange(Record(rootLba, 2048, true, "\u0001"));
        foreach (var (_, name, data) in files)
        {
            fileRecords.AddRange(Record(lba, data.Length, false, name + ";1"));
            for (var i = 0; i * 2048 < data.Length; i++)
            {
                sectors[lba++] = data.Skip(i * 2048).Take(2048).ToArray();
            }
        }

        var root = new List<byte>();
        root.AddRange(Record(rootLba, 2048, true, "\0"));
        root.AddRange(Record(rootLba, 2048, true, "\u0001"));
        root.AddRange(Record(folderLba, 2048, true, files[0].Folder));
        sectors[rootLba] = root.ToArray();
        sectors[folderLba] = fileRecords.ToArray();

        var pvd = new byte[2048];
        pvd[0] = 1;
        Encoding.ASCII.GetBytes("CD001").CopyTo(pvd, 1);
        Record(rootLba, 2048, true, "\0").CopyTo(pvd, 156);
        sectors[16] = pvd;

        var image = new byte[lba * sectorSize];
        foreach (var (at, data) in sectors)
        {
            data.CopyTo(image, (at * sectorSize) + dataOffset);
        }

        return image;

        static byte[] Record(int extent, int size, bool directory, string name)
        {
            var length = 33 + name.Length + ((33 + name.Length) % 2);
            var r = new byte[length];
            r[0] = (byte)length;
            BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(2), extent);
            BinaryPrimitives.WriteInt32LittleEndian(r.AsSpan(10), size);
            r[25] = directory ? (byte)2 : (byte)0;
            r[32] = (byte)name.Length;
            Encoding.ASCII.GetBytes(name).CopyTo(r, 33);
            return r;
        }
    }

    private static byte[] LE16(int value)
    {
        var b = new byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(b, (short)value);
        return b;
    }

    private static byte[] LE32(int value)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b, value);
        return b;
    }
}
