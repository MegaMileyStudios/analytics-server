using Aptabase.Features.ErrorReporting;
using Aptabase.Features.Symbolication.Engine;

namespace Aptabase.Features.Symbolication;

public class SymbolicationService
{
    private readonly Symbolicator _symbolicator;
    private readonly ISymbolicationClient _client;
    private readonly IErrorQueryClient _errors;
    private readonly LlvmTools _tools;
    private readonly ILogger _logger;

    public SymbolicationService(Symbolicator symbolicator, ISymbolicationClient client, IErrorQueryClient errors, LlvmTools tools, ILogger<SymbolicationService> logger)
    {
        _symbolicator = symbolicator ?? throw new ArgumentNullException(nameof(symbolicator));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _errors = errors ?? throw new ArgumentNullException(nameof(errors));
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsAvailable => _tools.IsAvailable;

    public string UnavailableMessage => _tools.MissingToolsMessage;

    // appId is the stored app id (with the _DEBUG suffix for debug builds). Returns null when the error doesn't exist or has nothing to symbolicate.
    public async Task<SymbolicationResult?> SymbolicateErrorAsync(string appId, string errorId, CancellationToken cancellationToken)
    {
        var error = await _errors.GetErrorByIdAsync(appId, errorId, cancellationToken);
        if (error is null || !CrashReportParser.IsEligible(error.StackTrace))
            return null;

        var result = await _symbolicator.SymbolicateAsync(error.StackTrace, cancellationToken);
        await _client.SaveAsync([new SymbolicationRow(appId, errorId, error.Timestamp, result)], cancellationToken);
        return result;
    }

    // Returns the ids of the errors that were symbolicated, a report that fails is logged and left for a later attempt
    public async Task<IReadOnlyList<string>> SymbolicateAsync(IReadOnlyList<ErrorToSymbolicate> errors, CancellationToken cancellationToken)
    {
        var rows = new List<SymbolicationRow>();
        foreach (var error in errors)
        {
            try
            {
                var result = await _symbolicator.SymbolicateAsync(error.StackTrace, cancellationToken);
                rows.Add(new SymbolicationRow(error.AppId, error.ErrorId, error.Timestamp, result));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Symbolicating error {ErrorId} of app {AppId} failed", error.ErrorId, error.AppId);
            }
        }

        await _client.SaveAsync(rows, cancellationToken);
        return rows.Select(r => r.ErrorId).ToArray();
    }
}
