using System.Net;
using AwesomeAssertions;
using Aptabase.Features.Apps;
using Aptabase.Features.Stats;

namespace Aptabase.IntegrationTests.Clients;

public class AccountClient
{
    private readonly HttpClient _client;

    public AccountClient(HttpClient client)
    {
        _client = client;
    }

    public async Task CreateAccount(string name, string email)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var response = await _client.PostAsJsonAsync("/api/_auth/register", new { name, email });
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            try
            {
                var confirmUrl = await MailCatcher.GetLinkSentTo(email);
                response = await _client.GetAsync(confirmUrl);
                response.StatusCode.Should().Be(HttpStatusCode.Redirect);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw lastError!;
    }

    public async Task<Application> CreateApp(string name)
    {
        var response = await _client.PostAsJsonAsync("/api/_apps", new { name });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var apps = await _client.GetFromJsonAsync<Application[]>("/api/_apps");
        var app = apps?.FirstOrDefault(x => x.Name == name);
        return app ?? throw new Exception("No app found");
    }

    public async Task<HttpResponseMessage> GetKeyMetrics(string appId, string period)
    {
        return await _client.GetAsync($"/api/_stats/metrics?buildMode=release&period={period}&appId={appId}");
    }

    public async Task<long> CountEvents(string appId, string period)
    {
        var metrics = await _client.GetFromJsonAsync<KeyMetrics>($"/api/_stats/metrics?buildMode=release&period={period}&appId={appId}");
        return metrics?.Current.Events ?? 0;
    }

    public async Task<HttpResponseMessage> GetMe()
    {
        return await _client.GetAsync("/api/_auth/me");
    }

    public async Task<HttpResponseMessage> PostApp(string name)
    {
        return await _client.PostAsJsonAsync("/api/_apps", new { name });
    }

    public async Task<HttpResponseMessage> GetErrors(string appId)
    {
        return await _client.GetAsync($"/api/v0/apps/{appId}/errors?buildMode=release");
    }

    public async Task<HttpResponseMessage> GetErrorById(string appId, string errorId)
    {
        return await _client.GetAsync($"/api/v0/apps/{appId}/errors/{errorId}?buildMode=release");
    }

    public async Task<SessionTimeline?> GetSessionTimeline(string appId, object sessionId)
    {
        return await _client.GetFromJsonAsync<SessionTimeline>($"/api/_stats/live-session-details?buildMode=release&appId={appId}&sessionId={sessionId}");
    }

    public async Task<KeyMetrics?> GetKeyMetrics(string appId, DateTime startDate, DateTime endDate)
    {
        return await _client.GetFromJsonAsync<KeyMetrics>($"/api/_stats/metrics?buildMode=release&appId={appId}&startDate={startDate:o}&endDate={endDate:o}");
    }

    public async Task<PeriodicStatsRow[]?> GetPeriodicStats(string appId, DateTime startDate, DateTime endDate, string granularity)
    {
        return await _client.GetFromJsonAsync<PeriodicStatsRow[]>($"/api/_stats/periodic?buildMode=release&appId={appId}&startDate={startDate:o}&endDate={endDate:o}&granularity={granularity}");
    }

    public async Task<LiveGeoDataPoint[]?> GetLiveGeo(string appId)
    {
        return await _client.GetFromJsonAsync<LiveGeoDataPoint[]>($"/api/_stats/live-geo?buildMode=release&appId={appId}");
    }

    public async Task<HttpResponseMessage> GetHistoricalSessions(string appId, string? persistentUserId = null, int? pageSize = null)
    {
        var url = $"/api/_stats/historical-sessions?buildMode=release&appId={appId}";
        if (persistentUserId != null)
            url += $"&persistentUserId={Uri.EscapeDataString(persistentUserId)}";
        if (pageSize != null)
            url += $"&pageSize={pageSize}";

        return await _client.GetAsync(url);
    }

    public async Task<HttpResponseMessage> GetRetention(string appId, DateTime? startDate, DateTime? endDate, string granularity = "day", int? periods = null, string buildMode = "release")
    {
        var url = $"/api/_stats/retention?buildMode={buildMode}&appId={appId}&granularity={granularity}";
        if (startDate.HasValue)
            url += $"&startDate={startDate:yyyy-MM-dd}";
        if (endDate.HasValue)
            url += $"&endDate={endDate:yyyy-MM-dd}";
        if (periods.HasValue)
            url += $"&periods={periods}";

        return await _client.GetAsync(url);
    }

    public async Task<HttpResponseMessage> GetEventPropStats(string endpoint, string appId, string eventName, string? propKey, DateTime startDate, DateTime endDate, string granularity = "Day", string buildMode = "release")
    {
        var url = $"/api/_stats/{endpoint}?buildMode={buildMode}&appId={appId}&eventName={Uri.EscapeDataString(eventName)}&startDate={startDate:o}&endDate={endDate:o}&granularity={granularity}";
        if (propKey != null)
            url += $"&propKey={Uri.EscapeDataString(propKey)}";

        return await _client.GetAsync(url);
    }

    public async Task<HttpResponseMessage> GetViewStats(string kind, string appId, DateTime startDate, DateTime endDate, string buildMode = "release", string? mainView = null, string? osName = null, string? appVersion = null)
    {
        var url = $"/api/_stats/views/{kind}?buildMode={buildMode}&appId={appId}&startDate={startDate:o}&endDate={endDate:o}";
        if (mainView != null)
            url += $"&mainView={Uri.EscapeDataString(mainView)}";
        if (osName != null)
            url += $"&osName={Uri.EscapeDataString(osName)}";
        if (appVersion != null)
            url += $"&appVersion={Uri.EscapeDataString(appVersion)}";

        return await _client.GetAsync(url);
    }
}
