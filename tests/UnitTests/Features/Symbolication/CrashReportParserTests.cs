using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication;
using Aptabase.Features.Symbolication.Engine;

namespace Aptabase.UnitTests.Features.Symbolication;

public class CrashReportParserTests
{
    // Shaped like what the SDK sends for a native crash on Linux (paths already reduced to file names)
    public const string LinuxCrash =
        """
        Build: 0-5-0-1793183957-Full_Release (linux-x64)
        handle_crash: Program crashed with signal 4
        Engine version: Godot Engine v4.7.3.stable.mono.custom_build (1c9ca03b7)
        Dumping the backtrace. Please include this when reporting the bug to the project developer.
        Load address: 5fdd5ea17000

        [1] 7f24f8045330 (libc.so.6+45330) - libc.so.6(+0x45330) [0x7f24f8045330]
        [2] 5fdd60ea3b70 (main+248cb70) - BallsyDrop.x86_64(+0x248cb70) [0x5fdd60ea3b70]
        [3] 7f2465a984d0 (BallsyDrop.so+4984d0) - BallsyDrop.so(+0x4984d0) [0x7f2465a984d0]
        [4] 7f24f8029d8b (libc.so.6+29d8b) - libc.so.6(__libc_start_main+0x8b) [0x7f24f8029d8b]

        Modules:
        libc.so.6 (libc.so.6) - base: 7f24f8000000, build ID: 42c84c92e6f98126b3e2230ebfdead22c235b667
        main (BallsyDrop.x86_64) - base: 5fdd5ea17000, build ID: 3128a633b44803a29dbeb2facfa7b764ec4ff026
        BallsyDrop.so (BallsyDrop.so) - base: 7f2465600000, build ID: c4a9dc3c93f34990366feedf5dd3265549f37070
        -- END OF C++ BACKTRACE --
        C# backtrace (most recent call first):
            [0] void BallsyDrop.Analytics.ErrorReportingTest.Trigger(Godot.InputEvent)
        -- END OF C# BACKTRACE --

        """;

    public const string WindowsCrash =
        """
        Build: 0-5-0-1793183957-Full_Release (win-x64)
        [0] 00007ff6c8a41234 (main+1ede29c) - <couldn't map PC to fn name>
        [1] 00007ffad01ab480 (BallsyDrop.dll+1ab480) - <couldn't map PC to fn name>
        [2] 00007ffb1a2b7344 (kernel32.dll+17344) - <couldn't map PC to fn name>

        Modules:
        main (BallsyDrop.exe) - base: 7ff6c6b60000, PDB: godot.windows.template_release.x86_64.mono.pdb, PDB ID: 61D3E1A84C9141A2A6365AFCA3C2BC751
        BallsyDrop.dll (BallsyDrop.dll) - base: 7ffad0000000, PDB: BallsyDrop.pdb, PDB ID: 5D208BED43854D76B91423A570D498C71
        kernel32.dll (kernel32.dll) - base: 7ffb1a2a0000
        """;

    public const string MacOSCrash =
        """
        Build: 0-5-0-1793188397-Full_Release (osx-arm64)
        [0] 102b95f00 (main+2095f00) - ??
        [1] 18c3d2de4 (libsystem_platform.dylib+3de4) - _sigtramp + 56
        [2] 102b95b78 (main+2095b78) - ??
        [3] 10bc2fb60 (BallsyDrop.dylib+22fb60) - godotsharp_game_main_init + 1234567

        Modules:
        main (BallsyDrop) - base: 100b00000, UUID: EB16A950-A45C-3AFB-90B6-FF978C7AF619
        BallsyDrop.dylib (BallsyDrop.dylib) - base: 10ba00000, UUID: D5AFCC57-C739-3F1E-A482-E9F5B2BF9887
        """;

    public const string NamedManagedException =
        """
        Build: 0-5-0-1793188397-Full_Release (osx-arm64)
        System.InvalidOperationException: Error reporting test: unhandled exception on a background thread
           at BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0() + 0x30
           at System.Threading.Thread.StartThread(IntPtr) + 0xd8
           --- End of stack trace from previous location ---
        """;

    public const string AddressOnlyManagedException =
        """
        Build: 20260930T143520Z (linux-x64)
        System.InvalidOperationException: Sample exception thrown in a Godot callback after 0 click(s).
           at SampleGame!<BaseAddress>+0x6fd9ef
           at SampleGame!<BaseAddress>+0xfef1a
        """;

    [Fact]
    public void Parses_The_Build_Header()
    {
        var report = CrashReportParser.Parse(LinuxCrash);

        report.BuildId.Should().Be("0-5-0-1793183957-Full_Release");
        report.Rid.Should().Be("linux-x64");
        report.Platform.Should().Be(ReportPlatform.Linux);
    }

    [Fact]
    public void Parses_A_Build_Header_Without_Build_Id()
    {
        var report = CrashReportParser.Parse("Build: (win-x64)\n[0] 1234 (main+10) - ??\n");

        report.BuildId.Should().BeNull();
        report.Rid.Should().Be("win-x64");
        report.Platform.Should().Be(ReportPlatform.Windows);
    }

