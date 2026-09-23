using System.Net;
using Xunit;
using AwesomeAssertions;
using Aptabase.IntegrationTests.Clients;
using Aptabase.Features.Apps;
using Aptabase.Features.Ingestion.Buffer;
using Aptabase.Features.Stats;

namespace Aptabase.IntegrationTests;

[Collection("Integration Tests")]
public class StatsTests
{
    private const string PersistentUserA = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string PersistentUserB = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string PersistentUserD = "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd";

    private readonly IntegrationTestsFixture _fixture;
    private readonly EventBackgroundWritter _eventWritter;

    public StatsTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
        _eventWritter = _fixture.GetHostedService<EventBackgroundWritter>();
    }

    // Each IngestionClient has its own IP address, so its own ephemeral user_id
    // - User A: persistent ID, two sessions (3 events)
    // - User B: persistent ID, one session (1 event)
    // - User C: no persistent ID, one session (2 events)
    // - User D: one session without and one with a persistent ID (1 + 1 events), must not be counted twice
    private async Task<(Application app, string sessionOfUserA)> CreateAppWithUsers()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var now = DateTime.UtcNow;

        var userA = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        userA.SetUserId(PersistentUserA);
        var sessionOfUserA = IngestionClient.NewSessionId();
        userA.SetSessionId(sessionOfUserA);
        await userA.TrackEvent(now, "App Started", null);
        await userA.TrackEvent(now, "Button Clicked", null);
        userA.SetSessionId(IngestionClient.NewSessionId());
        await userA.TrackEvent(now, "App Started", null);

        var userB = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        userB.SetUserId(PersistentUserB);
        await userB.TrackEvent(now, "App Started", null);

        var userC = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        await userC.TrackEvent(now, "App Started", null);
        await userC.TrackEvent(now, "Button Clicked", null);

        var userD = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        await userD.TrackEvent(now, "App Started", null);
        userD.SetSessionId(IngestionClient.NewSessionId());
        userD.SetUserId(PersistentUserD);
        await userD.TrackEvent(now, "App Started", null);

        await _eventWritter.FlushEvents();
        return (app, sessionOfUserA);
    }

    [Fact]
    public async Task Key_Metrics_Count_Persistent_And_Inferred_Users()
    {
        var (app, _) = await CreateAppWithUsers();

        var metrics = await _fixture.UserA.GetKeyMetrics(app.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1));

        metrics.Should().NotBeNull();
        metrics!.Current.DailyPersistentUsers.Should().Be(3);
        metrics.Current.DailyInferredUsers.Should().Be(1);
        metrics.Current.DailyUsers.Should().Be(4);
        metrics.Current.Sessions.Should().Be(6);
        metrics.Current.Events.Should().Be(8);
    }

    [Fact]
    public async Task Periodic_Stats_Split_Users_Sessions_And_Events()
    {
        var (app, _) = await CreateAppWithUsers();

        var rows = await _fixture.UserA.GetPeriodicStats(app.Id, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1), "Day");

        rows.Should().NotBeNull();
        rows!.Sum(x => x.PersistentUsers).Should().Be(3);
        rows.Sum(x => x.InferredUsers).Should().Be(1);
        rows.Sum(x => x.Users).Should().Be(4);
        rows.Sum(x => x.PersistentSessions).Should().Be(5);
        rows.Sum(x => x.InferredSessions).Should().Be(1);
        rows.Sum(x => x.Sessions).Should().Be(6);
        rows.Sum(x => x.PersistentEvents).Should().Be(6);
        rows.Sum(x => x.InferredEvents).Should().Be(2);
        rows.Sum(x => x.Events).Should().Be(8);
    }

    [Fact]
    public async Task Live_Geo_Splits_Persistent_And_Inferred_Users()
    {
        var (app, _) = await CreateAppWithUsers();

        var points = await _fixture.UserA.GetLiveGeo(app.Id);

        points.Should().NotBeNull();
        points!.Sum(x => (long)x.PersistentUsers).Should().Be(3);
        points.Sum(x => (long)x.InferredUsers).Should().Be(1);
        points.Sum(x => (long)x.Users).Should().Be(4);
    }

    [Fact]
    public async Task Can_Filter_Sessions_By_Persistent_UserId()
    {
        var (app, _) = await CreateAppWithUsers();

        // Mixed case is accepted, IDs are stored lowercase
        var response = await _fixture.UserA.GetHistoricalSessions(app.Id, PersistentUserD.ToUpperInvariant());
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var sessions = await response.Content.ReadFromJsonAsync<LiveRecentSession[]>();
        sessions.Should().HaveCount(1);
        sessions![0].PersistentUserId.Should().Be(PersistentUserD);

        response = await _fixture.UserA.GetHistoricalSessions(app.Id, PersistentUserA);
        sessions = await response.Content.ReadFromJsonAsync<LiveRecentSession[]>();
        sessions.Should().HaveCount(2);
        sessions.Should().OnlyContain(x => x.PersistentUserId == PersistentUserA);

        response = await _fixture.UserA.GetHistoricalSessions(app.Id);
        sessions = await response.Content.ReadFromJsonAsync<LiveRecentSession[]>();
        sessions.Should().HaveCount(6);
        sessions.Count(x => x.PersistentUserId == null).Should().Be(2);
    }

    [Fact]
    public async Task Cant_Filter_Sessions_By_Invalid_Persistent_UserId()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var response = await _fixture.UserA.GetHistoricalSessions(app.Id, "76561198000000000");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Session_Details_Include_Persistent_UserId()
    {
        var (app, sessionOfUserA) = await CreateAppWithUsers();

        var timeline = await _fixture.UserA.GetSessionTimeline(app.Id, sessionOfUserA);

        timeline.Should().NotBeNull();
        timeline!.PersistentUserId.Should().Be(PersistentUserA);
    }
}
