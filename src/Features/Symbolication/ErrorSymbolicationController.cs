using Aptabase.Data;
using Aptabase.Features.Authentication;
using Aptabase.Features.ErrorReporting;
using Aptabase.Features.Stats;
using Aptabase.Features.Symbolication.Engine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aptabase.Features.Symbolication;

[ApiController]
[IsAuthenticated]
[ResponseCache(NoStore = true)]
public class ErrorSymbolicationController : ControllerBase
{
    private readonly SymbolicationService _symbolication;
    private readonly ISymbolicationClient _client;
    private readonly IErrorQueryClient _errors;
    private readonly IDbContext _db;

    public ErrorSymbolicationController(SymbolicationService symbolication, ISymbolicationClient client, IErrorQueryClient errors, IDbContext db)
    {
        _symbolication = symbolication ?? throw new ArgumentNullException(nameof(symbolication));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    // eligible: the report came from the SDK and can be symbolicated. symbolication is null until that has happened.
    [HttpGet("/api/v0/apps/{appId}/errors/{errorId}/symbolication")]
    [EnableRateLimiting("Stats")]
    public async Task<IActionResult> GetSymbolication(string appId, string errorId, [FromQuery] string? buildMode, CancellationToken cancellationToken)
    {
        var user = HttpContext.GetCurrentUserIdentity();
        if (!await _db.HasReadAccessToApp(appId, user, cancellationToken))
            return StatusCode(403);

        var queryAppId = ErrorsController.ResolveAppId(appId, buildMode);
        var error = await _errors.GetErrorByIdAsync(queryAppId, errorId, cancellationToken);
        if (error is null)
            return NotFound(new { error = "Error not found" });

        var stored = CrashReportParser.IsEligible(error.StackTrace) ? await _client.GetAsync(queryAppId, errorId, cancellationToken) : null;
        return Ok(new
        {
            eligible = CrashReportParser.IsEligible(error.StackTrace),
            available = _symbolication.IsAvailable,
            symbolication = stored is null ? null : ToResponse(stored.SymbolicatedAt, stored.Result),
        });
    }

    [HttpPost("/api/v0/apps/{appId}/errors/{errorId}/symbolicate")]
    [EnableRateLimiting("Stats")]
    public async Task<IActionResult> Symbolicate(string appId, string errorId, [FromQuery] string? buildMode, CancellationToken cancellationToken)
    {
        var user = HttpContext.GetCurrentUserIdentity();
        if (!await _db.HasReadAccessToApp(appId, user, cancellationToken))
            return StatusCode(403);

        if (!_symbolication.IsAvailable)
            return StatusCode(503, new { error = _symbolication.UnavailableMessage });

        var queryAppId = ErrorsController.ResolveAppId(appId, buildMode);
        var result = await _symbolication.SymbolicateErrorAsync(queryAppId, errorId, cancellationToken);
        if (result is null)
            return NotFound(new { error = "Error not found, or it has nothing to symbolicate" });

        return Ok(new
        {
            eligible = true,
            available = true,
            symbolication = ToResponse(DateTime.UtcNow, result.ToJson()),
        });
    }

    private static object ToResponse(DateTime symbolicatedAt, string resultJson)
    {
        var result = SymbolicationResult.FromJson(resultJson);
        return new
        {
            symbolicatedAt,
            result,
            text = result?.ToText(),
        };
    }
}
