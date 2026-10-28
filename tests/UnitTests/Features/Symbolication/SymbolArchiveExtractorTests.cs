using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication.Store;

namespace Aptabase.UnitTests.Features.Symbolication;

public class SymbolArchiveExtractorTests : IDisposable
{
    private readonly string _target = Path.Combine(Path.GetTempPath(), $"symbols-test-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_target))
            Directory.Delete(_target, recursive: true);
    }

    [Theory]
    [InlineData("./linux-x64/BallsyDrop.so.dbg", "linux-x64/BallsyDrop.so.dbg")]
    [InlineData("win-x64\\BallsyDrop.pdb", "win-x64/BallsyDrop.pdb")]
    [InlineData("a//b/./c", "a/b/c")]
    public void Sanitizes_Paths(string entry, string expected)
    {
        SymbolArchiveExtractor.SanitizeEntryPath(entry, out var reason).Should().Be(expected);
        reason.Should().BeNull();
    }

    [Theory]
    [InlineData("../etc/passwd")]
    [InlineData("a/../../b")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\x.dll")]
    public void Rejects_Paths_Outside_The_Archive(string entry)
    {
        SymbolArchiveExtractor.SanitizeEntryPath(entry, out var reason).Should().BeNull();
        reason.Should().NotBeNull();
    }

    [Theory]
    [InlineData("./")]
    [InlineData("._.")]
    [InlineData("osx-arm64/._BallsyDrop.dylib")]
    [InlineData("a/.DS_Store")]
    [InlineData("__MACOSX/a/b")]
    public void Ignores_The_Root_And_MacOS_Metadata_Silently(string entry)
    {
        SymbolArchiveExtractor.SanitizeEntryPath(entry, out var reason).Should().BeNull();
        reason.Should().BeNull();
    }

    [Fact]
    public async Task Extracts_Regular_Files_Only()
    {
        var archive = Archive(tar =>
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "./linux-x64/"));
            tar.WriteEntry(File("./linux-x64/BallsyDrop.so.dbg", "debug"));
            tar.WriteEntry(File("./._BallsyDrop.so", "xattrs"));
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "./link") { LinkName = "/etc/passwd" });
            tar.WriteEntry(File("../escape", "nope"));
        });

        var result = await SymbolArchiveExtractor.ExtractAsync(archive, _target, 1024, 100, CancellationToken.None);

        result.Files.Should().ContainSingle().Which.RelativePath.Should().Be("linux-x64/BallsyDrop.so.dbg");
        (await System.IO.File.ReadAllTextAsync(Path.Combine(_target, "linux-x64", "BallsyDrop.so.dbg"))).Should().Be("debug");
        result.Skipped.Select(s => s.Path).Should().BeEquivalentTo(["link", "../escape"]);
        System.IO.File.Exists(Path.Combine(Path.GetDirectoryName(_target)!, "escape")).Should().BeFalse();
    }

    [Fact]
    public async Task Refuses_Archives_That_Unpack_Too_Large()
    {
        var archive = Archive(tar => tar.WriteEntry(File("big", new string('x', 2000))));

        var extract = () => SymbolArchiveExtractor.ExtractAsync(archive, _target, 1024, 100, CancellationToken.None);

        await extract.Should().ThrowAsync<SymbolUploadException>().WithMessage("*unpacks to more than*");
    }

    [Fact]
    public async Task Refuses_Data_That_Is_Not_A_Tar_Gz()
    {
        var extract = () => SymbolArchiveExtractor.ExtractAsync(new MemoryStream(Encoding.UTF8.GetBytes("not an archive")), _target, 1024, 100, CancellationToken.None);

        await extract.Should().ThrowAsync<SymbolUploadException>().WithMessage("*valid .tar.gz*");
    }

    private static PaxTarEntry File(string name, string content) =>
        new(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)) };

    private static MemoryStream Archive(Action<TarWriter> write)
    {
        var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax))
            write(tar);
        output.Position = 0;
        return output;
    }
}
