using Aptabase.Features.Authentication;
using Aptabase.Features.Stats;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aptabase.Features.ViewTracking;

public record ViewStatsRow
{
    public string Name { get; set; } = "";
    public ulong Visits { get; set; }
    public ulong Sessions { get; set; }
    public double TotalSeconds { get; set; }
    public double AvgSeconds { get; set; }
    public double MedianSeconds { get; set; }
}

public record SessionViewEvent
{
    public string ViewType { get; set; } = "";
    public string ViewName { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class ViewQueryParams : QueryParams
{
    public string? MainView { get; set; }
}

// View tracking reads the view_events table, which only exists on ClickHouse.
// On Tinybird every endpoint returns an empty result.
[ApiController, IsAuthenticated, HasReadAccessToApp]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[EnableRateLimiting("Stats")]
public class ViewStatsController : Controller
{
    private readonly IQueryClient _queryClient;

    public ViewStatsController(IQueryClient queryClient)
    {
        _queryClient = queryClient ?? throw new ArgumentNullException(nameof(queryClient));
    }

    [HttpGet("/api/_stats/views/main")]
    public async Task<IActionResult> MainViews([FromQuery] ViewQueryParams body, CancellationToken cancellationToken)
    {
        if (_queryClient is TinybirdQueryClient)
            return Ok(Array.Empty<ViewStatsRow>());

        var rows = await _queryClient.NamedQueryAsync<ViewStatsRow>("view_main_stats__v1", ToArgs(body), cancellationToken);
        return Ok(rows);
    }

    [HttpGet("/api/_stats/views/popups")]
    public async Task<IActionResult> Popups([FromQuery] ViewQueryParams body, CancellationToken cancellationToken)
    {
        if (_queryClient is TinybirdQueryClient)
            return Ok(Array.Empty<ViewStatsRow>());

        var rows = await _queryClient.NamedQueryAsync<ViewStatsRow>("view_popup_stats__v1", ToArgs(body), cancellationToken);
        return Ok(rows);
    }

    [HttpGet("/api/_stats/views/top-countries")]
    public Task<IActionResult> TopCountries([FromQuery] ViewQueryParams body, CancellationToken cancellationToken)
        => TopN("country_code", body, cancellationToken);

    [HttpGet("/api/_stats/views/top-operatingsystems")]
    public Task<IActionResult> TopOperatingSystems([FromQuery] ViewQueryParams body, CancellationToken cancellationToken)
        => TopN("os_name", body, cancellationToken);

    [HttpGet("/api/_stats/views/top-appversions")]
    public Task<IActionResult> TopAppVersions([FromQuery] ViewQueryParams body, CancellationToken cancellationToken)
        => TopN("app_version", body, cancellationToken);

    private async Task<IActionResult> TopN(string nameColumn, ViewQueryParams body, CancellationToken cancellationToken)
    {
        if (_queryClient is TinybirdQueryClient)
            return Ok(Array.Empty<TopNItem>());

        var args = ToArgs(body);
        var rows = await _queryClient.NamedQueryAsync<TopNItem>("view_top_n__v1", new
        {
            name_column = nameColumn,
            args.app_id,
            args.date_from,
            args.date_to,
            args.country_code,
            args.os_name,
            args.app_version,
        }, cancellationToken);

        return Ok(rows);
    }

    private static ViewQueryArgs ToArgs(ViewQueryParams body)
    {
        var query = body.Parse();

        // The queries need a range, default to the last 7 days like the errors page does
        var dateTo = query.DateTo ?? DateTime.UtcNow;
        var dateFrom = query.DateFrom ?? dateTo.AddDays(-7);

        return new ViewQueryArgs(
            app_id: query.AppId,
            date_from: dateFrom.ToString("yyyy-MM-dd HH:mm:ss"),
            date_to: dateTo.ToString("yyyy-MM-dd HH:mm:ss"),
            country_code: query.CountryCode,
            os_name: query.OsName,
            app_version: query.AppVersion,
            main_view: body.MainView
        );
    }

    // Property names are the template variables of the view_*.liquid queries
    private record ViewQueryArgs(
        string app_id,
        string date_from,
        string date_to,
        string? country_code,
        string? os_name,
        string? app_version,
        string? main_view);
}
