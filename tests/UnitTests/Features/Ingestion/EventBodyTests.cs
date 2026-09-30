using Xunit;
using System.Text.Json;
using Aptabase.Features.Ingestion;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aptabase.UnitTests.Features.Ingestion;

public class EventBodyTests
{
    [Fact]
    public void Past_dates_should_bot_be_changed()
    {
        var ts = DateTime.UtcNow.AddMinutes(-10);
        var body = new EventBody { Timestamp = ts };

        body.Timestamp.Should().Be(ts);
    }

    [Fact]
    public void Future_dates_should_be_normalized_to_now()
    {
        var body = new EventBody { Timestamp = DateTime.UtcNow.AddDays(5) };

        body.Timestamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void SplitProps_should_ignore_nonprimitive_types()
    {
        var body = new EventBody
        {
            Timestamp = DateTime.UtcNow.AddDays(5),
            Props = JsonDocument.Parse(@"{
                ""name"": ""Bob"",
                ""age"": 10,
                ""isAdult"": true,
                ""array"": [1,2,3],
                ""object"": { ""foo"": ""bar"" }
            }")
        };

        var (stringProps, numericProps) = body.SplitProps();
        stringProps.Count.Should().Be(4);
        stringProps["name"]!.GetValue<string>().Should().Be("Bob");
        stringProps["isAdult"]!.GetValue<string>().Should().Be("true");
        stringProps["array"]!.GetValue<string>().Should().Be("[Array]");
        stringProps["object"]!.GetValue<string>().Should().Be("{Object}");

        numericProps.Count.Should().Be(1);
        numericProps["age"]!.GetValue<decimal>().Should().Be(10);
    }

    [Fact]
    public void SplitProps_should_truncate_long_values()
    {
        var body = new EventBody
        {
            Timestamp = DateTime.UtcNow.AddDays(5),
            Props = JsonDocument.Parse(@"{
                ""type"": ""ERROR"",
                ""value"": ""12345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890""
            }")
        };

        var (stringProps, numericProps) = body.SplitProps();
        stringProps.Count.Should().Be(2);
        stringProps["type"]!.GetValue<string>().Should().Be("ERROR");
        stringProps["value"]!.GetValue<string>().Should().Be("123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567...");
    }

    private static EventBody CreateValidBody(string? userId) => new()
    {
        EventName = "Button Clicked",
        Timestamp = DateTime.UtcNow.AddMinutes(-1),
        SessionId = "8ee47a56-a457-4513-a65f-2b8c3065eb95",
        SystemProps = new SystemProperties { SdkVersion = "aptabase-godot@0.0.0", UserId = userId },
    };

    [Fact]
    public void IsValid_should_normalize_persistent_user_id()
    {
        var body = CreateValidBody("9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08");

        var (valid, _) = body.IsValid(NullLogger.Instance);

        valid.Should().BeTrue();
        body.SystemProps.UserId.Should().Be("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08");
    }

    [Fact]
    public void IsValid_should_accept_missing_persistent_user_id()
    {
        var body = CreateValidBody("");

        var (valid, _) = body.IsValid(NullLogger.Instance);

        valid.Should().BeTrue();
        body.SystemProps.UserId.Should().BeNull();
    }

    [Fact]
    public void IsValid_should_reject_invalid_persistent_user_id()
    {
        var body = CreateValidBody("76561198000000000");

        var (valid, msg) = body.IsValid(NullLogger.Instance);

        valid.Should().BeFalse();
        msg.Should().Be("UserID is invalid");
    }

    private const string ValidUserId = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public void IsValid_should_accept_delayed_events_with_persistent_user_id()
    {
        var body = CreateValidBody(ValidUserId);
        body.Timestamp = DateTime.UtcNow.AddDays(-3);

        var (valid, _) = body.IsValid(NullLogger.Instance);

        valid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_should_reject_delayed_events_without_persistent_user_id()
    {
        var body = CreateValidBody(null);
        body.Timestamp = DateTime.UtcNow.AddDays(-3);

        var (valid, msg) = body.IsValid(NullLogger.Instance);

        valid.Should().BeFalse();
        msg.Should().Be("Event is too old.");
    }

    [Fact]
    public void IsValid_should_reject_events_older_than_7_days_with_persistent_user_id()
    {
        var body = CreateValidBody(ValidUserId);
        body.Timestamp = DateTime.UtcNow.AddDays(-8);

        var (valid, msg) = body.IsValid(NullLogger.Instance);

        valid.Should().BeFalse();
        msg.Should().Be("Event is too old.");
    }

    [Fact]
    public void IsValid_should_reject_invalid_persistent_user_id_on_delayed_events()
    {
        var body = CreateValidBody("76561198000000000");
        body.Timestamp = DateTime.UtcNow.AddDays(-3);

        var (valid, msg) = body.IsValid(NullLogger.Instance);

        valid.Should().BeFalse();
        msg.Should().Be("UserID is invalid");
    }

    [Fact]
    public void IsValid_should_keep_timezone_offset_in_range()
    {
        var body = CreateValidBody(null);
        body.SystemProps.TimezoneOffset = -300;

        var (valid, _) = body.IsValid(NullLogger.Instance);

        valid.Should().BeTrue();
        body.SystemProps.TimezoneOffset.Should().Be(-300);
    }

    [Fact]
    public void IsValid_should_drop_out_of_range_timezone_offset_but_keep_event()
    {
        var body = CreateValidBody(null);
        body.SystemProps.TimezoneOffset = 900;

        var (valid, _) = body.IsValid(NullLogger.Instance);

        valid.Should().BeTrue();
        body.SystemProps.TimezoneOffset.Should().BeNull();
    }
}
