using System.Security.Claims;
using Xunit;
using Moq;
using AwesomeAssertions;
using Aptabase.Features;
using Aptabase.Features.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aptabase.UnitTests.Features.Authentication;

// Covers AuthController.Me for sessions whose user no longer exists (issue #53). The cookie
// clearing and app-creation guard need a real HTTP pipeline and Postgres, so they are covered
// in IntegrationTests (MissingUserSessionTests) instead.
public class AuthControllerMeTests
{
    private readonly Mock<IAuthService> _authService = new();

    [Fact]
    public async Task Me_Returns_Unauthorized_And_Signs_Out_When_User_Missing()
    {
        _authService
            .Setup(s => s.FindUserByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserAccount?)null);

        var result = await CreateController("u1").Me(default);

        result.Should().BeOfType<UnauthorizedResult>();
        _authService.Verify(s => s.SignOutAsync(), Times.Once());
    }

    [Fact]
    public async Task Me_Returns_User_When_Found()
    {
        var user = new UserAccount { Id = "u1", Name = "Jon Snow", Email = "jon.snow@got.com" };
        _authService
            .Setup(s => s.FindUserByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await CreateController("u1").Me(default);

        result.Should().BeOfType<OkObjectResult>().Which.Value.Should().Be(user);
        _authService.Verify(s => s.SignOutAsync(), Times.Never());
    }

    private AuthController CreateController(string userId)
    {
        var env = (EnvSettings)Activator.CreateInstance(typeof(EnvSettings), nonPublic: true)!;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("id", userId) }, "test"));

        return new AuthController(
            Mock.Of<ILogger<AuthController>>(),
            env,
            _authService.Object,
            Mock.Of<IAuthTokenManager>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = principal } }
        };
    }
}
