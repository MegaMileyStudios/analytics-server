using ClickHouse.Client.ADO;
using ClickHouse.Client.Utility;

namespace Aptabase.Features.Privacy;

public interface IUserDataEraser
{
    Task EraseAsync(string[] hashes, CancellationToken cancellationToken);
}

// Removes everything tied to a persistent user ID. These are ClickHouse mutations, so they are
// applied asynchronously in the background after the statement returns.
public class ClickHouseUserDataEraser : IUserDataEraser
{
    private static readonly string[] Statements =
    [
        "ALTER TABLE events DELETE WHERE persistent_user_id IN {hashes:Array(String)}",
        "ALTER TABLE view_events DELETE WHERE persistent_user_id IN {hashes:Array(String)}",
        // Errors are kept so they can still be resolved, only the link to the user is removed
        "ALTER TABLE error_events UPDATE persistent_user_id = NULL WHERE persistent_user_id IN {hashes:Array(String)}",
    ];

    private readonly ClickHouseConnection _conn;

    public ClickHouseUserDataEraser(ClickHouseConnection conn)
    {
        _conn = conn ?? throw new ArgumentNullException(nameof(conn));
    }

    public async Task EraseAsync(string[] hashes, CancellationToken cancellationToken)
    {
        foreach (var statement in Statements)
        {
            using var command = _conn.CreateCommand();
            command.CommandText = statement;
            command.AddParameter("hashes", hashes);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
