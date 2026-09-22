using System.Text.RegularExpressions;

namespace Aptabase.IntegrationTests.Clients;

public class MailCatcherMessage
{
    public long Id { get; set; }
    public string[] Recipients { get; set; } = Array.Empty<string>();
}

public static class MailCatcher
{
    public static async Task<string> GetLinkSentTo(string email)
    {
        var mailcatcherUrl = Environment.GetEnvironmentVariable("MAILCATCHER_URL") ?? "http://localhost:1080";
        using var client = new HttpClient();

        MailCatcherMessage? message = null;
        for (var attempt = 0; attempt < 25 && message == null; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(200);

            var messages = await client.GetFromJsonAsync<IEnumerable<MailCatcherMessage>>($"{mailcatcherUrl}/messages");
            message = messages?.OrderByDescending(x => x.Id)
                               .FirstOrDefault(m => m.Recipients.Contains($"<{email}>"));
        }

        if (message == null)
        {
            throw new Exception($"No message found for {email}.");
        }

        var html = await client.GetStringAsync($"{mailcatcherUrl}/messages/{message.Id}.html");
        var link = Regex.Match(html, @"<a\s+(?:[^>]*?\s+)?href=([""'])(.*?)\1").Groups[2].Value;
        var baseUrl = Environment.GetEnvironmentVariable("BASE_URL") ?? "";
        return link.Replace(baseUrl, "");
    }
}