using FluentAssertions;
using PoRepoLineTracker.API.Hubs;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// Unit tests for <see cref="AnalysisProgressService"/>.
/// Verifies in-memory progress tracking for background analysis jobs.
/// Thread-safe singleton behavior tested via sequential operations.
///
/// <para>The hub context is no longer on this service — it lives on <see cref="AnalysisProgressReader"/>,
/// the single reader of the bounded channel. These tests assert the stored snapshot, which is
/// what <c>GetProgress</c> and the polling endpoint read; the push is now a channel write, and
/// the channel policy is pinned by <c>AnalysisHubChannelTests</c>.</para>
/// </summary>
public class AnalysisProgressServiceTests
{
    private readonly AnalysisProgressService _sut = new();

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

        // The hub push was on this service in the previous design; in the current design the
        // push goes through the bounded channel, drained by AnalysisProgressReader. Asserting
        // "no broadcast without owner" at the channel level is AnalysisHubChannelTests' job;
        // here we pin the in-memory invariant that ReportStep on an unowned repo still records
        // no owner (TryGetOwner returns false) and TryGetOwner on a begun repo returns true.
        var unownedRepoId = RepositoryId.New();
        _sut.TryGetOwner(unownedRepoId, out _).Should().BeFalse();
        _sut.ReportStep(unownedRepoId, 1, "Cloning", "Cloning...");
        _sut.TryGetOwner(unownedRepoId, out _).Should().BeFalse();
        _sut.ReportError(unownedRepoId, "boom");

        _sut.TryGetOwner(repoId, out var ownerOfBegun).Should().BeTrue();
    }
}
