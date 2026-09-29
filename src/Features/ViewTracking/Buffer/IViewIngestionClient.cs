using ClickHouse.Client.ADO;
using ClickHouse.Client.Copy;

namespace Aptabase.Features.ViewTracking.Buffer;

public interface IViewIngestionClient
{
    Task<long> BulkSendViewsAsync(IEnumerable<ViewRow> rows, CancellationToken ct = default);
}

public class ClickHouseViewIngestionClient : IViewIngestionClient
{
    private readonly ClickHouseConnection _conn;

    private readonly string[] COLUMNS = [
        "app_id",
        "timestamp",
        "session_id",
        "persistent_user_id",
        "view_type",
        "view_name",
        "os_name",
        "os_version",
        "device_model",
        "locale",
        "app_version",
        "app_build_number",
        "engine_name",
        "engine_version",
        "sdk_version",
        "country_code",
        "region_name",
        "ttl"
    ];

    public ClickHouseViewIngestionClient(ClickHouseConnection conn)
    {
        _conn = conn ?? throw new ArgumentNullException(nameof(conn));
    }

    public async Task<long> BulkSendViewsAsync(IEnumerable<ViewRow> rows, CancellationToken ct = default)
    {
        using var bulkCopy = new ClickHouseBulkCopy(_conn)
        {
            DestinationTableName = "view_events",
            BatchSize = 1000,
            ColumnNames = COLUMNS,
        };

        var values = rows.Select(row => new object[] {
            row.AppId,
            row.Timestamp,
            row.SessionId,
            row.PersistentUserId ?? (object)DBNull.Value,
            row.ViewType,
            row.ViewName,
            row.OSName,
            row.OSVersion,
            row.DeviceModel,
            row.Locale,
            row.AppVersion,
            row.AppBuildNumber,
            row.EngineName,
            row.EngineVersion,
            row.SdkVersion,
            row.CountryCode,
            row.RegionName,
            row.TTL,
        });
        await bulkCopy.InitAsync();
        await bulkCopy.WriteToServerAsync(values, ct);
        return bulkCopy.RowsWritten;
    }
}

// View tracking is only implemented for ClickHouse. On Tinybird the views are accepted
// (so SDKs don't retry them) and dropped, and the view queries return empty results.
public class NoopViewIngestionClient : IViewIngestionClient
{
    public Task<long> BulkSendViewsAsync(IEnumerable<ViewRow> rows, CancellationToken ct = default)
    {
        return Task.FromResult(0L);
    }
}
