using Aptabase.Features.ErrorReporting.Buffer;
using Aptabase.Features.Stats;
using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;

namespace Aptabase.Features.Symbolication;

public class ErrorToSymbolicate
{
    private DateTime _timestamp;

    public string AppId { get; set; } = "";
    public string ErrorId { get; set; } = "";
    public string StackTrace { get; set; } = "";

    // Stored as UTC wall time, see ErrorDto.Timestamp
    public DateTime Timestamp
    {
        get => _timestamp;
        set => _timestamp = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}

public class StoredSymbolication
{
    private DateTime _symbolicatedAt;

    public string Status { get; set; } = "";
    public int ResolvedFrames { get; set; }
    public int TotalFrames { get; set; }
    public string Result { get; set; } = "";

    public DateTime SymbolicatedAt
    {
        get => _symbolicatedAt;
        set => _symbolicatedAt = DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }
}

public record SymbolicationRow(string AppId, string ErrorId, DateTime ErrorTimestamp, Engine.SymbolicationResult Result);

public interface ISymbolicationClient
{
    Task<ErrorToSymbolicate[]> GetUnsymbolicatedAsync(int days, int limit, CancellationToken cancellationToken);
    Task<ErrorToSymbolicate[]> GetMissingSymbolsAsync(string[] keys, int limit, CancellationToken cancellationToken);
    Task<StoredSymbolication?> GetAsync(string appId, string errorId, CancellationToken cancellationToken);
    Task SaveAsync(IReadOnlyList<SymbolicationRow> rows, CancellationToken cancellationToken);
}

// Results live in error_symbolications next to error_events, the original report is never changed.
// A re-symbolication adds a newer row, ReplacingMergeTree keeps the latest per error.
public class ClickHouseSymbolicationClient : ISymbolicationClient
{
    private static readonly string[] Columns = ["app_id", "error_id", "symbolicated_at", "status", "resolved_frames", "total_frames", "missing_keys", "result", "ttl"];

    private readonly IQueryClient _queryClient;
    private readonly ClickHouseConnection _conn;

    public ClickHouseSymbolicationClient(IQueryClient queryClient, ClickHouseConnection conn)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
        _conn = conn ?? throw new ArgumentNullException(nameof(conn));
    }

    public async Task<ErrorToSymbolicate[]> GetUnsymbolicatedAsync(int days, int limit, CancellationToken cancellationToken)
    {
        var rows = await _queryClient.NamedQueryAsync<ErrorToSymbolicate>("get_unsymbolicated_errors__v1", new { days, limit }, cancellationToken);
        return rows.ToArray();
    }

    public async Task<ErrorToSymbolicate[]> GetMissingSymbolsAsync(string[] keys, int limit, CancellationToken cancellationToken)
    {
        if (keys.Length == 0)
            return [];

        var rows = await _queryClient.NamedQueryAsync<ErrorToSymbolicate>("get_errors_missing_symbols__v1", new { keys, limit }, cancellationToken);
        return rows.ToArray();
    }

    public async Task<StoredSymbolication?> GetAsync(string appId, string errorId, CancellationToken cancellationToken)
    {
        var rows = await _queryClient.NamedQueryAsync<StoredSymbolication>("get_error_symbolication__v1", new { app_id = appId, error_id = errorId }, cancellationToken);
        return rows.FirstOrDefault();
    }

    public async Task SaveAsync(IReadOnlyList<SymbolicationRow> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
            return;

        using var bulkCopy = new ClickHouseBulkCopy(_conn)
        {
            DestinationTableName = "error_symbolications",
            BatchSize = 1000,
            ColumnNames = Columns,
        };

        var now = DateTime.UtcNow;
        var values = rows.Select(row => new object[]
        {
            row.AppId,
            row.ErrorId,
            now,
            row.Result.Status,
            (ushort)Math.Min(row.Result.ResolvedFrames, ushort.MaxValue),
            (ushort)Math.Min(row.Result.TotalFrames, ushort.MaxValue),
            row.Result.MissingKeys.ToArray(),
            row.Result.ToJson(),
            // Expires together with the original report
            row.ErrorTimestamp.Add(ErrorRow.ErrorTTL),
        });
        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(values, cancellationToken);
    }
}
