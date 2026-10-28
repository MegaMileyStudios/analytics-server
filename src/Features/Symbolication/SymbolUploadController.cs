using Aptabase.Features.Symbolication.Store;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aptabase.Features.Symbolication;

// Called by CI and the publish script with a symbol archive as the request body:
// curl --fail -X POST -H "Authorization: Bearer $KEY" --data-binary @symbols.tar.gz "$SERVER/api/v0/symbols?buildId=$BUILD_ID&name=symbols.tar.gz"
[ApiController]
[ResponseCache(NoStore = true)]
public class SymbolUploadController : ControllerBase
{
    private const long MaxUploadBytes = 4L * 1024 * 1024 * 1024;
    private const int MaxBuildIdLength = 100;
    private const int MaxNameLength = 200;

    private readonly ISymbolStore _store;
    private readonly SymbolUploadService _uploads;
    private readonly LlvmTools _tools;

    public SymbolUploadController(ISymbolStore store, SymbolUploadService uploads, LlvmTools tools)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    // buildId is the game's BuildId (the first part of the "Build:" line of its reports), only game archives have one
    [HttpPost("/api/v0/symbols")]
    [EnableRateLimiting("SymbolUpload")]
    [RequestSizeLimit(MaxUploadBytes)]
    public async Task<IActionResult> Upload([FromQuery] string? buildId, [FromQuery] string? name, CancellationToken cancellationToken)
    {
        // The key is checked before anything of the body is read
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Unauthorized(new { error = "Missing upload key, send it as 'Authorization: Bearer <key>'" });

        var keyId = await _store.UseKey(header["Bearer ".Length..].Trim(), cancellationToken);
        if (keyId is null)
            return Unauthorized(new { error = "Invalid upload key" });

        if (!_tools.IsAvailable)
            return StatusCode(503, new { error = _tools.MissingToolsMessage });

        buildId = string.IsNullOrWhiteSpace(buildId) ? null : buildId.Trim();
        if (buildId is not null && (buildId.Length > MaxBuildIdLength || buildId.Any(char.IsWhiteSpace)))
            return BadRequest(new { error = $"buildId must be at most {MaxBuildIdLength} characters, without spaces" });

        name = string.IsNullOrWhiteSpace(name) ? $"upload-{DateTime.UtcNow:yyyyMMdd-HHmmss}" : name.Trim();
        if (name.Length > MaxNameLength)
            return BadRequest(new { error = $"name must be at most {MaxNameLength} characters" });

        try
        {
            return Ok(await _uploads.UploadAsync(Request.Body, name, buildId, keyId, cancellationToken));
        }
        catch (SymbolUploadException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
