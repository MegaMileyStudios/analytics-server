using Sgbj.Cron;

namespace Aptabase.Features.Privacy;

public class ProcessDeletionRequestsCronJob : BackgroundService
{
    private const int BatchSize = 100;

    private readonly ILogger _logger;
    private readonly IPrivacyQueries _privacyQueries;
    private readonly IUserDataEraser _eraser;

    public ProcessDeletionRequestsCronJob(IPrivacyQueries privacyQueries, IUserDataEraser eraser, ILogger<ProcessDeletionRequestsCronJob> logger)
    {
        _privacyQueries = privacyQueries ?? throw new ArgumentNullException(nameof(privacyQueries));
        _eraser = eraser ?? throw new ArgumentNullException(nameof(eraser));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("ProcessDeletionRequestsCronJob is starting.");

                using var timer = new CronTimer("0 * * * *", TimeZoneInfo.Utc);

                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    var processed = await ProcessPendingRequests(cancellationToken);
                    if (processed > 0)
                        _logger.LogInformation("Processed {processed} deletion requests", processed);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("ProcessDeletionRequestsCronJob stopped.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ProcessDeletionRequestsCronJob crashed.");
            }
        }
    }

    // A batch is only marked completed once all of its mutations were accepted,
    // if anything fails the requests stay pending and are retried on the next run
    public async Task<int> ProcessPendingRequests(CancellationToken cancellationToken)
    {
        var total = 0;
        while (true)
        {
            var requests = await _privacyQueries.GetPendingDeletionRequests(BatchSize, cancellationToken);
            if (requests.Length == 0)
                return total;

            var hashes = requests.Select(r => r.Hash).Distinct().ToArray();
            await _eraser.EraseAsync(hashes, cancellationToken);
            await _privacyQueries.MarkDeletionRequestsCompleted(requests.Select(r => r.Id).ToArray(), cancellationToken);

            total += requests.Length;
            if (requests.Length < BatchSize)
                return total;
        }
    }
}
