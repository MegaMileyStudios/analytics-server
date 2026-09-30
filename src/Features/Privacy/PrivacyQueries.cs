using Aptabase.Data;
using Dapper;

namespace Aptabase.Features.Privacy;

public record DeletionRequest(string Id, string Hash);

public interface IPrivacyQueries
{
    Task<int> PurgeOldSalts(CancellationToken cancellationToken);
    Task CreateDeletionRequest(string hash, CancellationToken cancellationToken);
    Task<DeletionRequest[]> GetPendingDeletionRequests(int limit, CancellationToken cancellationToken);
    Task MarkDeletionRequestsCompleted(string[] ids, CancellationToken cancellationToken);
}

public class PrivacyQueries : IPrivacyQueries
{
    private readonly IDbContext _db;

    public PrivacyQueries(IDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<int> PurgeOldSalts(CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition(
            "DELETE FROM app_salts WHERE TO_DATE(date, 'YYYY/MM/DD') <= CURRENT_DATE - INTERVAL '2' DAY",
            cancellationToken: cancellationToken
        );

        return await _db.Connection.ExecuteAsync(cmd);
    }

    public async Task CreateDeletionRequest(string hash, CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition(
            @"INSERT INTO deletion_requests (id, hash, requested_at, status)
              VALUES (@id, @hash, NOW(), 'pending')
              ON CONFLICT (hash) WHERE status = 'pending' DO NOTHING",
            new { id = NanoId.New(), hash },
            cancellationToken: cancellationToken
        );

        await _db.Connection.ExecuteAsync(cmd);
    }

    public async Task<DeletionRequest[]> GetPendingDeletionRequests(int limit, CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition(
            @"SELECT id, hash FROM deletion_requests
              WHERE status = 'pending'
              ORDER BY requested_at
              LIMIT @limit",
            new { limit },
            cancellationToken: cancellationToken
        );

        var rows = await _db.Connection.QueryAsync<DeletionRequest>(cmd);
        return rows.ToArray();
    }

    public async Task MarkDeletionRequestsCompleted(string[] ids, CancellationToken cancellationToken)
    {
        var cmd = new CommandDefinition(
            @"UPDATE deletion_requests
              SET status = 'completed', completed_at = NOW()
              WHERE id = ANY(@ids)",
            new { ids },
            cancellationToken: cancellationToken
        );

        await _db.Connection.ExecuteAsync(cmd);
    }
}
