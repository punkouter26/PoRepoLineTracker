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
    public void GetProgress_NonExistentRepository_ReturnsNull()
    {
        var result = _sut.GetProgress(RepositoryId.New());
        result.Should().BeNull();
    }

    [Fact]
    public void ReportStep_NewRepository_CreatesProgressEntry()
    {
        var repoId = RepositoryId.New();

        _sut.ReportStep(repoId, 1, "Cloning", "Cloning repository...");

        var progress = _sut.GetProgress(repoId);
        progress.Should().NotBeNull();
        progress!.RepositoryId.Should().Be(repoId);
        progress.StepIndex.Should().Be(1);
        progress.StepName.Should().Be("Cloning");
        progress.StepDescription.Should().Be("Cloning repository...");
        progress.IsRunning.Should().BeTrue();
        progress.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ReportStep_ExistingRepository_UpdatesEntry()
    {
        var repoId = RepositoryId.New();
        _sut.ReportStep(repoId, 1, "Cloning", "Cloning...");

        _sut.ReportStep(repoId, 2, "Analyzing", "Processing commits...");

        var progress = _sut.GetProgress(repoId);
        progress!.StepIndex.Should().Be(2);
        progress.StepName.Should().Be("Analyzing");
        progress.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void ReportCommitsFound_SetsTotalAndResetsProcessed()
    {
        var repoId = RepositoryId.New();
        _sut.ReportStep(repoId, 1, "Cloning", "Cloning...");

        _sut.ReportCommitsFound(repoId, 42);

        var progress = _sut.GetProgress(repoId);
        progress!.CommitsTotal.Should().Be(42);
        progress.CommitsProcessed.Should().Be(0);
    }

    [Fact]
    public void Report_NonExistentRepository_AreNoOps()
    {
        // Reporting progress/commits/completion for an unknown repo must not throw and must
        // not create an entry. (ReportError is intentionally different — it creates one.)
        var repoId = RepositoryId.New();

        _sut.ReportCommitsFound(repoId, 10);
        _sut.GetProgress(repoId).Should().BeNull();

        _sut.ReportCommitProgress(repoId, 5, 10);
        _sut.GetProgress(repoId).Should().BeNull();

        _sut.ReportComplete(repoId);
        _sut.GetProgress(repoId).Should().BeNull();
    }

    [Fact]
    public void ReportCommitProgress_UpdatesProcessedCount()
    {
        var repoId = RepositoryId.New();
        _sut.ReportStep(repoId, 1, "Test", "Test");
        _sut.ReportCommitsFound(repoId, 100);

        _sut.ReportCommitProgress(repoId, 50, 100);

        var progress = _sut.GetProgress(repoId);
        progress!.CommitsProcessed.Should().Be(50);
        progress.CommitsTotal.Should().Be(100);
    }

    [Fact]
    public void ReportComplete_MarksAsNotRunning()
    {
        var repoId = RepositoryId.New();
        _sut.ReportStep(repoId, 1, "Cloning", "Cloning...");
        _sut.ReportCommitsFound(repoId, 10);
        _sut.ReportCommitProgress(repoId, 10, 10);

        _sut.ReportComplete(repoId);

        var progress = _sut.GetProgress(repoId);
        progress!.IsRunning.Should().BeFalse();
        progress.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void ReportError_SetsErrorMessageAndStopsRunning()
    {
        var repoId = RepositoryId.New();
        _sut.ReportStep(repoId, 1, "Cloning", "Cloning...");

        _sut.ReportError(repoId, "Connection timeout");

        var progress = _sut.GetProgress(repoId);
        progress!.IsRunning.Should().BeFalse();
        progress.ErrorMessage.Should().Be("Connection timeout");
    }

    [Fact]
    public void ReportError_NonExistentRepository_CreatesEntry()
    {
        var repoId = RepositoryId.New();

        _sut.ReportError(repoId, "Something went wrong");

        var progress = _sut.GetProgress(repoId);
        progress.Should().NotBeNull();
        progress!.ErrorMessage.Should().Be("Something went wrong");
        progress.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void FullLifecycle_ReportsCorrectStateTransitions()
    {
        var repoId = RepositoryId.New();

        // Step 1: Start
        _sut.ReportStep(repoId, 1, "Cloning", "Cloning repository...");
        var p1 = _sut.GetProgress(repoId);
        p1!.IsRunning.Should().BeTrue();
        p1.StepName.Should().Be("Cloning");

        // Step 2: Commits found
        _sut.ReportCommitsFound(repoId, 25);
        var p2 = _sut.GetProgress(repoId);
        p2!.CommitsTotal.Should().Be(25);
        p2.CommitsProcessed.Should().Be(0);

        // Step 3: Processing
        _sut.ReportStep(repoId, 2, "Analyzing", "Processing commits...");
        _sut.ReportCommitProgress(repoId, 13, 25);
        var p3 = _sut.GetProgress(repoId);
        p3!.StepName.Should().Be("Analyzing");
        p3.CommitsProcessed.Should().Be(13);

        // Step 4: Complete
        _sut.ReportComplete(repoId);
        var p4 = _sut.GetProgress(repoId);
        p4!.IsRunning.Should().BeFalse();
        p4.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void MultipleRepositories_TrackedIndependently()
    {
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
    public void BeginJob_RecordsOwnerLabelsAndMarksRunning()
    {
        var repoId = RepositoryId.New();

        _sut.BeginJob(repoId, UserId.New(), "octocat", "hello-world");

        var progress = _sut.GetProgress(repoId);
        progress.Should().NotBeNull();
        progress!.Owner.Should().Be("octocat");
        progress.Name.Should().Be("hello-world");
        progress.IsRunning.Should().BeTrue();
        progress.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void BeginJob_AfterFailure_ClearsThePreviousError()
    {
        // The UI renders a non-empty ErrorMessage as "Failed" regardless of IsRunning, so a
        // re-analysis that inherited the last run's error would show as failed the instant it
        // started. This is why BeginJob replaces the entry rather than mutating it.
        var repoId = RepositoryId.New();
        _sut.ReportStep(repoId, 1, "Cloning", "Cloning...");
        _sut.ReportError(repoId, "Connection timeout");

        _sut.BeginJob(repoId, UserId.New(), "octocat", "hello-world");

        var progress = _sut.GetProgress(repoId);
        progress!.ErrorMessage.Should().BeNull();
        progress.IsRunning.Should().BeTrue();
        progress.CommitsProcessed.Should().Be(0);
    }

    [Fact]
    public void BeginJob_ReportsANonNegativeProgressPercent()
    {
        // Regression: BeginJob opens a job at StepIndex 0, and the step-based branch of
        // ProgressPercent is (StepIndex - 1) / StepTotal — which produced -25 on the very first
        // frame. Unreachable while progress was only polled; observed live once it was pushed.
        var repoId = RepositoryId.New();

        _sut.BeginJob(repoId, UserId.New(), "octocat", "hello-world");

        _sut.GetProgress(repoId)!.ProgressPercent.Should().BeInRange(0, 100);
    }

    [Fact]
    public void Publish_WithoutBeginJob_SendsNothing()
    {
        // A job whose owner was never recorded has no address to send to. Broadcasting it anyway
        // would put one user's repository names in front of every connected user.
        var repoId = RepositoryId.New();

        _sut.ReportStep(repoId, 1, "Cloning", "Cloning...");
        _sut.ReportError(repoId, "boom");

        _ = _hubContext.DidNotReceive().Clients;
    }

    [Fact]
    public void LastUpdatedUtc_IsSetOnEveryMutation()
    {
        var repoId = RepositoryId.New();
        var before = DateTime.UtcNow;

        _sut.ReportStep(repoId, 1, "Test", "Test");

        var progress = _sut.GetProgress(repoId);
        progress!.LastUpdatedUtc.Should().BeOnOrAfter(before);
        progress.LastUpdatedUtc.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(1));
    }
}
