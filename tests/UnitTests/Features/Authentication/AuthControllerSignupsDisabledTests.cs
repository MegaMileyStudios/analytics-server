using Xunit;
using Moq;
using AwesomeAssertions;
using Aptabase.Features;
using Aptabase.Features.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aptabase.UnitTests.Features.Authentication;

// Covers the DISABLE_SIGNUPS gate on AuthController.Register and the flag it exposes via
// AuthController.Providers. The equivalent gate on AuthService.FindOrCreateAccountWithOAuthAsync
// needs a real Postgres round-trip via Dapper and is covered in IntegrationTests instead.
public class AuthControllerSignupsDisabledTests
{
    private readonly Mock<IAuthService> _authService = new();
    private readonly Mock<IAuthTokenManager> _tokenManager = new();

    [Fact]
    public async Task Register_Returns_NotFound_For_Unknown_Email_When_Signups_Disabled()
    {
        _authService
            .Setup(s => s.FindUserByEmailAsync("newbie@got.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        var controller = CreateController(signupsDisabled: true);
        var result = await controller.Register(new RegisterBodyRequest { Name = "New Person", Email = "newbie@got.com" }, default);

        result.Should().BeOfType<NotFoundObjectResult>();
        _authService.Verify(s => s.SendRegisterEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact]
    public async Task Register_Still_Succeeds_For_Existing_Email_When_Signups_Disabled()
    {
        _authService
            .Setup(s => s.FindUserByEmailAsync("jon.snow@got.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UserAccount { Id = "u1", Name = "Jon Snow", Email = "jon.snow@got.com" });

        var controller = CreateController(signupsDisabled: true);
        var result = await controller.Register(new RegisterBodyRequest { Name = "Jon Snow", Email = "jon.snow@got.com" }, default);

        result.Should().BeOfType<OkObjectResult>();
        _authService.Verify(s => s.SendRegisterEmailAsync("Jon Snow", "jon.snow@got.com", It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task Register_Succeeds_For_Any_Email_When_Signups_Enabled()
    {
        var controller = CreateController(signupsDisabled: false);
        var result = await controller.Register(new RegisterBodyRequest { Name = "New Person", Email = "newbie@got.com" }, default);

        result.Should().BeOfType<OkObjectResult>();
        // The existing-user lookup is only needed to enforce the gate, so it must be skipped entirely when signups are enabled.
        _authService.Verify(s => s.FindUserByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        _authService.Verify(s => s.SendRegisterEmailAsync("New Person", "newbie@got.com", It.IsAny<CancellationToken>()), Times.Once());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Providers_Reports_The_SignupsDisabled_Flag(bool signupsDisabled)
    {
        var controller = CreateController(signupsDisabled);
        var result = controller.Providers();

        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var value = okResult.Value!;
        var flag = value.GetType().GetProperty("signupsDisabled")!.GetValue(value);

        flag.Should().Be(signupsDisabled);
    }

    private AuthController CreateController(bool signupsDisabled)
    {
        return new AuthController(
            Mock.Of<ILogger<AuthController>>(),
            CreateEnvSettings(signupsDisabled),
            _authService.Object,
            _tokenManager.Object);
    }

    // EnvSettings can only be populated from environment variables (EnvSettings.Load), so
    // tests create an instance via its private constructor and flip the single flag needed.
    private static EnvSettings CreateEnvSettings(bool signupsDisabled)
    {
        var env = (EnvSettings)Activator.CreateInstance(typeof(EnvSettings), nonPublic: true)!;
        typeof(EnvSettings)
            .GetProperty(nameof(EnvSettings.SignupsDisabled))!
            .SetValue(env, signupsDisabled);
        return env;
    }
}
