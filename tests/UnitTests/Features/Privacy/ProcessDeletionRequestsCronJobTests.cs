using Xunit;
using Moq;
using AwesomeAssertions;
using Aptabase.Features.Privacy;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aptabase.UnitTests.Features.Privacy;

public class ProcessDeletionRequestsCronJobTests
{
    private readonly Mock<IPrivacyQueries> _queries = new();
    private readonly Mock<IUserDataEraser> _eraser = new();

    private ProcessDeletionRequestsCronJob CreateJob()
        => new(_queries.Object, _eraser.Object, NullLogger<ProcessDeletionRequestsCronJob>.Instance);

    [Fact]
    public async Task Should_erase_pending_hashes_and_mark_them_completed()
    {
        _queries.Setup(q => q.GetPendingDeletionRequests(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DeletionRequest("r1", "hash-a"), new DeletionRequest("r2", "hash-b")]);

        var processed = await CreateJob().ProcessPendingRequests(CancellationToken.None);

        processed.Should().Be(2);
        _eraser.Verify(e => e.EraseAsync(new[] { "hash-a", "hash-b" }, It.IsAny<CancellationToken>()), Times.Once);
        _queries.Verify(q => q.MarkDeletionRequestsCompleted(new[] { "r1", "r2" }, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Should_do_nothing_without_pending_requests()
    {
        _queries.Setup(q => q.GetPendingDeletionRequests(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var processed = await CreateJob().ProcessPendingRequests(CancellationToken.None);

        processed.Should().Be(0);
        _eraser.Verify(e => e.EraseAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        _queries.Verify(q => q.MarkDeletionRequestsCompleted(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Should_keep_requests_pending_when_erasing_fails()
    {
        _queries.Setup(q => q.GetPendingDeletionRequests(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DeletionRequest("r1", "hash-a")]);
        _eraser.Setup(e => e.EraseAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ClickHouse is down"));

        var act = () => CreateJob().ProcessPendingRequests(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _queries.Verify(q => q.MarkDeletionRequestsCompleted(It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
