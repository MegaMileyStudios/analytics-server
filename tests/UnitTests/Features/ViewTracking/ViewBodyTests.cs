using Xunit;
using Aptabase.Features.Ingestion;
using Aptabase.Features.ViewTracking;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aptabase.UnitTests.Features.ViewTracking;

public class ViewBodyTests
{
    private static ViewBody NewBody(string viewType = "MainScreenChange", string viewName = "Main Menu") => new()
    {
        ViewType = viewType,
        ViewName = viewName,
        Timestamp = DateTime.UtcNow.AddMinutes(-1),
        SessionId = Guid.NewGuid().ToString(),
        SystemProps = new SystemProperties { SdkVersion = "aptabase-unity@1.0.0", Locale = "en-US" },
    };

    [Theory]
    [InlineData("MainScreenChange", "MainScreenChange")]
    [InlineData("popupopen", "PopupOpen")]
    [InlineData("POPUPCLOSE", "PopupClose")]
    public void Known_view_types_are_canonicalized(string input, string expected)
    {
        var body = NewBody(viewType: input);

        var (valid, _) = body.IsValid(NullLogger.Instance);

        valid.Should().BeTrue();
        body.ViewType.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("ScreenChange")]
    [InlineData("PopupToggle")]
    public void Unknown_view_types_are_rejected(string viewType)
    {
        var (valid, message) = NewBody(viewType: viewType).IsValid(NullLogger.Instance);

        valid.Should().BeFalse();
        message.Should().Contain("ViewType");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_view_names_are_rejected(string viewName)
    {
        var (valid, message) = NewBody(viewName: viewName).IsValid(NullLogger.Instance);

        valid.Should().BeFalse();
        message.Should().Contain("ViewName");
    }

    [Fact]
    public void View_names_longer_than_the_limit_are_rejected()
    {
        NewBody(viewName: new string('a', ViewBody.MaxViewNameLength)).IsValid(NullLogger.Instance).Item1.Should().BeTrue();
        NewBody(viewName: new string('a', ViewBody.MaxViewNameLength + 1)).IsValid(NullLogger.Instance).Item1.Should().BeFalse();
    }

    [Fact]
    public void Old_views_without_a_persistent_user_id_are_rejected()
    {
        var body = NewBody();
        body.Timestamp = DateTime.UtcNow.AddDays(-2);

        var (valid, message) = body.IsValid(NullLogger.Instance);

        valid.Should().BeFalse();
        message.Should().Be("Event is too old.");
    }

    [Fact]
    public void Old_views_with_a_persistent_user_id_are_accepted()
    {
        var body = NewBody();
        body.Timestamp = DateTime.UtcNow.AddDays(-2);
        body.SystemProps.UserId = new string('A', 64);

        var (valid, _) = body.IsValid(NullLogger.Instance);

        valid.Should().BeTrue();
        body.SystemProps.UserId.Should().Be(new string('a', 64));
    }

    [Fact]
    public void Invalid_persistent_user_ids_are_rejected()
    {
        var body = NewBody();
        body.SystemProps.UserId = "not-a-valid-id";

        body.IsValid(NullLogger.Instance).Item1.Should().BeFalse();
    }

    [Fact]
    public void Session_ids_longer_than_36_characters_are_rejected()
    {
        var body = NewBody();
        body.SessionId = new string('x', 37);

        body.IsValid(NullLogger.Instance).Item1.Should().BeFalse();
    }
}
