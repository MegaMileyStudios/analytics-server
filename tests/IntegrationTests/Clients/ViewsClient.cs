namespace Aptabase.IntegrationTests.Clients;

public class ViewsClient
{
    private static readonly Random _random = new();
    private readonly HttpClient _client;
    private readonly string _appKey;
    private readonly string _ipAddress;

    public string SessionId { get; set; } = IngestionClient.NewSessionId();
    public bool IsDebug { get; set; }
    public string OsName { get; set; } = "Windows";
    public string AppVersion { get; set; } = "1.0.0";

    public ViewsClient(HttpClient client, string appKey)
    {
        _client = client;
        _appKey = appKey;
        // A random IP per client avoids the per-IP rate limiter
        _ipAddress = $"{_random.Next(1, 255)}.{_random.Next(0, 255)}.{_random.Next(0, 255)}.{_random.Next(0, 255)}";
    }

    public async Task<HttpResponseMessage> PostViews(IEnumerable<(DateTime Timestamp, string ViewType, string ViewName)> views)
    {
        var body = JsonContent.Create(views.Select(v => NewView(v.Timestamp, v.ViewType, v.ViewName)));
        return await Post("/api/v0/views", body);
    }

    public async Task<HttpResponseMessage> PostView(DateTime timestamp, string viewType, string viewName)
    {
        var body = JsonContent.Create(NewView(timestamp, viewType, viewName));
        return await Post("/api/v0/view", body);
    }

    // Regular events of the same session, so the session end can come from the events table
    public async Task<HttpResponseMessage> PostEvents(IEnumerable<(DateTime Timestamp, string EventName)> events)
    {
        var body = JsonContent.Create(events.Select(e => new
        {
            eventName = e.EventName,
            timestamp = e.Timestamp.ToString("o"),
            sessionId = SessionId,
            systemProps = SystemProps(),
        }));
        return await Post("/api/v0/events", body);
    }

    private async Task<HttpResponseMessage> Post(string path, HttpContent body)
    {
        body.Headers.Add("App-Key", _appKey);
        body.Headers.Add("CloudFront-Viewer-Address", _ipAddress);
        return await _client.PostAsync(path, body);
    }

    private object NewView(DateTime timestamp, string viewType, string viewName) => new
    {
        viewType,
        viewName,
        timestamp = timestamp.ToString("o"),
        sessionId = SessionId,
        systemProps = SystemProps(),
    };

    private object SystemProps() => new
    {
        isDebug = IsDebug,
        osName = OsName,
        osVersion = "11",
        appVersion = AppVersion,
        sdkVersion = "aptabase-unity@0.0.0",
    };
}
