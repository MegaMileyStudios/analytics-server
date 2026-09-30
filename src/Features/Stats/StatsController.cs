using Aptabase.Features.Authentication;
using Aptabase.Features.GeoIP;
using Aptabase.Features.Privacy;
using Aptabase.Features.ViewTracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aptabase.Features.Stats;

public record PeriodicStatsRow
{
    public DateTime Period { get; set; }
    public double Users { get; set; } = 0;
    public double PersistentUsers { get; set; } = 0;
    public double InferredUsers { get; set; } = 0;
    public int Sessions { get; set; } = 0;
    public int PersistentSessions { get; set; } = 0;
    public int InferredSessions { get; set; } = 0;
    public int Events { get; set; } = 0;
    public int PersistentEvents { get; set; } = 0;
    public int InferredEvents { get; set; } = 0;
}

public record KeyMetrics
{
    public KeyMetricsRow Current { get; set; } = new KeyMetricsRow();
    public KeyMetricsRow? Previous { get; set; }

    public KeyMetrics()
    {
    }

    public KeyMetrics(KeyMetricsRow current)
    {
        Current = current;
    }

    public KeyMetrics(KeyMetricsRow current, KeyMetricsRow previous)
    {
        Current = current;
        Previous = previous;
    }
}

public record KeyMetricsRow
{
    public double DailyUsers { get; set; } = 0;
    public double DailyPersistentUsers { get; set; } = 0;
    public double DailyInferredUsers { get; set; } = 0;
    public int Sessions { get; set; } = 0;
    public int Events { get; set; } = 0;
    public double DurationSeconds { get; set; } = 0;
}

public record TopNItem
{
    public string Name { get; set; } = "";
    public double Value { get; set; }
}

public record EventPropKeys
{
    public string[] StringKeys { get; set; } = Array.Empty<string>();
    public string[] NumericKeys { get; set; } = Array.Empty<string>();
}

public record EventPropsItem
{
    public string StringKey { get; set; } = "";
    public string StringValue { get; set; } = "";
    public string NumericKey { get; set; } = "";
    public int Events { get; set; }
    public decimal Median { get; set; }
    public decimal Min { get; set; }
    public decimal Max { get; set; }
    public decimal Sum { get; set; }
}

public record EventPropKey
{
    public string Key { get; set; } = "";
    public string Type { get; set; } = "";
}

public record EventPropNumericRow
{
    public DateTime Period { get; set; }
    public int Events { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Avg { get; set; }
    public double Median { get; set; }
    public double P95 { get; set; }
    public double P99 { get; set; }
    public double Sum { get; set; }
}

public record EventPropValueRow
{
    public int Undefined { get; set; }
    public string Value { get; set; } = "";
    public int Events { get; set; }
}

public record EventPropDistributionItem
{
    public string Name { get; set; } = "";
    public int Value { get; set; }
    public bool IsOther { get; set; }
    public bool IsUndefined { get; set; }
}

public record EventPropDistribution
{
    public const int MaxItems = 10;

    public string Type { get; set; } = "string";
    public EventPropDistributionItem[] Items { get; set; } = Array.Empty<EventPropDistributionItem>();

