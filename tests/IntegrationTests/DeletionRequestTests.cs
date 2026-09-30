using Xunit;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Aptabase.Data;
using Aptabase.IntegrationTests.Clients;
using Aptabase.Features.ErrorReporting.Buffer;
using Aptabase.Features.Ingestion.Buffer;
using Aptabase.Features.Privacy;
using Aptabase.Features.ViewTracking.Buffer;
using ClickHouse.Client.ADO;
using Dapper;

namespace Aptabase.IntegrationTests;

[Collection("Integration Tests")]
public class DeletionRequestTests
{
    private static readonly Random _random = new();
    private readonly IntegrationTestsFixture _fixture;

    public DeletionRequestTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    private static string NewHash()
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())));

    private static string RandomIpAddress()
        => $"{_random.Next(1, 255)}.{_random.Next(0, 255)}.{_random.Next(0, 255)}.{_random.Next(0, 255)}";

    private async Task<HttpResponseMessage> PostDeletionRequest(string? userId, string? ipAddress = null)
    {
        var body = JsonContent.Create(new { userId });
        body.Headers.Add("CloudFront-Viewer-Address", ipAddress ?? RandomIpAddress());
        return await _fixture.CreateClient().PostAsync("/api/v0/privacy/delete-request", body);
    }

    private async Task<string[]> GetRequestStatuses(string hash)
    {
        var db = _fixture.Services.GetRequiredService<IDbContext>();
        var rows = await db.Connection.QueryAsync<string>(
            "SELECT status FROM deletion_requests WHERE hash = @hash ORDER BY requested_at",
            new { hash });
        return rows.ToArray();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("76561198000000000")]
    public async Task Rejects_Missing_Or_Invalid_UserId(string? userId)
    {
        var response = await PostDeletionRequest(userId);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be(PersistentUserId.InvalidMessage);
    }

    [Fact]
    public async Task Creates_A_Single_Pending_Request_Per_Hash()
    {
        var hash = NewHash();

        (await PostDeletionRequest(hash.ToUpperInvariant())).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await PostDeletionRequest(hash)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var statuses = await GetRequestStatuses(hash);
        statuses.Should().Equal("pending");
    }

    [Fact]
    public async Task Is_Rate_Limited_Per_Ip()
    {
        var ip = RandomIpAddress();

        for (var i = 0; i < 5; i++)
            (await PostDeletionRequest(NewHash(), ip)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        (await PostDeletionRequest(NewHash(), ip)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Allows_Requesting_Again_After_Completion()
    {
        var hash = NewHash();
        var job = _fixture.GetHostedService<ProcessDeletionRequestsCronJob>();

        (await PostDeletionRequest(hash)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await job.ProcessPendingRequests(CancellationToken.None);
        (await PostDeletionRequest(hash)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        var statuses = await GetRequestStatuses(hash);
        statuses.Should().Equal("completed", "pending");
    }

    [Fact]
    public async Task Processing_Erases_Events_And_Views_And_Unlinks_Errors()
    {
        var app = await _fixture.UserA.CreateApp(Guid.NewGuid().ToString());
        var deletedHash = NewHash();
        var keptHash = NewHash();

        foreach (var hash in new[] { deletedHash, keptHash })
        {
            var events = new IngestionClient(_fixture.CreateClient(), app.AppKey);
            events.SetUserId(hash);
            (await events.TrackEvent(DateTime.UtcNow, "app_started", null)).Should().Be(HttpStatusCode.OK);

            var views = new ViewsClient(_fixture.CreateClient(), app.AppKey) { UserId = hash };
            (await views.PostView(DateTime.UtcNow, "MainScreenChange", "Main Menu")).StatusCode.Should().Be(HttpStatusCode.OK);

            var errors = new ErrorsClient(_fixture.CreateClient(), app.AppKey);
            (await errors.TrackError(ErrorsClient.NewError(DateTime.UtcNow, "NullReferenceException", "Something broke", userId: hash))).Should().Be(HttpStatusCode.Accepted);
        }

        await _fixture.GetHostedService<EventBackgroundWritter>().FlushEvents();
        await _fixture.GetHostedService<ViewBackgroundWritter>().FlushViews();
        await _fixture.GetHostedService<ErrorBackgroundWritter>().FlushErrors();

        (await PostDeletionRequest(deletedHash)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await _fixture.GetHostedService<ProcessDeletionRequestsCronJob>().ProcessPendingRequests(CancellationToken.None);

        (await GetRequestStatuses(deletedHash)).Should().Equal("completed");

        // Mutations are applied asynchronously, give ClickHouse a moment to finish them
        var counts = await WaitForMutations(app.Id, deletedHash);
        counts.Should().Be((0, 0, 0, 2));

        (await CountRows(app.Id, keptHash)).Should().Be((1, 1, 1, 2));
    }

    private async Task<(long Events, long Views, long Errors, long AllErrors)> WaitForMutations(string appId, string hash)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var counts = await CountRows(appId, hash);
            if (counts is (0, 0, 0, _) || DateTime.UtcNow > deadline)
                return counts;

            await Task.Delay(250);
        }
    }

    private async Task<(long Events, long Views, long Errors, long AllErrors)> CountRows(string appId, string hash)
    {
        var conn = _fixture.Services.GetRequiredService<ClickHouseConnection>();
        var args = new { app_id = appId, hash };
        var events = await conn.ExecuteScalarAsync<long>("SELECT count() FROM events WHERE app_id = @app_id AND persistent_user_id = @hash", args);
        var views = await conn.ExecuteScalarAsync<long>("SELECT count() FROM view_events WHERE app_id = @app_id AND persistent_user_id = @hash", args);
        var errors = await conn.ExecuteScalarAsync<long>("SELECT count() FROM error_events WHERE app_id = @app_id AND persistent_user_id = @hash", args);
        var allErrors = await conn.ExecuteScalarAsync<long>("SELECT count() FROM error_events WHERE app_id = @app_id", new { app_id = appId });
        return (events, views, errors, allErrors);
    }
}
