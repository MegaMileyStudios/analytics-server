using System.Net;
using Xunit;
using AwesomeAssertions;
using Aptabase.Data;
using Aptabase.Features.Authentication;
using Aptabase.IntegrationTests.Clients;
using Dapper;

namespace Aptabase.IntegrationTests;

// Covers sessions whose auth cookie is still validly signed but whose user no longer exists
// (account deleted, DB reset, API pointed at another DB). See issue #53.
[Collection("Integration Tests")]
public class MissingUserSessionTests
{
    private readonly IntegrationTestsFixture _fixture;

    public MissingUserSessionTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Me_Returns_Unauthorized_And_Clears_Cookie_When_User_Missing()
    {
        var (client, _) = await CreateAccountAndDeleteUser();

        var response = await client.GetMe();

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.TryGetValues("Set-Cookie", out var cookies).Should().BeTrue();
        cookies!.Should().Contain(c => c.StartsWith("auth-session=;") && c.Contains("expires=Thu, 01 Jan 1970"));
    }

    [Fact]
    public async Task CreateApp_Returns_Unauthorized_And_Inserts_Nothing_When_User_Missing()
    {
        var (client, userId) = await CreateAccountAndDeleteUser();

        var response = await client.PostApp("Orphan App");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var db = _fixture.Services.GetRequiredService<IDbContext>();
        var count = await db.Connection.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM apps WHERE owner_id = @userId", new { userId });
        count.Should().Be(0);
    }

    private async Task<(AccountClient Client, string UserId)> CreateAccountAndDeleteUser()
    {
        var client = new AccountClient(_fixture.CreateClient());
        await client.CreateAccount("Ned Stark", $"ned.stark.{Guid.NewGuid()}@got.com");

        var me = await client.GetMe();
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var user = await me.Content.ReadFromJsonAsync<UserAccount>();

        var db = _fixture.Services.GetRequiredService<IDbContext>();
        await db.Connection.ExecuteAsync("DELETE FROM users WHERE id = @id", new { id = user!.Id });

        return (client, user.Id);
    }
}
