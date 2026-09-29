using Aptabase.Features.GeoIP;
using Aptabase.Features.Ingestion;
using Aptabase.Features.ViewTracking.Buffer;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Aptabase.Features.ViewTracking;

[ApiController]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ViewsController : Controller
{
    public const int MaxViewsPerRequest = 25;

    private readonly ILogger _logger;
    private readonly IIngestionCache _cache;
    private readonly IViewBuffer _buffer;
    private readonly GeoIPClient _geoIP;

    public ViewsController(IIngestionCache cache,
                           IViewBuffer buffer,
                           GeoIPClient geoIP,
                           ILogger<ViewsController> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _geoIP = geoIP ?? throw new ArgumentNullException(nameof(geoIP));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("/api/v0/view")]
    [EnableCors("AllowAny")]
    [EnableRateLimiting("EventIngestion")]
    public async Task<IActionResult> Single(
        [FromHeader(Name = "App-Key")] string? appKey,
        [FromBody] ViewBody body,
        CancellationToken cancellationToken
    )
    {
        appKey = appKey?.ToUpper() ?? "";

        var (valid, validationMessage) = body.IsValid(_logger);
        if (!valid)
        {
            _logger.LogWarning("Dropping view from {AppKey} because: {ValidationMessage}.", appKey, validationMessage);
            return BadRequest(validationMessage);
        }

        return await Store(appKey, [body], cancellationToken);
    }

    [HttpOptions("/api/v0/view")]
    [EnableCors("AllowAny")]
    public IActionResult OptionsSingle()
    {
        return Ok();
    }

    [HttpPost("/api/v0/views")]
    [EnableCors("AllowAny")]
    [EnableRateLimiting("EventIngestion")]
    public async Task<IActionResult> Multiple(
        [FromHeader(Name = "App-Key")] string? appKey,
        [FromBody] ViewBody[] views,
        CancellationToken cancellationToken
    )
    {
        appKey = appKey?.ToUpper() ?? "";

        if (views.Length > MaxViewsPerRequest)
            return BadRequest($"Too many views ({views.Length}) in a single request. Maximum is {MaxViewsPerRequest}.");

        var validViews = views.Where(v =>
        {
            var (valid, validationMessage) = v.IsValid(_logger);
            if (!valid)
                _logger.LogWarning("Dropping view from {AppKey}. {ValidationMessage}", appKey, validationMessage);
            return valid;
        }).ToArray();

        if (validViews.Length == 0)
            return Ok(new { });

        return await Store(appKey, validViews, cancellationToken);
    }

    [HttpOptions("/api/v0/views")]
    [EnableCors("AllowAny")]
    public IActionResult OptionsMultiple()
    {
        return Ok();
    }

    private async Task<IActionResult> Store(string appKey, ViewBody[] views, CancellationToken cancellationToken)
    {
        var app = await _cache.FindByAppKey(appKey, cancellationToken);
        if (string.IsNullOrEmpty(app.Id))
        {
            _logger.LogWarning("Appplication not found with given app key: {AppKey}", appKey);
            return NotFound($"Appplication not found with given app key: {appKey}");
        }

        if (app.IsLocked)
            return BadRequest($"Owner account is locked.");

        var location = _geoIP.GetClientLocation(HttpContext);
        _buffer.AddRange(views.Select(v => NewTrackingView(app.Id, location.CountryCode, location.RegionName, v)));

        return Ok(new { });
    }

    private static TrackingView NewTrackingView(string appId, string countryCode, string regionName, ViewBody body)
    {
        return new TrackingView
        {
            AppId = appId,
            Timestamp = body.Timestamp.ToUniversalTime(),
            SessionId = body.SessionId,
            PersistentUserId = body.SystemProps.UserId,
            ViewType = body.ViewType,
            ViewName = body.ViewName,
            OSName = body.SystemProps.OSName ?? "",
            OSVersion = body.SystemProps.OSVersion ?? "",
            DeviceModel = body.SystemProps.DeviceModel ?? "",
            Locale = body.SystemProps.Locale ?? "",
            AppVersion = body.SystemProps.AppVersion ?? "",
            AppBuildNumber = body.SystemProps.AppBuildNumber ?? "",
            EngineName = body.SystemProps.EngineName ?? "",
            EngineVersion = body.SystemProps.EngineVersion ?? "",
            SdkVersion = body.SystemProps.SdkVersion ?? "",
            CountryCode = countryCode,
            RegionName = regionName,
            IsDebug = body.SystemProps.IsDebug,
        };
    }
}
