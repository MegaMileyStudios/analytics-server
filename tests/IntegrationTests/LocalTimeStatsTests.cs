using System.Net;
using ClickHouse.Client.ADO;
using Dapper;
using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Apps;
using Aptabase.Features.Stats;

namespace Aptabase.IntegrationTests;

[Collection("Integration Tests")]
public class LocalTimeStatsTests
{
    private readonly IntegrationTestsFixture _fixture;
    // A Tuesday
    private static readonly DateTime Day = new(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

    public LocalTimeStatsTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    // Ingestion rejects old events, so they're written to ClickHouse directly
    private async Task InsertEvent(string appId, DateTime timestamp, short? timezoneOffset, string? sessionId = null)
    {
        var conn = _fixture.Services.GetRequiredService<ClickHouseConnection>();
        var ttl = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd HH:mm:ss");
        var offset = timezoneOffset?.ToString() ?? "NULL";
        await conn.ExecuteAsync(
            "INSERT INTO events (app_id, timestamp, event_name, user_id, session_id, string_props, numeric_props, ttl, timezone_offset) VALUES " +
            $"('{appId}', '{timestamp:yyyy-MM-dd HH:mm:ss}', 'app_started', '{Guid.NewGuid():N}', '{sessionId ?? Guid.NewGuid().ToString("N")}', '{{}}', '{{}}', '{ttl}', {offset})");
    }

    private async Task<T> Get<T>(Application app, string endpoint, DateTime startDate, DateTime endDate, string query = "")
    {
        var response = await _fixture.UserA.GetStats(endpoint, app.Id, startDate, endDate, query);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Local_Play_Time_Uses_The_Players_Local_Hour()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        // The same moment for a player in Tokyo (20:00), Amsterdam (13:00) and one without an offset
        await InsertEvent(app.Id, Day.AddHours(11), 540);
        await InsertEvent(app.Id, Day.AddHours(11), 120);
        await InsertEvent(app.Id, Day.AddHours(11), null);

        var rows = await Get<LocalPlayTimeRow[]>(app, "local-play-time", Day, Day.AddDays(1), "&localTime=true&metric=Events");

        rows.Select(x => (x.DayOfWeek, x.Hour, x.Value)).Should().Equal(
            ((byte)2, (byte)13, 1UL),
            ((byte)2, (byte)20, 1UL));
    }

    [Fact]
    public async Task Local_Play_Time_Uses_The_Viewers_Time_Zone_In_Real_Time()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(11), 540);
        await InsertEvent(app.Id, Day.AddHours(11), 120);
        await InsertEvent(app.Id, Day.AddHours(11), null);

        var rows = await Get<LocalPlayTimeRow[]>(app, "local-play-time", Day, Day.AddDays(1), "&localTime=false&metric=Sessions&timeZone=Asia/Tokyo");

        rows.Select(x => (x.DayOfWeek, x.Hour, x.Value)).Should().Equal(((byte)2, (byte)20, 3UL));
    }

    [Fact]
    public async Task Local_Play_Time_Counts_Sessions_At_Their_First_Event()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var session = Guid.NewGuid().ToString("N");
        await InsertEvent(app.Id, Day.AddHours(11), 60, session);
        await InsertEvent(app.Id, Day.AddHours(12), 60, session);
        await InsertEvent(app.Id, Day.AddHours(13), 60, session);

        var rows = await Get<LocalPlayTimeRow[]>(app, "local-play-time", Day, Day.AddDays(1), "&localTime=true&metric=Sessions");

        rows.Select(x => (x.Hour, x.Value)).Should().Equal(((byte)12, 1UL));
    }

    [Fact]
    public async Task Invalid_Viewer_Time_Zone_Falls_Back_To_Utc()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(11), 540);

        var rows = await Get<LocalPlayTimeRow[]>(app, "local-play-time", Day, Day.AddDays(1), "&metric=Events&timeZone=Not%2FA%27Zone");

        rows.Select(x => x.Hour).Should().Equal((byte)11);
    }

    [Fact]
    public async Task Periodic_Stats_Filter_And_Bucket_By_Local_Date()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        // 11:00 local on the day
        await InsertEvent(app.Id, Day.AddHours(10), 60);
        // 00:30 local on the next day
        await InsertEvent(app.Id, Day.AddHours(22).AddMinutes(30), 120);
        // 23:00 local on the day before
        await InsertEvent(app.Id, Day.AddHours(1), -120);
        // No offset, left out in local time
        await InsertEvent(app.Id, Day.AddHours(12), null);

        var endOfDay = Day.AddDays(1).AddSeconds(-1);
        var real = await Get<PeriodicStatsRow[]>(app, "periodic", Day, endOfDay, "&granularity=Hour");
        real.Sum(x => x.Events).Should().Be(4);

        var local = await Get<PeriodicStatsRow[]>(app, "periodic", Day, endOfDay, "&granularity=Hour&localTime=true");
        local.Sum(x => x.Events).Should().Be(1);
        local.Single(x => x.Events > 0).Period.Should().Be(Day.AddHours(11));
    }

    [Fact]
    public async Task Key_Metrics_Leave_Out_Events_Without_Offset_In_Local_Time()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(10), 60);
        await InsertEvent(app.Id, Day.AddHours(12), null);

        var local = await Get<KeyMetrics>(app, "metrics", Day, Day.AddDays(1).AddSeconds(-1), "&localTime=true");

        local.Current.Events.Should().Be(1);
    }

    [Fact]
    public async Task Counts_Events_Without_Timezone_Offset()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(10), 60);
        await InsertEvent(app.Id, Day.AddHours(11), null);
        await InsertEvent(app.Id, Day.AddHours(12), null);

        var result = await Get<MissingTimezoneOffset>(app, "missing-timezone-offset", Day, Day.AddDays(1));

        result.Events.Should().Be(2);
    }
}
