using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Ingestion;

namespace Aptabase.UnitTests.Features.Ingestion;

public class TimezoneOffsetTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(120)]
    [InlineData(-300)]
    [InlineData(330)] // UTC+5:30
    [InlineData(-720)] // UTC-12
    [InlineData(840)] // UTC+14
    public void Should_keep_offsets_in_range(int value)
    {
        TimezoneOffset.Normalize(value).Should().Be((short)value);
    }

    [Theory]
    [InlineData(-721)]
    [InlineData(841)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Should_drop_offsets_out_of_range(int value)
    {
        TimezoneOffset.Normalize(value).Should().BeNull();
    }

    [Fact]
    public void Should_treat_missing_offset_as_absent()
    {
        TimezoneOffset.Normalize(null).Should().BeNull();
    }
}
