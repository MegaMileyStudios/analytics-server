using System.Net;
using ClickHouse.Client.ADO;
using Dapper;
using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Apps;
using Aptabase.Features.Stats;

namespace Aptabase.IntegrationTests;

[Collection("Integration Tests")]
public class RetentionTests
{
    private readonly IntegrationTestsFixture _fixture;

    public RetentionTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    private static string Player(char c) => new(c, 64);

    // Ingestion rejects events older than a day, so the history is written to ClickHouse directly
    private async Task InsertEvents(string appId, string? persistentUserId, params DateTime[] timestamps)
    {
        var conn = _fixture.Services.GetRequiredService<ClickHouseConnection>();
        var userId = persistentUserId is null ? "NULL" : $"'{persistentUserId}'";
        var ttl = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd HH:mm:ss");
        var values = timestamps.Select(t =>
            $"('{appId}', '{t:yyyy-MM-dd HH:mm:ss}', 'app_started', '{Guid.NewGuid():N}', '{Guid.NewGuid():N}', {userId}, '{{}}', '{{}}', '{ttl}')");

        await conn.ExecuteAsync(
            "INSERT INTO events (app_id, timestamp, event_name, user_id, session_id, persistent_user_id, string_props, numeric_props, ttl) VALUES " +
            string.Join(", ", values));
    }

    private async Task<RetentionCohorts> GetRetention(Application app, DateTime startDate, DateTime endDate, string granularity, int periods, string buildMode = "release")
    {
        var response = await _fixture.UserA.GetRetention(app.Id, startDate, endDate, granularity, periods, buildMode);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<RetentionCohorts>())!;
    }

    [Fact]
    public async Task Daily_Cohorts_Count_Players_Active_On_Day_N()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var day = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);

        // Two events on the first day must count once
        await InsertEvents(app.Id, Player('a'), day, day.AddHours(5), day.AddDays(1), day.AddDays(3));
        await InsertEvents(app.Id, Player('b'), day, day.AddDays(3));
        await InsertEvents(app.Id, Player('c'), day.AddDays(1), day.AddDays(2));
        // First seen before the range, so not part of any cohort in it
        await InsertEvents(app.Id, Player('d'), day.AddDays(-16), day, day.AddDays(1));
        // Anonymous players can't be followed over time and are ignored
        await InsertEvents(app.Id, null, day, day.AddDays(1), day.AddDays(3));

        var result = await GetRetention(app, day.Date, day.Date.AddDays(2), "day", 3);

        result.Granularity.Should().Be("Day");
        result.Periods.Should().Be(3);
        result.Cohorts.Should().HaveCount(2);
        result.Cohorts[0].CohortDate.Should().Be(day.Date);
        result.Cohorts[0].Size.Should().Be(2);
        result.Cohorts[0].Retained.Should().Equal(2, 1, 0, 2);
        result.Cohorts[1].CohortDate.Should().Be(day.Date.AddDays(1));
        result.Cohorts[1].Size.Should().Be(1);
        result.Cohorts[1].Retained.Should().Equal(1, 1, 0, 0);
    }

    [Fact]
    public async Task Weekly_Cohorts_Start_On_Monday()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var monday = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);
        var sunday = monday.AddDays(6);

        // First seen on Sunday belongs to that week, the next day is already week 1
        await InsertEvents(app.Id, Player('a'), sunday, sunday.AddDays(1));
        // Monday and Sunday are the same week
        await InsertEvents(app.Id, Player('b'), monday, sunday, monday.AddDays(14));

        var result = await GetRetention(app, monday.Date, sunday.Date, "week", 2);

        result.Cohorts.Should().ContainSingle();
        result.Cohorts[0].CohortDate.Should().Be(monday.Date);
        result.Cohorts[0].Retained.Should().Equal(2, 1, 1);
    }

    [Fact]
    public async Task Monthly_Cohorts_Use_Calendar_Months()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        await InsertEvents(app.Id, Player('a'),
            new DateTime(2026, 1, 31, 23, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 2, 1, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 3, 15, 1, 0, 0, DateTimeKind.Utc));
        await InsertEvents(app.Id, Player('b'), new DateTime(2026, 2, 10, 1, 0, 0, DateTimeKind.Utc));

        var result = await GetRetention(app, new DateTime(2026, 1, 1), new DateTime(2026, 2, 28), "month", 2);

        result.Cohorts.Should().HaveCount(2);
        result.Cohorts[0].CohortDate.Should().Be(new DateTime(2026, 1, 1));
        result.Cohorts[0].Retained.Should().Equal(1, 1, 1);
        result.Cohorts[1].CohortDate.Should().Be(new DateTime(2026, 2, 1));
        result.Cohorts[1].Retained.Should().Equal(1, 0, 0);
    }

    [Fact]
    public async Task Periods_That_Havent_Started_Are_Null()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var today = DateTime.UtcNow.Date;

        await InsertEvents(app.Id, Player('a'), today.AddDays(-2), today.AddDays(-1));

        var result = await GetRetention(app, today.AddDays(-2), today, "day", 5);

        result.Cohorts.Should().ContainSingle();
        result.Cohorts[0].Retained.Should().Equal(1, 1, 0, null, null, null);
    }

    [Fact]
    public async Task Debug_And_Release_Are_Separate()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var day = new DateTime(2026, 1, 5, 10, 0, 0, DateTimeKind.Utc);

        await InsertEvents($"{app.Id}_DEBUG", Player('a'), day, day.AddDays(1));

        var release = await GetRetention(app, day.Date, day.Date, "day", 1);
        release.Cohorts.Should().BeEmpty();

        var debug = await GetRetention(app, day.Date, day.Date, "day", 1, "debug");
        debug.Cohorts.Should().ContainSingle().Which.Retained.Should().Equal(1, 1);
    }

    [Theory]
    [InlineData(true, false, "day", 7)]
    [InlineData(false, true, "day", 7)]
    [InlineData(true, true, "day", 0)]
    [InlineData(true, true, "day", 91)]
    [InlineData(true, true, "week", 53)]
    [InlineData(true, true, "month", 25)]
    public async Task Invalid_Parameters_Return_BadRequest(bool withStart, bool withEnd, string granularity, int periods)
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var today = DateTime.UtcNow.Date;

        var response = await _fixture.UserA.GetRetention(app.Id, withStart ? today.AddDays(-7) : null, withEnd ? today : null, granularity, periods);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Start_After_End_Returns_BadRequest()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var today = DateTime.UtcNow.Date;

        var response = await _fixture.UserA.GetRetention(app.Id, today, today.AddDays(-7));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cant_Read_Retention_Of_Someone_Elses_App()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var today = DateTime.UtcNow.Date;

        var response = await _fixture.UserB.GetRetention(app.Id, today.AddDays(-7), today);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
