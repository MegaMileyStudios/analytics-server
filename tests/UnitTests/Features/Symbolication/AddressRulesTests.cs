using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Symbolication;
using Aptabase.Features.Symbolication.Engine;

namespace Aptabase.UnitTests.Features.Symbolication;

public class AddressRulesTests
{
    [Theory]
    [InlineData(ReportPlatform.Linux, 2, 0x1000ul)]
    [InlineData(ReportPlatform.Linux, 3, 0x0FFFul)]
    [InlineData(ReportPlatform.MacOS, 2, 0x1000ul)]
    [InlineData(ReportPlatform.MacOS, 5, 0x0FFFul)]
    [InlineData(ReportPlatform.Windows, 0, 0x1000ul)]
    [InlineData(ReportPlatform.Windows, 1, 0x0FFFul)]
    public void The_Faulting_Frame_Is_Exact_And_Return_Addresses_Step_Back(ReportPlatform platform, int index, ulong expected)
    {
        AddressRules.NativeAddress(platform, new NativeFrame(index, "main", 0x1000, null), 0).Should().Be(expected);
    }

    [Fact]
    public void The_Image_Base_Is_Added()
    {
        AddressRules.NativeAddress(ReportPlatform.Windows, new NativeFrame(1, "BallsyDrop.dll", 0x1ab480, null), 0x140000000)
            .Should().Be(0x1401ab47F);
    }

    [Theory]
    [InlineData(ReportPlatform.Linux, 1, true)]
    [InlineData(ReportPlatform.Linux, 2, false)]
    [InlineData(ReportPlatform.MacOS, 0, true)]
    [InlineData(ReportPlatform.MacOS, 1, true)]
    [InlineData(ReportPlatform.MacOS, 2, false)]
    [InlineData(ReportPlatform.Windows, 0, false)]
    public void Crash_Handler_Frames(ReportPlatform platform, int index, bool expected)
    {
        AddressRules.IsCrashHandlerFrame(platform, index).Should().Be(expected);
    }

    [Fact]
    public void Managed_Frames_Are_Return_Addresses()
    {
        AddressRules.ManagedAddress(0x435ec0, 0x30).Should().Be(0x435eef);
        AddressRules.ManagedAddress(0x435ec0, 0).Should().Be(0x435ec0);
    }
}