    [Fact]
    public void Parses_Native_Frames()
    {
        var frames = CrashReportParser.Parse(LinuxCrash).Lines.Select(l => l.Native).OfType<NativeFrame>().ToArray();

        frames.Should().HaveCount(4);
        frames[1].Should().Be(new NativeFrame(2, "main", 0x248cb70, "BallsyDrop.x86_64(+0x248cb70) [0x5fdd60ea3b70]"));
        frames[3].Module.Should().Be("libc.so.6");
        frames[3].Offset.Should().Be(0x29d8b);
    }

    [Fact]
    public void Keeps_Every_Line_Including_Empty_Ones()
    {
        var report = CrashReportParser.Parse(LinuxCrash);

        string.Join('\n', report.Lines.Select(l => l.Text)).Should().Be(LinuxCrash.TrimEnd('\n'));
    }

    [Fact]
    public void Script_Backtrace_Frames_Are_Not_Native_Frames()
    {
        var report = CrashReportParser.Parse(LinuxCrash);

        report.Lines.Single(l => l.Text.Contains("void BallsyDrop")).Native.Should().BeNull();
    }

    [Fact]
    public void Parses_Linux_Modules_With_Normalized_Build_Ids()
    {
        var modules = CrashReportParser.Parse(LinuxCrash).Modules;

        modules.Keys.Should().BeEquivalentTo(["libc.so.6", "main", "BallsyDrop.so"]);
        modules["main"].DebugId.Should().Be("3128A633B44803A29DBEB2FACFA7B764EC4FF026");
        modules["main"].File.Should().Be("BallsyDrop.x86_64");
    }

    [Fact]
    public void Parses_Windows_Modules_With_Pdb_Ids_And_Modules_Without_One()
    {
        var modules = CrashReportParser.Parse(WindowsCrash).Modules;

        modules["BallsyDrop.dll"].DebugId.Should().Be("5D208BED43854D76B91423A570D498C71");
        modules["kernel32.dll"].DebugId.Should().BeNull();
    }

    [Fact]
    public void Parses_MacOS_Uuids_Without_Dashes()
    {
        var report = CrashReportParser.Parse(MacOSCrash);

        report.Platform.Should().Be(ReportPlatform.MacOS);
        report.Modules["BallsyDrop.dylib"].DebugId.Should().Be("D5AFCC57C7393F1EA482E9F5B2BF9887");
        report.Lines.Select(l => l.Native).OfType<NativeFrame>().First(f => f.Index == 1).Reported.Should().Be("_sigtramp + 56");
    }

    [Fact]
    public void Parses_A_Frame_Whose_Address_Was_Redacted()
    {
        var report = CrashReportParser.Parse("Build: x (win-x64)\n[1] [CARD_REDACTED] (BallsyDrop.dll+1ab480) - <couldn't map PC to fn name>");

        report.Lines[1].Native.Should().Be(new NativeFrame(1, "BallsyDrop.dll", 0x1ab480, "<couldn't map PC to fn name>"));
    }

    [Fact]
    public void Parses_Module_Names_Containing_A_Plus()
    {
        var report = CrashReportParser.Parse("Build: x (linux-x64)\n[3] 7f00 (libstdc++.so.6+a1b2) - ??");

        report.Lines[1].Native!.Module.Should().Be("libstdc++.so.6");
        report.Lines[1].Native!.Offset.Should().Be(0xa1b2);
    }

    [Fact]
    public void Parses_Managed_Frames_With_Method_Names()
    {
        var report = CrashReportParser.Parse(NamedManagedException);
        var frames = report.Lines.Select(l => l.Managed).OfType<ManagedFrame>().ToArray();

        frames.Should().BeEquivalentTo([
            new ManagedFrame(0x30, "BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0", null),
            new ManagedFrame(0xd8, "System.Threading.Thread.StartThread", null),
        ]);
        report.HasManagedFrames.Should().BeTrue();
    }

    [Fact]
    public void Parses_Address_Only_Managed_Frames()
    {
        var frames = CrashReportParser.Parse(AddressOnlyManagedException).Lines.Select(l => l.Managed).OfType<ManagedFrame>().ToArray();

        frames.Should().BeEquivalentTo([
            new ManagedFrame(0x6fd9ef, null, "SampleGame"),
            new ManagedFrame(0xfef1a, null, "SampleGame"),
        ]);
    }

    [Fact]
    public void Managed_Frames_With_Source_Lines_Are_Left_As_Text()
    {
        var report = CrashReportParser.Parse("Build: x (linux-x64)\n   at Game.Main.Run() in /src/Main.cs:line 12");

        report.HasFrames.Should().BeFalse();
    }

    [Theory]
    [InlineData("Build: 123 (linux-x64)\nboom", true)]
    [InlineData("System.Exception: boom", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_Sdk_Reports_Are_Eligible(string? stackTrace, bool expected)
    {
        CrashReportParser.IsEligible(stackTrace).Should().Be(expected);
    }
}
