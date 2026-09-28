using System.Net;
using ClickHouse.Client.ADO;
using Dapper;
using Xunit;
using AwesomeAssertions;
using Aptabase.Features.Apps;
using Aptabase.Features.Stats;

namespace Aptabase.IntegrationTests;

[Collection("Integration Tests")]
public class EventPropsChartTests
{
    private readonly IntegrationTestsFixture _fixture;
    private static readonly DateTime Day = new(2026, 3, 10, 0, 0, 0, DateTimeKind.Utc);

    public EventPropsChartTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    // Ingestion rejects old events, so they're written to ClickHouse directly
    private async Task InsertEvent(string appId, DateTime timestamp, string eventName, string stringProps, string numericProps)
    {
        var conn = _fixture.Services.GetRequiredService<ClickHouseConnection>();
        var ttl = DateTime.UtcNow.AddYears(1).ToString("yyyy-MM-dd HH:mm:ss");
        await conn.ExecuteAsync(
            "INSERT INTO events (app_id, timestamp, event_name, user_id, session_id, string_props, numeric_props, ttl) VALUES " +
            $"('{appId}', '{timestamp:yyyy-MM-dd HH:mm:ss}', '{eventName}', '{Guid.NewGuid():N}', '{Guid.NewGuid():N}', '{stringProps}', '{numericProps}', '{ttl}')");
    }

    private async Task<T> Get<T>(Application app, string endpoint, string eventName, string? propKey, string buildMode = "release")
    {
        var response = await _fixture.UserA.GetEventPropStats(endpoint, app.Id, eventName, propKey, Day, Day.AddDays(2), buildMode: buildMode);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    [Fact]
    public async Task Prop_Keys_Are_Typed_As_Number_Boolean_Or_String()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(1), "level_completed", """{"cleared":"true","level":"forest","mode":"true"}""", """{"score":10}""");
        await InsertEvent(app.Id, Day.AddHours(2), "level_completed", """{"cleared":"false","level":"cave","mode":"hard"}""", """{"score":20,"time":3.5}""");
        await InsertEvent(app.Id, Day.AddHours(3), "other_event", """{"unrelated":"x"}""", "{}");

        var keys = await Get<EventPropKey[]>(app, "event-prop-keys", "level_completed", null);

        keys.Select(x => (x.Key, x.Type)).Should().Equal(
            ("cleared", "boolean"),
            ("level", "string"),
            ("mode", "string"),
            ("score", "number"),
            ("time", "number"));
    }

    [Fact]
    public async Task Numeric_Props_Are_Aggregated_Per_Period()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(1), "level_completed", "{}", """{"score":10}""");
        await InsertEvent(app.Id, Day.AddHours(2), "level_completed", "{}", """{"score":30}""");
        await InsertEvent(app.Id, Day.AddHours(3), "level_completed", "{}", """{"score":20}""");
        // Events without the prop are skipped instead of counted as zero
        await InsertEvent(app.Id, Day.AddHours(4), "level_completed", "{}", """{"time":1}""");
        await InsertEvent(app.Id, Day.AddHours(5), "other_event", "{}", """{"score":1000}""");

        var rows = await Get<EventPropNumericRow[]>(app, "event-prop-numeric", "level_completed", "score");

        rows.Should().HaveCount(2);
        rows[0].Period.Should().Be(Day);
        rows[0].Events.Should().Be(3);
        rows[0].Min.Should().Be(10);
        rows[0].Max.Should().Be(30);
        rows[0].Avg.Should().Be(20);
        rows[0].Median.Should().Be(20);
        rows[0].Sum.Should().Be(60);
        // The day without values is filled in with zero events (the end of the range is exclusive)
        rows[1].Period.Should().Be(Day.AddDays(1));
        rows[1].Events.Should().Be(0);
    }

    [Fact]
    public async Task Boolean_Distribution_Includes_Undefined()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(1), "level_completed", """{"cleared":"true"}""", "{}");
        await InsertEvent(app.Id, Day.AddHours(2), "level_completed", """{"cleared":"true"}""", "{}");
        await InsertEvent(app.Id, Day.AddHours(3), "level_completed", """{"cleared":"false"}""", "{}");
        await InsertEvent(app.Id, Day.AddHours(4), "level_completed", "{}", "{}");

        var result = await Get<EventPropDistribution>(app, "event-prop-distribution", "level_completed", "cleared");

        result.Type.Should().Be("boolean");
        result.Items.Select(x => (x.Name, x.Value)).Should().Equal(("True", 2), ("False", 1), ("Undefined", 1));
    }

    [Fact]
    public async Task String_Distribution_Groups_Overflow_As_Other()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        for (var i = 1; i <= 11; i++)
            await InsertEvent(app.Id, Day.AddHours(1), "level_completed", $$"""{"level":"level-{{i:D2}}"}""", "{}");
        await InsertEvent(app.Id, Day.AddHours(2), "level_completed", """{"level":"level-01"}""", "{}");

        var result = await Get<EventPropDistribution>(app, "event-prop-distribution", "level_completed", "level");

        result.Type.Should().Be("string");
        result.Items.Should().HaveCount(10);
        result.Items[0].Should().Be(new EventPropDistributionItem { Name = "level-01", Value = 2 });
        result.Items[9].Should().Be(new EventPropDistributionItem { Name = "Other", Value = 2, IsOther = true });
    }

    [Fact]
    public async Task Distribution_Is_Split_Per_Period()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(1), "level_completed", """{"cleared":"true"}""", "{}");
        await InsertEvent(app.Id, Day.AddHours(2), "level_completed", """{"cleared":"false"}""", "{}");
        await InsertEvent(app.Id, Day.AddDays(1).AddHours(1), "level_completed", """{"cleared":"true"}""", "{}");
        await InsertEvent(app.Id, Day.AddDays(1).AddHours(2), "level_completed", "{}", "{}");

        var result = await Get<EventPropDistributionSeries>(app, "event-prop-distribution-periodic", "level_completed", "cleared");

        result.Type.Should().Be("boolean");
        result.Periods.Should().Equal(Day, Day.AddDays(1));
        result.Series.Select(x => (x.Name, string.Join(",", x.Values))).Should().Equal(
            ("True", "1,1"),
            ("False", "1,0"),
            ("Undefined", "0,1"));
    }

    [Fact]
    public async Task Debug_Events_Are_Kept_Separate()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(1), "level_completed", "{}", """{"score":10}""");
        await InsertEvent($"{app.Id}_DEBUG", Day.AddHours(1), "level_completed", "{}", """{"score":99}""");

        var release = await Get<EventPropNumericRow[]>(app, "event-prop-numeric", "level_completed", "score");
        var debug = await Get<EventPropNumericRow[]>(app, "event-prop-numeric", "level_completed", "score", "debug");

        release.Sum(x => x.Sum).Should().Be(10);
        debug.Sum(x => x.Sum).Should().Be(99);
    }

    [Fact]
    public async Task Prop_Key_With_Quotes_Is_Escaped()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        await InsertEvent(app.Id, Day.AddHours(1), "level_completed", "{}", """{"score":10}""");

        var rows = await Get<EventPropNumericRow[]>(app, "event-prop-numeric", "level_completed", "sc'ore");

        rows.Sum(x => x.Events).Should().Be(0);
    }

    [Theory]
    [InlineData("event-prop-numeric")]
    [InlineData("event-prop-distribution")]
    [InlineData("event-prop-distribution-periodic")]
    public async Task Prop_Key_Is_Required(string endpoint)
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var response = await _fixture.UserA.GetEventPropStats(endpoint, app.Id, "level_completed", null, Day, Day.AddDays(1));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
