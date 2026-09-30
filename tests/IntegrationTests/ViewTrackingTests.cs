using System.Net;
using ClickHouse.Client.ADO;
using Dapper;
using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Apps;
using Aptabase.Features.Ingestion.Buffer;
using Aptabase.Features.ViewTracking;
using Aptabase.Features.ViewTracking.Buffer;
using Aptabase.IntegrationTests.Clients;

namespace Aptabase.IntegrationTests;

[Collection("Integration Tests")]
public class ViewTrackingTests
{
    private readonly IntegrationTestsFixture _fixture;
    private readonly EventBackgroundWritter _eventWritter;
    private readonly ViewBackgroundWritter _viewWritter;

    public ViewTrackingTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
        _eventWritter = _fixture.GetHostedService<EventBackgroundWritter>();
        _viewWritter = _fixture.GetHostedService<ViewBackgroundWritter>();
    }

    // Ingestion rejects anything older than a day, so the session happened a few minutes ago.
    // Whole seconds, because regular events are stored with second precision.
    private static DateTime SessionStart()
    {
        var start = DateTime.UtcNow.AddMinutes(-10);
        return new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second, DateTimeKind.Utc);
    }

    // Main Menu (Settings open/close) -> Game View (Pause -> Settings on top) -> Main Menu
    // closes both popups implicitly -> Feedback is never closed and ends with the session.
    private async Task SendScriptedSession(ViewsClient client, DateTime t0)
    {
        var views = await client.PostViews(new[]
        {
            (t0.AddSeconds(0.2), "MainScreenChange", "Main Menu"),
            (t0.AddSeconds(5.5), "PopupOpen", "Settings"),
            (t0.AddSeconds(9.25), "PopupClose", "Settings"),
            (t0.AddSeconds(12.1), "MainScreenChange", "Game View"),
            (t0.AddSeconds(100), "PopupOpen", "Pause"),
            (t0.AddSeconds(110), "PopupOpen", "Settings"),
            (t0.AddSeconds(130), "MainScreenChange", "Main Menu"),
            (t0.AddSeconds(150), "PopupOpen", "Feedback"),
        });
        views.StatusCode.Should().Be(HttpStatusCode.OK);

        var events = await client.PostEvents(new[]
        {
            (t0, "app_started"),
            (t0.AddSeconds(40), "level_started"),
            (t0.AddSeconds(160), "app_shutdown"),
        });
        events.StatusCode.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();
        await _viewWritter.FlushViews();
    }

    private async Task<ViewStatsRow[]> GetStats(Application app, string kind, DateTime t0, string buildMode = "release", string? mainView = null, string? osName = null, string? appVersion = null)
    {
        var response = await _fixture.UserA.GetViewStats(kind, app.Id, t0.AddHours(-1), t0.AddHours(1), buildMode, mainView, osName, appVersion);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<ViewStatsRow[]>())!;
    }

    [Fact]
    public async Task Main_Views_Last_Until_The_Next_Main_View_Or_Session_End()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var t0 = SessionStart();
        await SendScriptedSession(new ViewsClient(_fixture.CreateClient(), app.AppKey), t0);

        var rows = await GetStats(app, "main", t0);

        rows.Should().HaveCount(2);
        var gameView = rows.Single(r => r.Name == "Game View");
        gameView.Visits.Should().Be(1);
        gameView.TotalSeconds.Should().BeApproximately(117.9, 0.001);

        // 11.9s for the first visit, 30s until the app_shutdown event for the second one
        var mainMenu = rows.Single(r => r.Name == "Main Menu");
        mainMenu.Visits.Should().Be(2);
        mainMenu.Sessions.Should().Be(1);
        mainMenu.TotalSeconds.Should().BeApproximately(41.9, 0.001);
        mainMenu.AvgSeconds.Should().BeApproximately(20.95, 0.001);
        mainMenu.MedianSeconds.Should().BeApproximately(20.95, 0.001);
    }

    [Fact]
    public async Task Popups_End_On_Close_Main_View_Change_Or_Session_End()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var t0 = SessionStart();
        await SendScriptedSession(new ViewsClient(_fixture.CreateClient(), app.AppKey), t0);

        var rows = await GetStats(app, "popups", t0);

        rows.Should().HaveCount(3);
        // Explicitly closed after 3.75s, and implicitly closed after 20s by the main view change
        var settings = rows.Single(r => r.Name == "Settings");
        settings.Visits.Should().Be(2);
        settings.TotalSeconds.Should().BeApproximately(23.75, 0.001);
        rows.Single(r => r.Name == "Pause").TotalSeconds.Should().BeApproximately(30, 0.001);
        // Never closed, so it ends with the session
        rows.Single(r => r.Name == "Feedback").TotalSeconds.Should().BeApproximately(10, 0.001);
    }

    [Fact]
    public async Task Popups_Can_Be_Filtered_By_Their_Main_View()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var t0 = SessionStart();
        await SendScriptedSession(new ViewsClient(_fixture.CreateClient(), app.AppKey), t0);

        // Settings opened on top of Pause still belongs to the Game View underneath
        var gameView = await GetStats(app, "popups", t0, mainView: "Game View");
        gameView.Select(r => r.Name).Should().BeEquivalentTo("Pause", "Settings");
        gameView.Single(r => r.Name == "Settings").TotalSeconds.Should().BeApproximately(20, 0.001);

        var mainMenu = await GetStats(app, "popups", t0, mainView: "Main Menu");
        mainMenu.Select(r => r.Name).Should().BeEquivalentTo("Settings", "Feedback");
        mainMenu.Single(r => r.Name == "Settings").TotalSeconds.Should().BeApproximately(3.75, 0.001);
    }

    [Fact]
    public async Task Stats_Can_Be_Filtered_By_OS_And_App_Version()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var t0 = SessionStart();
        await SendScriptedSession(new ViewsClient(_fixture.CreateClient(), app.AppKey) { OsName = "Windows", AppVersion = "1.0.0" }, t0);
        await SendScriptedSession(new ViewsClient(_fixture.CreateClient(), app.AppKey) { OsName = "Android", AppVersion = "2.0.0" }, t0);

        (await GetStats(app, "main", t0)).Single(r => r.Name == "Game View").Sessions.Should().Be(2);
        (await GetStats(app, "main", t0, osName: "Android")).Single(r => r.Name == "Game View").Sessions.Should().Be(1);
        (await GetStats(app, "popups", t0, appVersion: "2.0.0")).Single(r => r.Name == "Pause").Sessions.Should().Be(1);
        (await GetStats(app, "main", t0, osName: "iOS")).Should().BeEmpty();
    }

    [Fact]
    public async Task Debug_And_Release_Views_Are_Kept_Apart()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var t0 = SessionStart();
        await SendScriptedSession(new ViewsClient(_fixture.CreateClient(), app.AppKey) { IsDebug = true }, t0);

        (await GetStats(app, "main", t0)).Should().BeEmpty();
        (await GetStats(app, "main", t0, buildMode: "debug")).Should().HaveCount(2);
    }

    [Fact]
    public async Task Session_Timeline_Includes_The_View_Events()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var t0 = SessionStart();
        var client = new ViewsClient(_fixture.CreateClient(), app.AppKey);
        await SendScriptedSession(client, t0);

        var timeline = await _fixture.UserA.GetSessionTimeline(app.Id, client.SessionId);

        timeline!.EventsName.Should().BeEquivalentTo("app_started", "level_started", "app_shutdown");
        timeline.ViewsType.Should().HaveCount(8);
        timeline.ViewsName.Should().StartWith(new[] { "Main Menu", "Settings", "Settings", "Game View" });
        timeline.ViewsType.Should().StartWith(new[] { "MainScreenChange", "PopupOpen", "PopupClose" });
        timeline.ViewsTimestamp[1].Should().Be(t0.AddSeconds(5.5));
    }

    [Fact]
    public async Task Single_View_Endpoint_Accepts_A_View()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var t0 = SessionStart();
        var client = new ViewsClient(_fixture.CreateClient(), app.AppKey);

        var response = await client.PostView(t0, "mainscreenchange", "Main Menu");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _viewWritter.FlushViews();

        (await GetStats(app, "main", t0)).Single().Name.Should().Be("Main Menu");
    }

    [Theory]
    [InlineData("Unknown", "Main Menu")]
    [InlineData("PopupOpen", "")]
    public async Task Invalid_Views_Are_Rejected(string viewType, string viewName)
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var client = new ViewsClient(_fixture.CreateClient(), app.AppKey);

        var response = await client.PostView(DateTime.UtcNow, viewType, viewName);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Views_With_An_Unknown_App_Key_Are_Rejected()
    {
        var client = new ViewsClient(_fixture.CreateClient(), "A-DEV-0000000000");

        var response = await client.PostView(DateTime.UtcNow, "MainScreenChange", "Main Menu");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Too_Many_Views_In_One_Request_Are_Rejected()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var client = new ViewsClient(_fixture.CreateClient(), app.AppKey);

        var views = Enumerable.Range(0, ViewsController.MaxViewsPerRequest + 1)
            .Select(i => (DateTime.UtcNow, "PopupOpen", $"Popup {i}"));
        var response = await client.PostViews(views);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Other_Users_Cannot_Read_View_Stats()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var response = await _fixture.UserB.GetViewStats("main", app.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Views_Store_The_Timezone_Offset()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var client = new ViewsClient(_fixture.CreateClient(), app.AppKey) { TimezoneOffset = 540 };

        var response = await client.PostView(DateTime.UtcNow, "MainScreenChange", "Main Menu");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await _viewWritter.FlushViews();

        var conn = _fixture.Services.GetRequiredService<ClickHouseConnection>();
        var offsets = await conn.QueryAsync<short?>(
            "SELECT timezone_offset FROM view_events WHERE app_id = @app_id",
            new { app_id = app.Id });
        offsets.Should().ContainSingle().Which.Should().Be(540);
    }
}
