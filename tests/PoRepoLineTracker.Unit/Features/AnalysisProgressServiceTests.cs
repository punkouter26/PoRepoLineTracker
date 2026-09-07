using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.API.Hubs;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// Unit tests for <see cref="AnalysisProgressService"/>.
/// Verifies in-memory progress tracking for background analysis jobs.
/// Thread-safe singleton behavior tested via sequential operations.
///
/// <para>The hub context is substituted rather than exercised. These tests assert the stored
/// snapshot, which is what <c>GetProgress</c> and the polling endpoint read; the push is a
/// side effect of the same mutation and is covered where it matters — that a job with no
/// recorded owner is never broadcast — by <see cref="Publish_WithoutBeginJob_SendsNothing"/>.</para>
/// </summary>
public class AnalysisProgressServiceTests
{
    private readonly IHubContext<AnalysisHub> _hubContext = Substitute.For<IHubContext<AnalysisHub>>();
    private readonly AnalysisProgressService _sut;

    public AnalysisProgressServiceTests()
    {
        _sut = new AnalysisProgressService(
            _hubContext,
            Substitute.For<ILogger<AnalysisProgressService>>());
    }

    [Fact]
    public void StepReportingAndLifecycle_TracksStateTransitionsCorrectly()
    {
        var repoId = RepositoryId.New();

        _sut.ReportStep(repoId, 1, "Cloning", "Cloning repository...");
        var p1 = _sut.GetProgress(repoId);
        p1.Should().NotBeNull();
        p1!.RepositoryId.Should().Be(repoId);
        p1.StepIndex.Should().Be(1);
        p1.StepName.Should().Be("Cloning");
        p1.IsRunning.Should().BeTrue();
        p1.ErrorMessage.Should().BeNull();

        _sut.ReportCommitsFound(repoId, 25);
        var p2 = _sut.GetProgress(repoId);
        p2!.CommitsTotal.Should().Be(25);
        p2.CommitsProcessed.Should().Be(0);

        _sut.ReportStep(repoId, 2, "Analyzing", "Processing commits...");
        _sut.ReportCommitProgress(repoId, 13, 25);
        var p3 = _sut.GetProgress(repoId);
        p3!.StepIndex.Should().Be(2);
        p3.StepName.Should().Be("Analyzing");
        p3.CommitsProcessed.Should().Be(13);
        p3.IsRunning.Should().BeTrue();

        _sut.ReportComplete(repoId);
        var p4 = _sut.GetProgress(repoId);
        p4!.IsRunning.Should().BeFalse();
        p4.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void NonExistentRepositories_HandledGracefully()
    {
        var repoId = RepositoryId.New();

        _sut.GetProgress(repoId).Should().BeNull();
        _sut.ReportCommitsFound(repoId, 10);
        _sut.GetProgress(repoId).Should().BeNull();
        _sut.ReportCommitProgress(repoId, 5, 10);
        _sut.GetProgress(repoId).Should().BeNull();
        _sut.ReportComplete(repoId);
        _sut.GetProgress(repoId).Should().BeNull();

        var errorRepoId = RepositoryId.New();
        _sut.ReportError(errorRepoId, "Something went wrong");
        var progress = _sut.GetProgress(errorRepoId);
        progress.Should().NotBeNull();
        progress!.ErrorMessage.Should().Be("Something went wrong");
        progress.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void ErrorReportingAndIndependentTracking()
    {
        var repoId = RepositoryId.New();
        _sut.ReportStep(repoId, 1, "Cloning", "Cloning...");
        _sut.ReportError(repoId, "Connection timeout");
        var progress = _sut.GetProgress(repoId);
        progress!.IsRunning.Should().BeFalse();
        progress.ErrorMessage.Should().Be("Connection timeout");

        var repo1 = RepositoryId.New();
        var repo2 = RepositoryId.New();
        _sut.ReportStep(repo1, 1, "Cloning", "Repo 1 cloning...");
        _sut.ReportStep(repo2, 3, "Complete", "Repo 2 done...");
        var p1 = _sut.GetProgress(repo1);
        var p2 = _sut.GetProgress(repo2);
        p1!.StepIndex.Should().Be(1);
        p1.StepName.Should().Be("Cloning");
        p2!.StepIndex.Should().Be(3);
        p2.StepName.Should().Be("Complete");
    }

    [Fact]
    public void BeginJobAndPublish_InitializesJobClearsErrorsAndGuardsBroadcast()
    {
        var repoId = RepositoryId.New();
        _sut.BeginJob(repoId, UserId.New(), "octocat", "hello-world");
        var progress = _sut.GetProgress(repoId);
        progress.Should().NotBeNull();
        progress!.Owner.Should().Be("octocat");
        progress.Name.Should().Be("hello-world");
        progress.IsRunning.Should().BeTrue();
        progress.ErrorMessage.Should().BeNull();
        progress.ProgressPercent.Should().BeInRange(0, 100);

        _sut.ReportError(repoId, "Connection timeout");
        _sut.BeginJob(repoId, UserId.New(), "octocat", "hello-world");
        var resetProgress = _sut.GetProgress(repoId);
        resetProgress!.ErrorMessage.Should().BeNull();
        resetProgress.IsRunning.Should().BeTrue();
        resetProgress.CommitsProcessed.Should().Be(0);

        var unownedRepoId = RepositoryId.New();
        _hubContext.ClearReceivedCalls();
        _sut.ReportStep(unownedRepoId, 1, "Cloning", "Cloning...");
        _sut.ReportError(unownedRepoId, "boom");
        _ = _hubContext.DidNotReceive().Clients;
    }
}