    // Booleans are always True, False and Undefined. Strings show up to 10 values,
    // when there are more the top 9 are kept and the rest is summed up as Other.
    public static EventPropDistribution FromRows(IEnumerable<EventPropValueRow> rows)
    {
        var defined = rows.Where(r => r.Undefined == 0)
            .OrderByDescending(r => r.Events)
            .ThenBy(r => r.Value, StringComparer.Ordinal)
            .ToArray();
        var undefinedEvents = rows.Where(r => r.Undefined != 0).Sum(r => r.Events);

        if (defined.Length > 0 && defined.All(r => r.Value is "true" or "false"))
        {
            return new EventPropDistribution
            {
                Type = "boolean",
                Items = new[]
                {
                    new EventPropDistributionItem { Name = "True", Value = defined.Where(r => r.Value == "true").Sum(r => r.Events) },
                    new EventPropDistributionItem { Name = "False", Value = defined.Where(r => r.Value == "false").Sum(r => r.Events) },
                    new EventPropDistributionItem { Name = "Undefined", Value = undefinedEvents, IsUndefined = true },
                },
            };
        }

        var items = defined.Select(r => new EventPropDistributionItem { Name = r.Value, Value = r.Events });
        if (defined.Length > MaxItems)
        {
            items = items.Take(MaxItems - 1).Append(new EventPropDistributionItem
            {
                Name = "Other",
                Value = defined.Skip(MaxItems - 1).Sum(r => r.Events),
                IsOther = true,
            });
        }

        return new EventPropDistribution { Type = "string", Items = items.ToArray() };
    }
}

public record EventPropPeriodicValueRow
{
    public DateTime Period { get; set; }
    public int Undefined { get; set; }
    public string Value { get; set; } = "";
    public int Events { get; set; }
}

public record EventPropDistributionSeriesItem
{
    public string Name { get; set; } = "";
    public bool IsOther { get; set; }
    public bool IsUndefined { get; set; }
    public int[] Values { get; set; } = Array.Empty<int>();
}

public record EventPropDistributionSeries
{
    public string Type { get; set; } = "string";
    public DateTime[] Periods { get; set; } = Array.Empty<DateTime>();
    public EventPropDistributionSeriesItem[] Series { get; set; } = Array.Empty<EventPropDistributionSeriesItem>();

    public static EventPropDistributionSeries FromRows(IEnumerable<EventPropPeriodicValueRow> rows)
    {
        var all = rows.ToArray();
        var periods = all.Select(r => r.Period).Distinct().OrderBy(p => p).ToArray();
        var periodIndex = periods.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i);

        var withEvents = all.Where(r => r.Events > 0).ToArray();
        var totals = withEvents
            .GroupBy(r => (r.Undefined, r.Value))
            .Select(g => new EventPropValueRow { Undefined = g.Key.Undefined, Value = g.Key.Value, Events = g.Sum(r => r.Events) });
        var distribution = EventPropDistribution.FromRows(totals);
        var isBoolean = distribution.Type == "boolean";

        var series = distribution.Items
            .Select(item => new EventPropDistributionSeriesItem
            {
                Name = item.Name,
                IsOther = item.IsOther,
                IsUndefined = item.IsUndefined,
                Values = new int[periods.Length],
            })
            .ToArray();
        var namedIndex = distribution.Items
            .Select((item, i) => (item, i))
            .Where(x => !x.item.IsOther && !x.item.IsUndefined)
            .ToDictionary(x => x.item.Name, x => x.i);
        var otherIndex = Array.FindIndex(distribution.Items, x => x.IsOther);

        int? SeriesOf(EventPropPeriodicValueRow row)
        {
            if (isBoolean)
                return row.Undefined != 0 ? 2 : row.Value == "true" ? 0 : 1;
            if (row.Undefined != 0)
                return null;
            if (namedIndex.TryGetValue(row.Value, out var index))
                return index;
            return otherIndex >= 0 ? otherIndex : null;
        }

        foreach (var row in withEvents)
        {
            if (SeriesOf(row) is int index)
                series[index].Values[periodIndex[row.Period]] += row.Events;
        }

        return new EventPropDistributionSeries { Type = distribution.Type, Periods = periods, Series = series };
    }
}

