using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Privacy;

namespace Aptabase.UnitTests.Features.Privacy;

public class PersistentUserIdTests
{
    private const string ValidHash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public void Should_accept_lowercase_hex_hash()
    {
        PersistentUserId.TryNormalize(ValidHash, out var normalized).Should().BeTrue();
        normalized.Should().Be(ValidHash);
    }

    [Fact]
    public void Should_normalize_uppercase_hex_hash_to_lowercase()
    {
        PersistentUserId.TryNormalize(ValidHash.ToUpperInvariant(), out var normalized).Should().BeTrue();
        normalized.Should().Be(ValidHash);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Should_treat_missing_value_as_absent(string? value)
    {
        PersistentUserId.TryNormalize(value, out var normalized).Should().BeTrue();
        normalized.Should().BeNull();
    }

    [Theory]
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a0")] // 63 chars
    [InlineData("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a080")] // 65 chars
    [InlineData("zf86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08")] // non-hex char
    [InlineData("9f86d081-884c-7d65-9a2f-eaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f0")] // dashes
    [InlineData("76561198000000000")] // raw Steam ID
    public void Should_reject_invalid_values(string value)
    {
        PersistentUserId.TryNormalize(value, out var normalized).Should().BeFalse();
        normalized.Should().BeNull();
    }
}
