using AwesomeAssertions;
using Aptabase.Data;
using Aptabase.Features;
using Aptabase.Features.Authentication;
using Aptabase.Features.Notification;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aptabase.IntegrationTests;

// Covers the DISABLE_SIGNUPS gate in AuthService.FindOrCreateAccountWithOAuthAsync.
// This needs a real Postgres round-trip via Dapper (FindUserByOAuthProviderAsync /
// FindUserByEmailAsync), so it lives here rather than in UnitTests. The equivalent gate on
// AuthController.Register has no DB dependency and is covered in UnitTests instead.
[Collection("Integration Tests")]
public class SignupsDisabledTests
{
    private readonly IntegrationTestsFixture _fixture;

    public SignupsDisabledTests(IntegrationTestsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FindOrCreateAccountWithOAuthAsync_Throws_For_New_User_When_Signups_Disabled()
    {
        var authService = CreateAuthService(signupsDisabled: true);
        var email = $"oauth.newbie.{Guid.NewGuid()}@got.com";

        var act = () => authService.FindOrCreateAccountWithOAuthAsync("New Person", email, "github", Guid.NewGuid().ToString(), default);

        await act.Should().ThrowAsync<Exception>();

        var user = await authService.FindUserByEmailAsync(email, default);
        user.Should().BeNull("no account should have been created as a side effect");
    }

    [Fact]
    public async Task FindOrCreateAccountWithOAuthAsync_Still_Allows_Existing_User_When_Signups_Disabled()
    {
        // Account creation itself isn't gated (only the auto-provisioning path is), so this
        // is a valid way to seed an existing user regardless of the flag under test below.
        var seedingService = CreateAuthService(signupsDisabled: false);
        var email = $"oauth.returning.{Guid.NewGuid()}@got.com";
        var existing = await seedingService.CreateAccountAsync("Returning Person", email, default);

        var authService = CreateAuthService(signupsDisabled: true);
        var providerUid = Guid.NewGuid().ToString();
        var user = await authService.FindOrCreateAccountWithOAuthAsync("Returning Person", email, "github", providerUid, default);

        user.Id.Should().Be(existing.Id);

        var attached = await authService.FindUserByOAuthProviderAsync("github", providerUid, default);
        attached.Should().NotBeNull();
        attached!.Id.Should().Be(existing.Id);
    }

    private AuthService CreateAuthService(bool signupsDisabled)
    {
        var env = (EnvSettings)Activator.CreateInstance(typeof(EnvSettings), nonPublic: true)!;
        typeof(EnvSettings).GetProperty(nameof(EnvSettings.SignupsDisabled))!.SetValue(env, signupsDisabled);

        return new AuthService(
            NullLogger<AuthService>.Instance,
            _fixture.Services.GetRequiredService<IDbContext>(),
            env,
            _fixture.Services.GetRequiredService<IHttpContextAccessor>(),
            _fixture.Services.GetRequiredService<IEmailClient>(),
            _fixture.Services.GetRequiredService<IAuthTokenManager>());
    }
}
