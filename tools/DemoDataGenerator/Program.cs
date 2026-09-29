using Aptabase.Tools.DemoDataGenerator;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

// Usage: dotnet run -- <app-key> [--sessions 150] [--users 40] [--days 6.5] [--url https://localhost:5251]
if (args.Length == 0 || args[0].StartsWith("--"))
{
    Console.WriteLine("Usage: dotnet run -- <app-key> [--sessions 150] [--users 40] [--days 6.5] [--url https://localhost:5251]");
    return 1;
}

string Option(string name, string fallback)
{
    var index = Array.IndexOf(args, $"--{name}");
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
}

var random = new Random();
var appKey = args[0];
var sessions = int.Parse(Option("sessions", "150"));
var userCount = int.Parse(Option("users", "40"));
var baseUrl = new Uri(Option("url", "https://localhost:5251"));
var maxEventsPerSession = 8;

// Events older than a day are only accepted with a persistent user ID, and never older than 7 days
var days = double.Parse(Option("days", "6.5"), System.Globalization.CultureInfo.InvariantCulture);
if (days <= 0 || days >= 7)
{
    Console.WriteLine("--days must be above 0 and below 7, older events are rejected by the API.");
    return 1;
}
var maxAnonymousAge = TimeSpan.FromHours(23);

// The local dev certificate isn't trusted by .NET on every machine
var handler = new HttpClientHandler();
if (baseUrl.IsLoopback)
    handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

var httpClient = new HttpClient(handler) { BaseAddress = baseUrl };
httpClient.DefaultRequestHeaders.Add("App-Key", appKey);

// Each demo user keeps the same device, location and locale across their sessions
var users = Enumerable.Range(0, userCount).Select(i => new
{
    Id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"aptabase-demo-user-{i}"))).ToLowerInvariant(),
    Device = DemoDataSource.GetDeviceInfo(),
    Locale = DemoDataSource.GetLocale(),
    IpAddress = DemoDataSource.GetRandomIpAddress(),
}).ToArray();

var sentEvents = 0;
var failedSessions = 0;

for (var i = 0; i < sessions; i++)
{
    var startedAt = DateTime.UtcNow.AddMinutes(-random.Next(5, Convert.ToInt32(TimeSpan.FromDays(days).TotalMinutes)));
    var (sessionId, timestamps) = DemoDataSource.NewSession(startedAt, maxEventsPerSession);
    var user = users[random.Next(users.Length)];
    var (osName, osVersion, engineName, engineVersion) = user.Device;
    var (appVersion, appBuildNumber, sdkVersion) = DemoDataSource.GetAppInfo();

    // Some recent sessions come from older SDKs that don't send a persistent user ID
    var anonymous = DateTime.UtcNow - startedAt < maxAnonymousAge && random.NextDouble() < 0.25;
    var systemProps = new Dictionary<string, object?>
    {
        ["isDebug"] = false,
        ["osName"] = osName,
        ["osVersion"] = osVersion,
        ["locale"] = user.Locale,
        ["engineName"] = engineName,
        ["engineVersion"] = engineVersion,
        ["appVersion"] = appVersion,
        ["appBuildNumber"] = appBuildNumber,
        ["sdkVersion"] = sdkVersion,
        ["userId"] = anonymous ? null : user.Id,
    };

    var events = new List<Dictionary<string, object?>>();
    foreach (var timestamp in timestamps)
    {
        var (eventName, props) = DemoDataSource.NewEvent();
        events.Add(new Dictionary<string, object?>
        {
            ["timestamp"] = timestamp.ToString("o"),
            ["sessionId"] = sessionId,
            ["eventName"] = eventName,
            ["systemProps"] = systemProps,
            ["props"] = props,
        });
    }

    // Most sessions end cleanly, the rest time out
    if (random.NextDouble() < 0.8)
    {
        events.Add(new Dictionary<string, object?>
        {
            ["timestamp"] = timestamps[^1].AddSeconds(random.Next(2, 20)).ToString("o"),
            ["sessionId"] = sessionId,
            ["eventName"] = "app_shutdown",
            ["systemProps"] = systemProps,
            ["props"] = new Dictionary<string, object>(),
        });
    }

    var body = JsonContent.Create(events);
    body.Headers.Add("CloudFront-Viewer-Address", user.IpAddress);

    var response = await httpClient.PostAsync("/api/v0/events", body);
    if ((int)response.StatusCode == 429)
    {
        // Ingestion is rate limited per IP, wait for the window to pass and retry once
        await Task.Delay(1000);
        body = JsonContent.Create(events);
        body.Headers.Add("CloudFront-Viewer-Address", user.IpAddress);
        response = await httpClient.PostAsync("/api/v0/events", body);
    }

    if (!response.IsSuccessStatusCode)
    {
        failedSessions++;
        Console.WriteLine($"[{(int)response.StatusCode}] {await response.Content.ReadAsStringAsync()}");
        continue;
    }

    sentEvents += events.Count;
}

Console.WriteLine($"Sent {sentEvents} events in {sessions - failedSessions} sessions from {userCount} users over the last {days} days ({failedSessions} sessions failed).");
Console.WriteLine("Note: the API drops invalid events without an error, check the dashboard to confirm they arrived.");
return failedSessions == 0 ? 0 : 1;
