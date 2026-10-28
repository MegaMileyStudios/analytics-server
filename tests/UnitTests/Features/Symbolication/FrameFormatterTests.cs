using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication.Engine;

namespace Aptabase.UnitTests.Features.Symbolication;

public class FrameFormatterTests
{
    [Theory]
    [InlineData("BallsyDrop_BallsyDrop_UI_Main_MainMenu___UnhandledInput", "MainMenu._UnhandledInput")]
    [InlineData("S_P_CoreLib_System_Threading_Thread__StartThread", "Thread.StartThread")]
    [InlineData("GodotSharp_Godot_NativeCalls__godot_icall_1_57", "NativeCalls.godot_icall_1_57")]
    [InlineData("S_P_CoreLib_System_Collections_Generic_List_1<System___Canon>__Add", "List_1.Add")]
    [InlineData("CoreBind::OS::crash(class String const &)", "CoreBind::OS::crash(String const &)")]
    [InlineData("Callable::call<struct Ref<enum Foo>>(void)", "Callable::call<Ref<Foo>>(void)")]
    [InlineData("__libc_start_main", "__libc_start_main")]
    [InlineData("godotsharp_game_main_init", "godotsharp_game_main_init")]
    [InlineData("main", "main")]
    public void PrettyFunction(string raw, string expected)
    {
        FrameFormatter.PrettyFunction(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData("BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest__Trigger", true)]
    [InlineData("CoreBind::OS::crash(String const&)", false)]
    [InlineData("__cxa_throw", false)]
    [InlineData("main", false)]
    public void IsManaged(string name, bool expected)
    {
        FrameFormatter.IsManaged(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("BallsyDrop_BallsyDrop_Analytics_ErrorReportingTest__Trigger", "/workspace/x/Scripts/A.cs", true)]
    [InlineData("S_P_CoreLib_System_Threading_Thread__StartThread", "/_/src/coreclr/Thread.cs", true)]
    [InlineData("GodotSharp_Godot_Node__InvokeGodotClassMethod", "/workspace/x/Scripts/Feedback/FeedbackProvider.cs", false)]
    [InlineData("GodotSharp_Godot_Node__InvokeGodotClassMethod", null, false)]
    [InlineData("CoreBind::OS::crash(String const&)", "core/core_bind.cpp", true)]
    public void Only_Trusted_Managed_Lines_Are_Shown(string function, string? file, bool expected)
    {
        FrameFormatter.HasTrustedSource(function, file, "BallsyDrop").Should().Be(expected);
    }

    [Fact]
    public void The_Game_Assembly_Is_Matched_Without_Case()
    {
        // Windows crash reports name the module ballsydrop.dll
        FrameFormatter.HasTrustedSource("BallsyDrop_BallsyDrop_UI_Main_MainMenu___UnhandledInput", "/x/MainMenu.cs", "ballsydrop").Should().BeTrue();
    }

    [Theory]
    [InlineData("/workspace/MegaMileyStudios/godot-custom/core/core_bind.cpp", "core/core_bind.cpp")]
    [InlineData("/workspace/MegaMileyStudios/BallsyDrop/Scripts/Analytics/ErrorReportingTest.cs", "Scripts/Analytics/ErrorReportingTest.cs")]
    [InlineData(@"C:\Users\NAS\.cache\act\53d8fc40397df578\hostexecutor\core\core_bind.cpp", "core/core_bind.cpp")]
    [InlineData("/Users/runner/.cache/act/882c607be22e55b4/hostexecutor/Scripts/UI/Main/MainMenu.cs", "Scripts/UI/Main/MainMenu.cs")]
    [InlineData("./core/object/object.h", "core/object/object.h")]
    [InlineData("/workspace/MegaMileyStudios/BallsyDrop/.godot/mono/temp/obj/ExportRelease/linux-x64/Godot.SourceGenerators/MainMenu_ScriptMethods.generated.cs", "MainMenu_ScriptMethods.generated.cs")]
    [InlineData("D:/a/_work/1/s/src/vctools/crt/vcstartup/src/startup/exe_common.inl", "D:/a/_work/1/s/src/vctools/crt/vcstartup/src/startup/exe_common.inl")]
    public void PrettyPath(string path, string expected)
    {
        FrameFormatter.PrettyPath(path).Should().Be(expected);
    }

    [Fact]
    public void Location_Adds_The_Line()
    {
        FrameFormatter.Location(new SymbolizedLocation("f", "/workspace/a/b/core/x.cpp", 349, 0)).Should().Be("core/x.cpp:349");
        FrameFormatter.Location(new SymbolizedLocation("f", "core/x.cpp", 0, 0)).Should().Be("core/x.cpp");
        FrameFormatter.Location(new SymbolizedLocation("f", null, 12, 0)).Should().BeNull();
    }

    [Theory]
    [InlineData("libc.so.6(__libc_start_main+0x8b) [0x7f24f8029d8b]", "__libc_start_main+0x8b")]
    [InlineData("libc.so.6(+0x45330) [0x7f24f8045330]", null)]
    [InlineData("<couldn't map PC to fn name>", null)]
    [InlineData("??", null)]
    [InlineData("_sigtramp + 56", "_sigtramp + 56")]
    [InlineData("-[NSApplication run] + 368", "-[NSApplication run] + 368")]
    [InlineData(null, null)]
    public void ReportedName(string? reported, string? expected)
    {
        FrameFormatter.ReportedName(reported).Should().Be(expected);
    }

    [Theory]
    [InlineData("BallsyDrop.so.dbg", "BallsyDrop")]
    [InlineData("BallsyDrop.pdb", "BallsyDrop")]
    [InlineData("BallsyDrop.dylib", "BallsyDrop")]
    [InlineData("BallsyDrop.dll", "BallsyDrop")]
    [InlineData("BallsyDrop.x86_64", "BallsyDrop")]
    [InlineData("SampleGame", "SampleGame")]
    public void AssemblyName(string module, string expected)
    {
        FrameFormatter.AssemblyName(module).Should().Be(expected);
    }
}
