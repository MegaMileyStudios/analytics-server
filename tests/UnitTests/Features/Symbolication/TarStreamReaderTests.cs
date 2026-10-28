using System.Formats.Tar;
using System.Text;
using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication.Store;

namespace Aptabase.UnitTests.Features.Symbolication;

public class TarStreamReaderTests
{
    private const string LongPath = "godot.macos.template_release.arm64.mono.dSYM/Contents/Resources/DWARF/godot.macos.template_release.arm64.mono";

    [Theory]
    [InlineData(TarEntryFormat.Ustar)]
    [InlineData(TarEntryFormat.Pax)]
    [InlineData(TarEntryFormat.Gnu)]
    public async Task Reads_Long_Paths_In_Every_Format(TarEntryFormat format)
    {
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, format, leaveOpen: true))
        {
            writer.WriteEntry(Entry(format, TarEntryType.Directory, "a/"));
            writer.WriteEntry(Entry(format, RegularFileType(format), LongPath, "dwarf"));
            writer.WriteEntry(Entry(format, RegularFileType(format), "short.txt", new string('x', 1000)));
        }
        archive.Position = 0;

        var entries = await ReadAll(archive);

        entries.Should().Equal(("a/", TarEntryKind.Directory, ""), (LongPath, TarEntryKind.File, "dwarf"), ("short.txt", TarEntryKind.File, new string('x', 1000)));
    }

    // What bsdtar on macOS writes: a pax header with only extended attributes, then a ustar header with the path
    // split into the prefix and name fields. System.Formats.Tar drops the prefix for these.
    [Fact]
    public async Task Reads_The_Ustar_Prefix_Of_A_Pax_Entry()
    {
        var archive = new MemoryStream();
        const string record = " LIBARCHIVE.xattr.com.apple.provenance=AQIAvvUh7mE4h0s=\n";
        var pax = Encoding.ASCII.GetBytes($"{record.Length + 2}{record}");
        archive.Write(Header("PaxHeader/godot.macos.template_release.arm64.mono", 'x', pax.Length));
        archive.Write(Pad(pax));
        archive.Write(Header(LongPath[(LongPath.LastIndexOf('/') + 1)..], '0', 5, prefix: LongPath[..LongPath.LastIndexOf('/')]));
        archive.Write(Pad("dwarf"u8.ToArray()));
        archive.Write(new byte[1024]);
        archive.Position = 0;

        var entries = await ReadAll(archive);

        entries.Should().Equal((LongPath, TarEntryKind.File, "dwarf"));
    }

    [Fact]
    public async Task Rejects_Data_That_Is_Not_A_Tar()
    {
        var read = () => ReadAll(new MemoryStream(Enumerable.Repeat((byte)'a', 512).ToArray()));

        await read.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Skips_Data_The_Caller_Does_Not_Read()
    {
        var archive = new MemoryStream();
        using (var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: true))
        {
            writer.WriteEntry(Entry(TarEntryFormat.Pax, TarEntryType.RegularFile, "skipped", new string('y', 700)));
            writer.WriteEntry(Entry(TarEntryFormat.Pax, TarEntryType.RegularFile, "read", "data"));
        }
        archive.Position = 0;
        var reader = new TarStreamReader(archive);

        (await reader.NextAsync(CancellationToken.None))!.Path.Should().Be("skipped");
        var second = await reader.NextAsync(CancellationToken.None);
        var data = new MemoryStream();
        await reader.CopyDataToAsync(data, CancellationToken.None);

        second!.Path.Should().Be("read");
        Encoding.UTF8.GetString(data.ToArray()).Should().Be("data");
        (await reader.NextAsync(CancellationToken.None)).Should().BeNull();
    }

    private static async Task<List<(string, TarEntryKind, string)>> ReadAll(Stream archive)
    {
        var reader = new TarStreamReader(archive);
        var entries = new List<(string, TarEntryKind, string)>();
        while (await reader.NextAsync(CancellationToken.None) is { } entry)
        {
            var data = new MemoryStream();
            await reader.CopyDataToAsync(data, CancellationToken.None);
            entries.Add((entry.Path, entry.Kind, Encoding.UTF8.GetString(data.ToArray())));
        }
        return entries;
    }

    private static TarEntryType RegularFileType(TarEntryFormat format) => format == TarEntryFormat.V7 ? TarEntryType.V7RegularFile : TarEntryType.RegularFile;

    private static TarEntry Entry(TarEntryFormat format, TarEntryType type, string name, string? content = null)
    {
        TarEntry entry = format switch
        {
            TarEntryFormat.Ustar => new UstarTarEntry(type, name),
            TarEntryFormat.Gnu => new GnuTarEntry(type, name),
            _ => new PaxTarEntry(type, name),
        };
        if (content is not null)
            entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return entry;
    }

    private static byte[] Header(string name, char type, long size, string prefix = "")
    {
        var header = new byte[512];
        Encoding.ASCII.GetBytes(name).CopyTo(header, 0);
        Encoding.ASCII.GetBytes("0000644\0").CopyTo(header, 100);
        Encoding.ASCII.GetBytes(Convert.ToString(size, 8).PadLeft(11, '0') + "\0").CopyTo(header, 124);
        header[156] = (byte)type;
        "ustar\0"u8.CopyTo(header.AsSpan(257));
        "00"u8.CopyTo(header.AsSpan(263));
        Encoding.ASCII.GetBytes(prefix).CopyTo(header, 345);

        "        "u8.CopyTo(header.AsSpan(148));
        var checksum = header.Sum(b => (long)b);
        Encoding.ASCII.GetBytes(Convert.ToString(checksum, 8).PadLeft(6, '0') + "\0 ").CopyTo(header, 148);
        return header;
    }

    private static byte[] Pad(byte[] data) => [.. data, .. new byte[(512 - data.Length % 512) % 512]];
}
