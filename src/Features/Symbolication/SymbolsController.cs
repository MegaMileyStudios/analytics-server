using Aptabase.Features.Authentication;
using Aptabase.Features.Symbolication.Store;
using Microsoft.AspNetCore.Mvc;

namespace Aptabase.Features.Symbolication;

public class CreateSymbolUploadKeyBody
{
    public string Name { get; set; } = "";
}

// The dashboard side of the symbol store, which the whole server shares: upload keys and the stored uploads
[ApiController]
[IsAuthenticated]
[ResponseCache(NoStore = true)]
public class SymbolsController : Controller
{
    private const int MaxKeyNameLength = 100;

    private readonly ISymbolStore _store;
    private readonly SymbolUploadService _uploads;
    private readonly LlvmTools _tools;

    public SymbolsController(ISymbolStore store, SymbolUploadService uploads, LlvmTools tools)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _uploads = uploads ?? throw new ArgumentNullException(nameof(uploads));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    [HttpGet("/api/_symbols/keys")]
    public async Task<IActionResult> ListKeys(CancellationToken cancellationToken)
    {
        if (!await CanManage(cancellationToken))
            return StatusCode(403);

        return Ok(await _store.ListKeys(cancellationToken));
    }

    [HttpPost("/api/_symbols/keys")]
    public async Task<IActionResult> CreateKey([FromBody] CreateSymbolUploadKeyBody body, CancellationToken cancellationToken)
    {
        if (!await CanManage(cancellationToken))
            return StatusCode(403);

        var name = body.Name.Trim();
        if (name.Length == 0 || name.Length > MaxKeyNameLength)
            return BadRequest(new { errors = new Dictionary<string, string[]> { ["name"] = [$"Name must be 1 to {MaxKeyNameLength} characters"] } });

        var (key, secret) = await _store.CreateKey(name, this.GetCurrentUserIdentity().Id, cancellationToken);
        return Ok(new { key, secret });
    }

    [HttpDelete("/api/_symbols/keys/{id}")]
    public async Task<IActionResult> DeleteKey(string id, CancellationToken cancellationToken)
    {
        if (!await CanManage(cancellationToken))
            return StatusCode(403);

        return await _store.DeleteKey(id, cancellationToken) ? Ok(new { }) : NotFound();
    }

    [HttpGet("/api/_symbols/uploads")]
    public async Task<IActionResult> ListUploads(CancellationToken cancellationToken)
    {
        if (!await CanManage(cancellationToken))
            return StatusCode(403);

        var uploads = await _store.ListUploads(cancellationToken);
        return Ok(new
        {
            available = _tools.IsAvailable,
            unavailableMessage = _tools.IsAvailable ? null : _tools.MissingToolsMessage,
            uploads = uploads.Select(u => new
            {
                u.Id,
                u.Name,
                u.BuildId,
                u.KeyName,
                u.SizeBytes,
                u.CreatedAt,
                files = u.Files.Select(f => new
                {
                    f.DebugId,
                    f.Format,
                    f.Arch,
                    f.Rid,
                    f.ModuleName,
                    // The path inside the archive, without the upload's folder
                    path = f.Path[(f.Path.IndexOf('/') + 1)..],
                    f.SizeBytes,
                }),
            }),
        });
    }

    [HttpDelete("/api/_symbols/uploads/{id}")]
    public async Task<IActionResult> DeleteUpload(string id, CancellationToken cancellationToken)
    {
        if (!await CanManage(cancellationToken))
            return StatusCode(403);

        return await _uploads.DeleteAsync(id, cancellationToken) ? Ok(new { }) : NotFound();
    }

    private Task<bool> CanManage(CancellationToken cancellationToken) =>
        _store.CanManageSymbols(this.GetCurrentUserIdentity().Id, cancellationToken);
}