public record LiveGeoDataPoint
{
    public string CountryCode { get; set; } = "";
    public string RegionName { get; set; } = "";
    public ulong Users { get; set; }
    public ulong PersistentUsers { get; set; }
    public ulong InferredUsers { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public record LiveRecentSession
{
    public string Id { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public ulong Duration { get; set; }
    public ulong EventsCount { get; set; }
    public string AppVersion { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string RegionName { get; set; } = "";
    public string OsName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string DeviceModel { get; set; } = "";
    public string? PersistentUserId { get; set; }
}

public record SessionTimeline
{
    public string Id { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public ulong Duration { get; set; }
    public ulong EventsCount { get; set; }
    public string AppVersion { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string RegionName { get; set; } = "";
    public string OsName { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public string? PersistentUserId { get; set; }
    public string[] EventsName { get; set; } = Array.Empty<string>();
    public DateTime[] EventsTimestamp { get; set; } = Array.Empty<DateTime>();
    public string[] EventsStringProps { get; set; } = Array.Empty<string>();
    public string[] EventsNumericProps { get; set; } = Array.Empty<string>();
    public string[] ViewsType { get; set; } = Array.Empty<string>();
    public string[] ViewsName { get; set; } = Array.Empty<string>();
    public DateTime[] ViewsTimestamp { get; set; } = Array.Empty<DateTime>();
}

public record RetentionRow
{
    public DateTime CohortDate { get; set; }
    public uint PeriodNumber { get; set; }
    public ulong Users { get; set; }
}

public record RetentionCohort
{
    public DateTime CohortDate { get; set; }
    public int Size { get; set; }
    public int?[] Retained { get; set; } = Array.Empty<int?>();
}

public record RetentionCohorts
{
    public string Granularity { get; set; } = "";
    public int Periods { get; set; }
    public RetentionCohort[] Cohorts { get; set; } = Array.Empty<RetentionCohort>();
}

public enum CohortGranularity
{
    Day,
    Week,
    Month
}

public class RetentionQueryParams
{
    public string BuildMode { get; set; } = "";
    public string AppId { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public CohortGranularity Granularity { get; set; } = CohortGranularity.Day;
    public int? Periods { get; set; }
}

public enum LocalPlayTimeMetric
{
    Sessions,
    Events
}

public class LocalPlayTimeQueryParams : QueryParams
{
    public LocalPlayTimeMetric Metric { get; set; } = LocalPlayTimeMetric.Sessions;
    // The viewer's IANA time zone, used when LocalTime is off
    public string? TimeZone { get; set; }
}

public record LocalPlayTimeRow
{
    // 1 = Monday ... 7 = Sunday
    public byte DayOfWeek { get; set; }
    public byte Hour { get; set; }
    public ulong Value { get; set; }
}

public record MissingTimezoneOffset
{
    public ulong Events { get; set; }
}

public enum TopNValue
{
    UniqueSessions,
    TotalEvents
}

public enum GranularityEnum
{
    Hour,
    Day,
    Month
}

public record QueryArgs
{
    public string AppId { get; set; } = "";
    public string? SessionId { get; set; } = "";
    public DateTime? DateFrom { get; set; }
    public DateTime? DateTo { get; set; }
    public GranularityEnum? Granularity { get; set; }
    public string? CountryCode { get; set; }
    public string? OsName { get; set; }
    public string? DeviceModel { get; set; }
    public string? EventName { get; set; }
    public string? AppVersion { get; set; }
    public string? PersistentUserId { get; set; }
    public string? PropKey { get; set; }
    // Bucket and filter by the player's local time instead of real (UTC) time
    public bool LocalTime { get; set; }

    // Liquid treats any non-empty string as true (FormatArg would render a bool as "False"),
    // so the flag is passed as "1" or left out
    public string? LocalTimeArg => LocalTime ? "1" : null;

    public QueryArgs CloneToPreviousInterval()
    {
        if (!DateFrom.HasValue || !DateTo.HasValue)
        {
            return this;
        }

        var previousEnd = DateFrom.Value;
        // Go back 1 second so that when relativeTo is 00:00:00 we start with the previous day
        // Happens when looking for previous period and "period" is "month" or "last-month"
        if (previousEnd.TimeOfDay == TimeSpan.Zero)
            previousEnd = previousEnd.AddSeconds(-1);
        var previousStart =
            DateFrom.Value.Subtract(
                DateTo.Value.Subtract((DateFrom.Value))); //start - (end - start)

        return new QueryArgs
        {
            AppId = AppId,
            SessionId = SessionId,
            DateFrom = previousStart,
            DateTo = previousEnd,
            Granularity = Granularity,
            CountryCode = CountryCode,
            OsName = OsName,
            EventName = EventName,
            AppVersion = AppVersion,
            PersistentUserId = PersistentUserId,
            PropKey = PropKey,
            LocalTime = LocalTime,
        };
    }
}

public class QueryParams
{
    public string BuildMode { get; set; } = "";
    public string AppId { get; set; } = "";
    public string? SessionId { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public GranularityEnum? Granularity { get; set; }
    public string? CountryCode { get; set; }
    public string? OsName { get; set; }
    public string? EventName { get; set; }
    public string? AppVersion { get; set; }
    public string? DeviceModel { get; set; }
    public string? PersistentUserId { get; set; }
    public string? PropKey { get; set; }
    public bool LocalTime { get; set; }

    public QueryArgs Parse()
    {
        var appId = BuildMode.ToLower() switch
        {
            "debug" => $"{AppId}_DEBUG",
            _ => AppId,
        };

        return new QueryArgs
        {
            AppId = appId,
            SessionId = SessionId,
            DateFrom = StartDate,
            DateTo = EndDate,
            Granularity = Granularity,
            CountryCode = CountryCode,
            OsName = OsName,
            DeviceModel = DeviceModel,
            EventName = EventName,
            AppVersion = AppVersion,
            PersistentUserId = PersistentUserId,
            PropKey = PropKey,
            LocalTime = LocalTime,
        };
    }
}

[ApiController, IsAuthenticated, HasReadAccessToApp]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("Stats")]
public class StatsController : Controller
{
    private readonly IQueryClient _queryClient;
    private readonly GeoIPClient _geodb;

    public StatsController(IQueryClient queryClient, GeoIPClient geodb)
    {
        _geodb = geodb ?? throw new ArgumentNullException(nameof(geodb));
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
    }

    [HttpGet("/api/_stats/top-countries")]
    public async Task<IActionResult> TopCountries([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        return await TopN("country_code", body.Granularity ?? GranularityEnum.Hour, TopNValue.UniqueSessions, body, cancellationToken);
    }

    [HttpGet("/api/_stats/top-osversions")]
    public async Task<IActionResult> TopOSVersions([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        return await TopN("os_version", body.Granularity ?? GranularityEnum.Hour, TopNValue.UniqueSessions, body, cancellationToken);
    }

    [HttpGet("/api/_stats/top-devices")]
    public async Task<IActionResult> TopDevices([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        return await TopN("device_model", body.Granularity ?? GranularityEnum.Hour, TopNValue.UniqueSessions, body, cancellationToken);
    }

    [HttpGet("/api/_stats/top-operatingsystems")]
    public async Task<IActionResult> TopOperatingSystems([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        return await TopN("os_name", body.Granularity ?? GranularityEnum.Hour, TopNValue.UniqueSessions, body, cancellationToken);
    }

    [HttpGet("/api/_stats/top-regions")]
    public async Task<IActionResult> TopRegions([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        return await TopN("region_name", body.Granularity ?? GranularityEnum.Hour, TopNValue.UniqueSessions, body, cancellationToken);
    }

    [HttpGet("/api/_stats/top-events")]
    public async Task<IActionResult> TopEvents([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        return await TopN("event_name", body.Granularity ?? GranularityEnum.Hour, TopNValue.TotalEvents, body, cancellationToken);
    }

    [HttpGet("/api/_stats/top-appversions")]
    public async Task<IActionResult> TopAppVersions([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        return await TopN("app_version", body.Granularity ?? GranularityEnum.Hour, TopNValue.UniqueSessions, body, cancellationToken);
    }

    [HttpGet("/api/_stats/top-appbuildnumbers")]
    public async Task<IActionResult> TopAppBuildNumbers([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        return await TopN("app_build_number", body.Granularity ?? GranularityEnum.Hour, TopNValue.UniqueSessions, body, cancellationToken);
    }

    [HttpGet("/api/_stats/metrics")]
    public async Task<IActionResult> KeyMetrics([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var currentQuery = body.Parse();
        var current = GetKeyMetrics(currentQuery, cancellationToken);

        if (!currentQuery.DateFrom.HasValue || !currentQuery.DateTo.HasValue) {
            return Ok(new KeyMetrics(await current));
        }
        
        var previousQuery = currentQuery.CloneToPreviousInterval();
        var previous = await GetKeyMetrics(previousQuery, cancellationToken);
        return Ok(new KeyMetrics(await current, previous));
    }

    [HttpGet("/api/_stats/periodic")]
    public async Task<IActionResult> PeriodicStats([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();
        var rows = await _queryClient.NamedQueryAsync<PeriodicStatsRow>("key_metrics_periodic__v2", new {
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            granularity = (query.Granularity ?? GranularityEnum.Hour).ToString(),
            app_id = query.AppId,
            event_name = query.EventName,
            os_name = query.OsName,
            app_version = query.AppVersion,
            country_code = query.CountryCode,
            device_model = query.DeviceModel,
            local_time = query.LocalTimeArg,
        }, cancellationToken);

        return Ok(rows);
    }

    [HttpGet("/api/_stats/top-props")]
    public async Task<IActionResult> EventProps([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();
        var rows = await _queryClient.NamedQueryAsync<EventPropsItem>("top_props__v2", new {
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            granularity = (body.Granularity ?? GranularityEnum.Hour).ToString(),
            app_id = query.AppId,
            event_name = query.EventName,
            os_name = query.OsName,
            app_version = query.AppVersion,
            country_code = query.CountryCode,
            device_model = query.DeviceModel,
            local_time = query.LocalTimeArg,
        }, cancellationToken);

        return Ok(rows);
    }

    // The event property charts query the props JSON directly, which the Tinybird pipes don't support
    [HttpGet("/api/_stats/event-prop-keys")]
    public async Task<IActionResult> EventPropKeys([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();
        if (string.IsNullOrEmpty(query.EventName))
            return BadRequest("eventName is required.");

        if (_queryClient is TinybirdQueryClient)
            return Ok(Array.Empty<EventPropKey>());

        var rows = await _queryClient.NamedQueryAsync<EventPropKey>("event_prop_keys__v1", new {
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            app_id = query.AppId,
            event_name = query.EventName,
            local_time = query.LocalTimeArg,
        }, cancellationToken);

        return Ok(rows);
    }

    [HttpGet("/api/_stats/event-prop-numeric")]
    public async Task<IActionResult> EventPropNumeric([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();
        if (string.IsNullOrEmpty(query.EventName) || string.IsNullOrEmpty(query.PropKey))
            return BadRequest("eventName and propKey are required.");

        if (_queryClient is TinybirdQueryClient)
            return Ok(Array.Empty<EventPropNumericRow>());

        var rows = await _queryClient.NamedQueryAsync<EventPropNumericRow>("event_prop_numeric_periodic__v1", new {
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            granularity = (query.Granularity ?? GranularityEnum.Hour).ToString(),
            app_id = query.AppId,
            event_name = query.EventName,
            prop_key = query.PropKey,
            local_time = query.LocalTimeArg,
        }, cancellationToken);

        return Ok(rows);
    }

    [HttpGet("/api/_stats/event-prop-distribution")]
    public async Task<IActionResult> EventPropDistributionStats([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();
        if (string.IsNullOrEmpty(query.EventName) || string.IsNullOrEmpty(query.PropKey))
            return BadRequest("eventName and propKey are required.");

        if (_queryClient is TinybirdQueryClient)
            return Ok(new EventPropDistribution());

        var rows = await _queryClient.NamedQueryAsync<EventPropValueRow>("event_prop_distribution__v1", new {
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            app_id = query.AppId,
            event_name = query.EventName,
            prop_key = query.PropKey,
            local_time = query.LocalTimeArg,
        }, cancellationToken);

        return Ok(EventPropDistribution.FromRows(rows));
    }

    [HttpGet("/api/_stats/event-prop-distribution-periodic")]
    public async Task<IActionResult> EventPropDistributionPeriodic([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();
        if (string.IsNullOrEmpty(query.EventName) || string.IsNullOrEmpty(query.PropKey))
            return BadRequest("eventName and propKey are required.");

        if (_queryClient is TinybirdQueryClient)
            return Ok(new EventPropDistributionSeries());

        var rows = await _queryClient.NamedQueryAsync<EventPropPeriodicValueRow>("event_prop_distribution_periodic__v1", new {
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            granularity = (query.Granularity ?? GranularityEnum.Hour).ToString(),
            app_id = query.AppId,
            event_name = query.EventName,
            prop_key = query.PropKey,
            local_time = query.LocalTimeArg,
        }, cancellationToken);

        return Ok(EventPropDistributionSeries.FromRows(rows));
    }

    // Local play time and the missing offset count need the timezone_offset column, which the Tinybird pipes don't have
    [HttpGet("/api/_stats/local-play-time")]
    public async Task<IActionResult> LocalPlayTime([FromQuery] LocalPlayTimeQueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();
        if (_queryClient is TinybirdQueryClient)
            return Ok(Array.Empty<LocalPlayTimeRow>());

        var rows = await _queryClient.NamedQueryAsync<LocalPlayTimeRow>("local_play_time__v1", new {
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            app_id = query.AppId,
            event_name = query.EventName,
            os_name = query.OsName,
            app_version = query.AppVersion,
            country_code = query.CountryCode,
            device_model = query.DeviceModel,
            metric = body.Metric.ToString(),
            viewer_tz = ValidTimeZoneOrUtc(body.TimeZone),
            local_time = query.LocalTimeArg,
        }, cancellationToken);

        return Ok(rows);
    }

    [HttpGet("/api/_stats/missing-timezone-offset")]
    public async Task<IActionResult> MissingTimezoneOffsetCount([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();
        if (_queryClient is TinybirdQueryClient)
            return Ok(new MissingTimezoneOffset());

        var row = await _queryClient.NamedQuerySingleAsync<MissingTimezoneOffset>("missing_timezone_offset__v1", new {
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            app_id = query.AppId,
            event_name = query.EventName,
            os_name = query.OsName,
            app_version = query.AppVersion,
            country_code = query.CountryCode,
            device_model = query.DeviceModel,
        }, cancellationToken);

        return Ok(row);
    }

    // Only IANA names this server knows are passed on to ClickHouse, anything else falls back to UTC
    private static string ValidTimeZoneOrUtc(string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone) || timeZone.Length > 64)
            return "UTC";

        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var tz) && tz.HasIanaId ? tz.Id : "UTC";
    }

    [HttpGet("/api/_stats/live-geo")]
    public async Task<IActionResult> LiveGeo([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();

        var rows = await _queryClient.NamedQueryAsync<LiveGeoDataPoint>("live_geo__v1", new {
            app_id = query.AppId,
        }, cancellationToken);

        foreach (var row in rows)
        {
            var (lat, lng) = _geodb.GetLatLng(row.CountryCode, row.RegionName);
            row.Latitude = lat;
            row.Longitude = lng;
        }

        return Ok(rows);
    }

    [HttpGet("/api/_stats/live-sessions")]
    public async Task<IActionResult> LiveSessions([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();

        var rows = await _queryClient.NamedQueryAsync<LiveRecentSession>("live_sessions__v1", new {
            app_id = query.AppId,
        }, cancellationToken);

        return Ok(rows);
    }

    [HttpGet("/api/_stats/live-session-details")]
    public async Task<IActionResult> LiveSessionDetails([FromQuery] QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();

        var row = await _queryClient.NamedQuerySingleAsync<SessionTimeline>("live_session_details__v1", new {
            app_id = query.AppId,
            session_id = query.SessionId,
        }, cancellationToken);

        // View events live in their own table, which only exists on ClickHouse
        if (_queryClient is not TinybirdQueryClient)
        {
            var views = (await _queryClient.NamedQueryAsync<SessionViewEvent>("session_view_events__v1", new {
                app_id = query.AppId,
                session_id = query.SessionId,
            }, cancellationToken)).ToArray();

            row.ViewsType = views.Select(v => v.ViewType).ToArray();
            row.ViewsName = views.Select(v => v.ViewName).ToArray();
            row.ViewsTimestamp = views.Select(v => v.Timestamp).ToArray();
        }

        return Ok(row);
    }

    [HttpGet("/api/_stats/historical-sessions")]
    public async Task<IActionResult> HistoricalSessions([FromQuery] QueryParams body, [FromQuery] int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var query = body.Parse();

        if (!PersistentUserId.TryNormalize(query.PersistentUserId, out var persistentUserId))
            return BadRequest(PersistentUserId.InvalidMessage);

        if (pageSize < 1 || pageSize > 50)
            return BadRequest("pageSize must be between 1 and 50.");

        var row = await _queryClient.NamedQueryAsync<LiveRecentSession>("historical_sessions__v1", new
        {
            app_id = query.AppId,
            session_id = query.SessionId,
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            event_name = query.EventName,
            os_name = query.OsName,
            app_version = query.AppVersion,
            country_code = query.CountryCode,
            persistent_user_id = persistentUserId,
            limit = pageSize,
        }, cancellationToken);

        return Ok(row);
    }

    [HttpGet("/api/_stats/retention")]
    public async Task<IActionResult> Retention([FromQuery] RetentionQueryParams body, CancellationToken cancellationToken)
    {
        if (!body.StartDate.HasValue || !body.EndDate.HasValue)
            return BadRequest("startDate and endDate are required.");

        var dateFrom = ToUtcDate(body.StartDate.Value);
        var dateTo = ToUtcDate(body.EndDate.Value);
        if (dateFrom > dateTo)
            return BadRequest("startDate must be before endDate.");

        var (defaultPeriods, maxPeriods) = body.Granularity switch
        {
            CohortGranularity.Week => (12, 52),
            CohortGranularity.Month => (12, 24),
            _ => (30, 90),
        };
        var periods = body.Periods ?? defaultPeriods;
        if (periods < 1 || periods > maxPeriods)
            return BadRequest($"periods must be between 1 and {maxPeriods}.");

        var result = new RetentionCohorts { Granularity = body.Granularity.ToString(), Periods = periods };

        // Retention needs the persistent user ID, which the Tinybird pipes don't have
        if (_queryClient is TinybirdQueryClient)
            return Ok(result);

        var appId = new QueryParams { AppId = body.AppId, BuildMode = body.BuildMode }.Parse().AppId;
        var rows = await _queryClient.NamedQueryAsync<RetentionRow>("retention_cohorts__v1", new
        {
            app_id = appId,
            date_from = dateFrom.ToString("yyyy-MM-dd"),
            date_to = dateTo.ToString("yyyy-MM-dd"),
            granularity = body.Granularity.ToString(),
            periods,
        }, cancellationToken);

        var today = DateTime.UtcNow.Date;
        result.Cohorts = rows
            .GroupBy(r => r.CohortDate.Date)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var users = g.ToDictionary(r => (int)r.PeriodNumber, r => (int)r.Users);
                return new RetentionCohort
                {
                    CohortDate = g.Key,
                    Size = users.GetValueOrDefault(0),
                    Retained = Enumerable.Range(0, periods + 1)
                        .Select(n => AddPeriods(g.Key, body.Granularity, n) <= today ? users.GetValueOrDefault(n) : (int?)null)
                        .ToArray(),
                };
            })
            .ToArray();

        return Ok(result);
    }

    private static DateTime ToUtcDate(DateTime value)
        => (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value).Date;

    private static DateTime AddPeriods(DateTime cohort, CohortGranularity granularity, int count) => granularity switch
    {
        CohortGranularity.Week => cohort.AddDays(7 * count),
        CohortGranularity.Month => cohort.AddMonths(count),
        _ => cohort.AddDays(count),
    };

    private async Task<KeyMetricsRow> GetKeyMetrics(QueryArgs args, CancellationToken cancellationToken)
    {
        return await _queryClient.NamedQuerySingleAsync<KeyMetricsRow>("key_metrics__v2", new {
            date_from = args.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = args.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            granularity = (args.Granularity ?? GranularityEnum.Hour).ToString(),
            app_id = args.AppId,
            event_name = args.EventName,
            os_name = args.OsName,
            app_version = args.AppVersion,
            country_code = args.CountryCode,
            device_model = args.DeviceModel,
            local_time = args.LocalTimeArg,
        }, cancellationToken);
    }

    private async Task<IActionResult> TopN(string nameColumn, GranularityEnum granularity, TopNValue valueColumn, QueryParams body, CancellationToken cancellationToken)
    {
        var query = body.Parse();
        var rows = await _queryClient.NamedQueryAsync<TopNItem>("top_n__v2", new {
            name_column = nameColumn,
            granularity = granularity.ToString(),
            value_column = valueColumn.ToString(),
            date_from = query.DateFrom?.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to = query.DateTo?.ToString("yyyy-MM-dd HH:mm:ss"),
            app_id = query.AppId,
            event_name = query.EventName,
            os_name = query.OsName,
            app_version = query.AppVersion,
            country_code = query.CountryCode,
            device_model = query.DeviceModel,
            local_time = query.LocalTimeArg,
        }, cancellationToken);

        return Ok(rows);
    }
}
