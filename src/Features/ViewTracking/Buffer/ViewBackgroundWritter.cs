using System.Diagnostics;

namespace Aptabase.Features.ViewTracking.Buffer;

public class ViewBackgroundWritter : BackgroundService
{
    private readonly IViewBuffer _buffer;
    private readonly IViewIngestionClient _client;
    private readonly ILogger _logger;
    private readonly Stopwatch _watch = new();

    public ViewBackgroundWritter(IViewBuffer buffer, IViewIngestionClient client, ILogger<ViewBackgroundWritter> logger)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ViewBackgroundWritter is starting.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushViews();
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch { }
        }

        // We need to wait a few seconds when cancellation is requested
        // because some views may be added to the buffer after the cancellation
        // After flushing we can safely exit
        _logger.LogInformation("ViewBackgroundWritter is stopping.");
        await Task.Delay(TimeSpan.FromSeconds(2));
        await FlushViews();
        _logger.LogInformation("ViewBackgroundWritter stopped.");
    }

    public async Task FlushViews()
    {
        var views = _buffer.TakeAll();
        if (views.Length == 0) return;

        try
        {
            _watch.Restart();

            var rows = views.Select(v => new ViewRow(ref v)).ToArray();

            await _client.BulkSendViewsAsync(rows);
            _watch.Stop();
            _logger.LogInformation("Flushed {Count} views in {TimeMs}ms.", views.Length, _watch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send views. {Count} views were discarded.", views.Length);
        }
    }
}
