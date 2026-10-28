using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication.Engine;

namespace Aptabase.UnitTests.Features.Symbolication;

public class ManagedNameMatcherTests
{
    // Names as NativeAOT emits them, including the lookalikes that must not match
    private static readonly FunctionTable Table = new([
        new FunctionSymbol(0x435ec0, 0x28, "BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0"),
        new FunctionSymbol(0x037d18, 0x10, "__DelegateCtor_S_P_CoreLib_System_Delegate__InitializeClosedInstance__BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0"),
        new FunctionSymbol(0x2b7a10, 0x1a7, "BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest__Trigger"),
        new FunctionSymbol(0xc106a0, 0x1bc, "S_P_CoreLib_System_Threading_Thread__StartThread"),
        new FunctionSymbol(0x500000, 0x40, "GodotSharp_Godot_ColorRect___ctor"),
        new FunctionSymbol(0x500040, 0x40, "GodotSharp_Godot_ColorRect___ctor_0"),
        new FunctionSymbol(0xd54b30, 0x30, "S_P_CoreLib_System_Collections_Generic_List_1<BallsyDrop_BallsyDrop_Tutorials_TutorialStateId>__Add"),
    ]);

    [Theory]
    [InlineData("BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0", "BallsyDrop_Analytics_ErrorReportingTest___c___Trigger_b__0_0")]
    [InlineData("System.Threading.Thread.StartThread", "System_Threading_Thread__StartThread")]
    [InlineData("Godot.ColorRect..ctor", "Godot_ColorRect___ctor")]
    [InlineData("System.Collections.Generic.List`1[System.__Canon].Add", "System_Collections_Generic_List_1__Add")]
    [InlineData("SampleGame.Main+<RunCrashMode>d__9.MoveNext", "SampleGame_Main__RunCrashMode_d__9__MoveNext")]
    [InlineData("NoMethod", null)]
    public void MangledSuffix(string traceName, string? expected)
    {
        ManagedNameMatcher.MangledSuffix(traceName).Should().Be(expected);
    }

    [Theory]
    [InlineData("BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0", "ErrorReportingTest.<>c.<Trigger>b__0_0")]
    [InlineData("System.Threading.Thread.StartThread", "Thread.StartThread")]
    [InlineData("Godot.ColorRect..ctor", "ColorRect..ctor")]
    public void ShortName(string traceName, string expected)
    {
        ManagedNameMatcher.ShortName(traceName).Should().Be(expected);
    }

    [Fact]
    public void Finds_A_Lambda_And_Ignores_Lookalikes()
    {
        var match = ManagedNameMatcher.Find(Table, "BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0", 0x20);

        match.Function!.Address.Should().Be(0x435ec0);
        match.CandidateCount.Should().Be(1);
    }

    [Fact]
    public void Finds_A_Method_Of_Another_Assembly()
    {
        ManagedNameMatcher.Find(Table, "System.Threading.Thread.StartThread", 0xd8).Function!.Address.Should().Be(0xc106a0);
    }

    [Fact]
    public void Overloads_Are_Candidates_When_The_Offset_Fits()
    {
        var match = ManagedNameMatcher.Find(Table, "Godot.ColorRect..ctor", 0x10);

        match.Function.Should().NotBeNull();
        match.CandidateCount.Should().Be(2);
    }

    [Fact]
    public void Generic_Instantiations_Match_Without_Their_Arguments()
    {
        ManagedNameMatcher.Find(Table, "System.Collections.Generic.List`1.Add", 0x8).Function!.Address.Should().Be(0xd54b30);
    }

    [Fact]
    public void An_Offset_Outside_The_Method_Is_Rejected()
    {
        var match = ManagedNameMatcher.Find(Table, "BallsyDrop.Analytics.ErrorReportingTest.<>c.<Trigger>b__0_0", 0x30);

        match.Function.Should().BeNull();
        match.Reason.Should().Contain("outside");
    }

    [Fact]
    public void An_Unknown_Method_Has_A_Reason()
    {
        var match = ManagedNameMatcher.Find(Table, "BallsyDrop.Nope.Missing", 0x8);

        match.Function.Should().BeNull();
        match.Reason.Should().Be("No symbol for Nope.Missing in the game's symbols");
    }
}
