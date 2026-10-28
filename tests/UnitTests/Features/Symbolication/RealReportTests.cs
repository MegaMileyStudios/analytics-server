using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication;
using Aptabase.Features.Symbolication.Engine;

namespace Aptabase.UnitTests.Features.Symbolication;

// Native crash reports BallsyDrop sent through the SDK on each platform (tests/Fixtures/Reports)
public class RealReportTests
{
    private static CrashReport Load(string name) =>
        CrashReportParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Reports", name)));

    [Theory]
    [InlineData("crash-linux.txt", ReportPlatform.Linux, "0-5-0-1793183957-Full_Release", 29, 3)]
    [InlineData("crash-windows.txt", ReportPlatform.Windows, "0-5-0-1793183957-Full_Release", 31, 3)]
    [InlineData("crash-macos.txt", ReportPlatform.MacOS, "0-5-0-1793188397-Full_Release", 39, 7)]
    public void Parses_Every_Frame_And_Module(string file, ReportPlatform platform, string buildId, int frames, int modules)
    {
        var report = Load(file);

        report.Platform.Should().Be(platform);
        report.BuildId.Should().Be(buildId);
        report.Lines.Count(l => l.Native is not null).Should().Be(frames);
        report.Modules.Should().HaveCount(modules);
        report.Modules.Values.Should().OnlyContain(m => m.DebugId != null);
        report.HasManagedFrames.Should().BeFalse();
    }

    [Fact]
    public void Windows_Frames_Use_The_Module_Name_Of_The_Modules_List()
    {
        var report = Load("crash-windows.txt");

        // The crash handler lower-cases the game library, in the frames and in the Modules list alike
        var gameFrames = report.Lines.Select(l => l.Native).OfType<NativeFrame>().Where(f => f.Module == "ballsydrop.dll").ToArray();
        gameFrames.Should().HaveCount(8);
        report.Modules["ballsydrop.dll"].DebugId.Should().Be("5D208BED43854D76B91423A570D498C71");
        report.Modules["ballsydrop.dll"].File.Should().Be("BallsyDrop.dll");
    }

    [Fact]
    public void Script_Backtraces_Stay_Text()
    {
        var report = Load("crash-macos.txt");

        report.Lines.Should().Contain(l => l.Text == "    [6]" && l.Native == null && l.Managed == null);
        report.Lines.Last().Text.Should().Be("-- END OF C# BACKTRACE --");
    }

    [Fact]
    public void The_Game_Library_Is_Recognized_By_Its_Export_On_MacOS()
    {
        var report = Load("crash-macos.txt");

        report.Lines.Select(l => l.Native).OfType<NativeFrame>()
            .Where(f => f.Module == "BallsyDrop.dylib")
            .Should().OnlyContain(f => f.Reported!.StartsWith("godotsharp_game_main_init"));
    }

    [Fact]
    public void Parses_The_Managed_Exception()
    {
        var report = Load("exception-macos.txt");

        report.HasManagedFrames.Should().BeTrue();
        report.Lines.Select(l => l.Managed).OfType<ManagedFrame>().Select(f => f.MethodName).Should().Equal(
            "BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0",
            "System.Threading.Thread.StartThread",
            "System.Threading.Thread.ThreadEntryPoint");
    }
}
