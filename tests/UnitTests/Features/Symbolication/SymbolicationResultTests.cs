using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication.Engine;
using Aptabase.Features.Symbolication.Store;

namespace Aptabase.UnitTests.Features.Symbolication;

public class SymbolicationResultTests
{
    private static SymbolicationResult Sample() => new()
    {
        BuildId = "1",
        Rid = "linux-x64",
        TotalFrames = 3,
        ResolvedFrames = 2,
        Lines =
        [
            new ResultLine { Text = "Build: 1 (linux-x64)" },
            new ResultLine
            {
                Text = "[2] 5f (main+10) - ??",
                Frame = new ResultFrame
                {
                    Kind = "native", Index = 2, Module = "main", ModuleRole = ModuleRole.Engine, Offset = "10", Status = FrameStatus.Resolved,
                    Symbols =
                    [
                        new ResultSymbol { Function = "List::List", RawFunction = "List::List", Location = "core/list.h:253", Inlined = true },
                        new ResultSymbol { Function = "CoreBind::OS::crash(String const&)", RawFunction = "x", Location = "core/core_bind.cpp:349" },
                    ],
                },
            },
            new ResultLine
            {
                Text = "[3] 7f (libc.so.6+29d8b) - libc.so.6(__libc_start_main+0x8b) [0x7f]",
                Frame = new ResultFrame { Kind = "native", Index = 3, Module = "libc.so.6", Offset = "29d8b", Status = FrameStatus.Named, ReportedName = "__libc_start_main+0x8b" },
            },
            new ResultLine
            {
                Text = "   at Game!<BaseAddress>+0x10",
                Frame = new ResultFrame { Kind = "managed", Module = "Game", Offset = "0x10", Reason = "No symbols uploaded for build 1 (linux-x64)" },
            },
        ],
    };

    [Fact]
    public void Text_Replaces_Frames_With_Their_Symbols()
    {
        Sample().ToText().Should().Be(
            """
            Build: 1 (linux-x64)
            [2] List::List  core/list.h:253  [main]  (inlined)
                CoreBind::OS::crash(String const&)  core/core_bind.cpp:349  [main]
            [3] __libc_start_main+0x8b  (named in the report)  [libc.so.6]
               at Game+0x10  (No symbols uploaded for build 1 (linux-x64))  [Game]

            """.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData(0, 0, SymbolicationStatus.None)]
    [InlineData(3, 3, SymbolicationStatus.Complete)]
    [InlineData(3, 1, SymbolicationStatus.Partial)]
    [InlineData(3, 0, SymbolicationStatus.Unresolved)]
    public void Status(int total, int resolved, string expected)
    {
        new SymbolicationResult { TotalFrames = total, ResolvedFrames = resolved }.Status.Should().Be(expected);
    }

    [Fact]
    public void Json_Round_Trip()
    {
        var json = Sample().ToJson();

        json.Should().Contain("\"moduleRole\":\"engine\"");
        SymbolicationResult.FromJson(json)!.ToText().Should().Be(Sample().ToText());
    }

    [Theory]
    [InlineData("linux-x64/BallsyDrop.so.dbg", SymbolFormats.Elf, "x86_64", "linux-x64")]
    [InlineData("godot.linuxbsd.template_release.x86_64.llvm.mono.debugsymbols", SymbolFormats.Elf, "x86_64", "linux-x64")]
    [InlineData("win-x64/BallsyDrop.pdb", SymbolFormats.Pdb, null, "win-x64")]
    [InlineData("godot.windows.template_release.x86_64.mono.pdb", SymbolFormats.Pdb, null, null)]
    [InlineData("osx-arm64/BallsyDrop.dylib.dSYM/Contents/Resources/DWARF/BallsyDrop.dylib", SymbolFormats.MachO, "x86_64", "osx-x64")]
    public void Rid_Of_A_Symbol_File(string path, string format, string? arch, string? expected)
    {
        SymbolIndexer.ResolveRid(path, new SymbolIdentity("ID", format, arch, 0)).Should().Be(expected);
    }
}
