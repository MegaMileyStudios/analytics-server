using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aptabase.Features.Privacy;

public class DeletionRequestBody
{
    public string? UserId { get; set; }
}

// Public on purpose: the game opens a confirmation page on its own website, which posts here.
// There's no login, so anyone who knows a hash can request its deletion, the rate limit keeps guessing impractical.
[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class DeletionRequestController : Controller
{
    private readonly IPrivacyQueries _privacyQueries;

    public DeletionRequestController(IPrivacyQueries privacyQueries)
    {
        _privacyQueries = privacyQueries ?? throw new ArgumentNullException(nameof(privacyQueries));
    }

    [HttpPost("/api/v0/privacy/delete-request")]
    [EnableCors("AllowAny")]
    [EnableRateLimiting("DeletionRequest")]
    public async Task<IActionResult> Create([FromBody] DeletionRequestBody body, CancellationToken cancellationToken)
    {
        if (!PersistentUserId.TryNormalize(body.UserId, out var hash) || hash is null)
            return BadRequest(PersistentUserId.InvalidMessage);

        await _privacyQueries.CreateDeletionRequest(hash, cancellationToken);

        // Same response whether or not the hash has any data or a pending request
        return Accepted(new { });
    }
}
