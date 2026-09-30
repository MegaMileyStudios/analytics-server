using Xunit;
using System.Net;
using AwesomeAssertions;
using Aptabase.IntegrationTests.Clients;
using Aptabase.Features.Ingestion.Buffer;
using ClickHouse.Client.ADO;
using Dapper;

namespace Aptabase.IntegrationTests;

[Collection("Integration Tests")]
public class IngestionTests
{
    private readonly IntegrationTestsFixture _fixture;
    private readonly EventBackgroundWritter _eventWritter;

    public IngestionTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
        _eventWritter = _fixture.GetHostedService<EventBackgroundWritter>();
    }

    [Fact]
    public async Task Can_Ingest_For_Known_App()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        var code = await client.TrackEvent(DateTime.UtcNow.AddHours(-10), "Button Clicked", null);
        code.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();

        var count = await _fixture.UserA.CountEvents(app.Id, "24h");
        count.Should().Be(1);
    }

    [Fact]
    public async Task Can_Ingest_Future_Events()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        var code = await client.TrackEvent(DateTime.UtcNow.AddYears(10), "Button Clicked", null);
        code.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();

        var count = await _fixture.UserA.CountEvents(app.Id, "24h");
        count.Should().Be(1);
    }

    [Fact]
    public async Task Can_Ingest_Multiple_Events()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        var code = await client.TrackEvents([
            (DateTime.UtcNow, "App Started", null),
            (DateTime.UtcNow, "Menu Opened", null),
            (DateTime.UtcNow, "Button Clicked", null),
        ]);
        code.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();

        var count = await _fixture.UserA.CountEvents(app.Id, "24h");
        count.Should().Be(3);
    }

    [Fact]
    public async Task Should_drop_events_if_invalid()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        var code = await client.TrackEvents([
            (DateTime.UtcNow, "App Started", null),
            (DateTime.UtcNow, "Menu Opened", null),
            (DateTime.UtcNow, "Button Clicked", "INVALID PROPS"),
        ]);
        code.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();

        var count = await _fixture.UserA.CountEvents(app.Id, "24h");
        count.Should().Be(2);
    }

    [Fact]
    public async Task Cant_Ingest_More_Than_25_Events_In_Batch()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        var code = await client.TrackEvents(Enumerable.Range(1, 26).Select(i => (DateTime.UtcNow, "Button Clicked", (object?)null)));
        code.Should().Be(HttpStatusCode.BadRequest);

        await _eventWritter.FlushEvents();

        var count = await _fixture.UserA.CountEvents(app.Id, "24h");
        count.Should().Be(0);
    }

    [Fact]
    public async Task Cant_Ingest_Unknown_AppKey()
    {
        var client = new IngestionClient(_fixture.CreateClient(), "THIS-DOES-NOT-EXIST");
        var code = await client.TrackEvent(DateTime.UtcNow, "Button Clicked", null);
        code.Should().Be(HttpStatusCode.NotFound);
    }
 
    public static IEnumerable<object[]> ValidProps => 
    [
        ["{}"],
        ["{\"name\":\"Bob\"}"],
        ["{\"name\":\"Bob\", \"age\":20}"],
        [new { }],
        [new { age = 20 }],
        [new { name = "Bob", age = 20 }],
        [new { name = "Bob", valid = true }],
        [new { name = "Bob", surname = (object?)null }],
        [new { name = "Bob", age = 20, list = new int[]{1,2,3} }],
        ["{\"name\":\"Bob\", \"age\":20, \"list\": [1,2,3]}"],
    ];

    [Theory, MemberData(nameof(ValidProps))]
    public async Task Cant_Ingest_With_Valid_Props(object props)
    {
        var appA = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var client = new IngestionClient(_fixture.CreateClient(), appA.AppKey);
        var code = await client.TrackEvent(DateTime.UtcNow, "Button Clicked", props);
        code.Should().Be(HttpStatusCode.OK);
    }
 
    public static IEnumerable<object[]> InvalidProps => 
    [
        [1],
        ["Something"],
        ["[1,2,3]"],
        [""],
        [new string[]{"A", "B", "C"}],
    ];

    [Theory, MemberData(nameof(InvalidProps))]
    public async Task Cant_Ingest_With_Invalid_Props(object props)
    {
        var appA = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var client = new IngestionClient(_fixture.CreateClient(), appA.AppKey);
        var code = await client.TrackEvent(DateTime.UtcNow, "Button Clicked", props);
        code.Should().Be(HttpStatusCode.BadRequest);
    }

    public static IEnumerable<object[]> ValidSessionIds => 
    [
        ["8ee47a56-a457-4513-a65f-2b8c3065eb95"],
        ["0ef724ce-7d46-4bde-9e0f-69303ef3f2af"],
        ["1234-5678-9012-3456"],
        [IngestionClient.NewSessionId().ToString()],
    ];

    [Theory, MemberData(nameof(ValidSessionIds))]
    public async Task Can_Ingest_Valid_SessionId(string sessionId)
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        client.SetSessionId(sessionId);

        var code = await client.TrackEvent(DateTime.UtcNow, "Button Clicked", null);
        code.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567890123456789012345678901234567890")]
    [InlineData(null)]
    public async Task Cant_Ingest_Invalid_SessionId(string? sessionId)
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        client.SetSessionId(sessionId);

        var code = await client.TrackEvent(DateTime.UtcNow, "Button Clicked", null);
        code.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Cant_Read_Stats_From_Other_Users()
    {
        var appA = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), appA.AppKey);
        await client.TrackEvent(DateTime.UtcNow, "Button Clicked", null);
        
        var responseA = await _fixture.UserA.GetKeyMetrics(appA.Id, "24h");
        responseA.StatusCode.Should().Be(HttpStatusCode.OK);

        var responseB = await _fixture.UserB.GetKeyMetrics(appA.Id, "24h");
        responseB.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Check_Rate_Limiting()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey, "10.0.0.0");
        for (var i = 1; i <= 22; i++)
        {
            var code1 = await client.TrackEvent(DateTime.UtcNow, "Button Clicked", null);

            if (i <= 20)
                code1.Should().Be(HttpStatusCode.OK);
            else
                code1.Should().Be(HttpStatusCode.TooManyRequests);
        }

        // A different IP should succeed because it uses a different IP
        var client2 = new IngestionClient(_fixture.CreateClient(), app.AppKey, "12.0.0.0");
        var code2 = await client2.TrackEvent(DateTime.UtcNow, "Button Clicked", null);
        code2.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();

        var count = await _fixture.UserA.CountEvents(app.Id, "24h");
        count.Should().Be(21);
    }

    private const string PersistentUserId = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public async Task Can_Ingest_With_Persistent_UserId()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        client.SetUserId(PersistentUserId.ToUpperInvariant());

        var code = await client.TrackEvent(DateTime.UtcNow, "App Started", null);
        code.Should().Be(HttpStatusCode.OK);

        code = await client.TrackEvents([
            (DateTime.UtcNow, "Menu Opened", null),
            (DateTime.UtcNow, "Button Clicked", null),
        ]);
        code.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();

        var userIds = await GetPersistentUserIds(app.Id);
        userIds.Should().HaveCount(3).And.AllBe(PersistentUserId);
    }

    [Fact]
    public async Task Can_Ingest_Without_Persistent_UserId()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        var code = await client.TrackEvent(DateTime.UtcNow, "App Started", null);
        code.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();

        var userIds = await GetPersistentUserIds(app.Id);
        userIds.Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task Cant_Ingest_Invalid_Persistent_UserId()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        client.SetUserId("76561198000000000");

        var response = await client.PostEvent(DateTime.UtcNow, "App Started", null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("UserID is invalid");

        await _eventWritter.FlushEvents();

        var count = await _fixture.UserA.CountEvents(app.Id, "24h");
        count.Should().Be(0);
    }

    [Fact]
    public async Task Can_Ingest_With_Timezone_Offset()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        client.SetTimezoneOffset(-300);

        var code = await client.TrackEvent(DateTime.UtcNow, "App Started", null);
        code.Should().Be(HttpStatusCode.OK);

        code = await client.TrackEvents([
            (DateTime.UtcNow, "Menu Opened", null),
            (DateTime.UtcNow, "Button Clicked", null),
        ]);
        code.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();

        var offsets = await GetTimezoneOffsets(app.Id);
        offsets.Should().HaveCount(3).And.AllBeEquivalentTo((short?)-300);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(900)]
    [InlineData(-721)]
    public async Task Stores_Missing_Or_Out_Of_Range_Timezone_Offset_As_Null(int? offset)
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());

        var client = new IngestionClient(_fixture.CreateClient(), app.AppKey);
        client.SetTimezoneOffset(offset);

        var code = await client.TrackEvent(DateTime.UtcNow, "App Started", null);
        code.Should().Be(HttpStatusCode.OK);

        await _eventWritter.FlushEvents();

        var offsets = await GetTimezoneOffsets(app.Id);
        offsets.Should().ContainSingle().Which.Should().BeNull();
    }

    private async Task<short?[]> GetTimezoneOffsets(string appId)
    {
        var conn = _fixture.Services.GetRequiredService<ClickHouseConnection>();
        var rows = await conn.QueryAsync<short?>(
            "SELECT timezone_offset FROM events WHERE app_id = @app_id",
            new { app_id = appId });
        return rows.ToArray();
    }

    private async Task<string?[]> GetPersistentUserIds(string appId)
    {
        var conn = _fixture.Services.GetRequiredService<ClickHouseConnection>();
        var rows = await conn.QueryAsync<string?>(
            "SELECT persistent_user_id FROM events WHERE app_id = @app_id",
            new { app_id = appId });
        return rows.ToArray();
    }
}