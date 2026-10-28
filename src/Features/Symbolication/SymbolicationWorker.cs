using Aptabase.Features.Symbolication.Store;

namespace Aptabase.Features.Symbolication;

// Symbolicates new error reports shortly after they arrive, and re-symbolicates reports whose symbols were uploaded later
public class SymbolicationWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    // Older reports are only symbolicated on request (or when their missing symbols are uploaded)
    private const int NewReportsMaxAgeDays = 30;
    private const int BatchSize = 20;
    private const int MaxBatchesPerRun = 10;

    private readonly SymbolicationService _symbolication;
    private readonly ISymbolicationClient _client;
    private readonly ISymbolStore _store;
    private readonly SymbolStorage _storage;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _runLock = new(1, 1);

    // Symbols that changed since this point are checked against stored results that were missing them.
    // On startup that's the last hour, which covers uploads that finished just before a restart.
    private DateTime? _lastStoreChange = DateTime.UtcNow.AddHours(-1);

    public SymbolicationWorker(SymbolicationService symbolication, ISymbolicationClient client, ISymbolStore store, SymbolStorage storage, ILogger<SymbolicationWorker> logger)
    {
        _symbolication = symbolication ?? throw new ArgumentNullException(nameof(symbolication));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        // Don't hold up startup (and the health check) with the tool check
        await Task.Yield();

        _storage.CleanStaging();
        if (!_symbolication.IsAvailable)
        {
            _logger.LogWarning("SymbolicationWorker is not running: {Message}", _symbolication.UnavailableMessage);
            return;
        }

        _logger.LogInformation("SymbolicationWorker is starting.");
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPending(cancellationToken);
                await Task.Delay(Interval, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SymbolicationWorker stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SymbolicationWorker crashed.");
                await Task.Delay(Interval, cancellationToken).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    // Returns how many reports were symbolicated
    public async Task<int> ProcessPending(CancellationToken cancellationToken)
    {
        await _runLock.WaitAsync(cancellationToken);
        try
        {
            var attempted = new HashSet<string>();

            var changes = await _store.ChangesSince(_lastStoreChange, cancellationToken);
            var total = await ProcessBatches(limit => _client.GetMissingSymbolsAsync(changes.Keys, limit, cancellationToken), attempted, cancellationToken);
            _lastStoreChange = changes.LastModifiedAt;

            total += await ProcessBatches(limit => _client.GetUnsymbolicatedAsync(NewReportsMaxAgeDays, limit, cancellationToken), attempted, cancellationToken);
            if (total > 0)
                _logger.LogInformation("Symbolicated {Count} error reports", total);
            return total;
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task<int> ProcessBatches(Func<int, Task<ErrorToSymbolicate[]>> fetch, HashSet<string> attempted, CancellationToken cancellationToken)
    {
        var total = 0;
        for (var batch = 0; batch < MaxBatchesPerRun; batch++)
        {
            var errors = await fetch(BatchSize);

            // A report that failed is returned again by the next query, it's retried on the next run instead
            var todo = errors.Where(e => attempted.Add($"{e.AppId}/{e.ErrorId}")).ToArray();
            if (todo.Length == 0)
                break;

            total += (await _symbolication.SymbolicateAsync(todo, cancellationToken)).Count;
            if (errors.Length < BatchSize)
                break;
        }
        return total;
    }
}
