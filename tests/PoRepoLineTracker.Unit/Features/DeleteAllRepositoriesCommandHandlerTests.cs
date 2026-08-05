using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The most destructive handler in the codebase: it drops every repository row for a user AND
/// deletes a directory tree recursively. It had no test.
///
/// <para>Two properties matter more than the happy path. It must delete only the configured
/// repositories directory — an integration run once pointed this at the system temp root and it
/// removed the test host's own scratch files. And local cleanup must stay best-effort: a locked
/// Git file on Windows is routine, and letting it propagate would fail a request whose storage
/// work has already succeeded, leaving the user with an error and no way to retry cleanly.</para>
///
/// <para>Each test gets its own directory under the session scratch root, so a bug in the handler
/// cannot reach anything shared.</para>
/// </summary>
public sealed class DeleteAllRepositoriesCommandHandlerTests : IDisposable
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly UserId _userId = UserId.New();
    private readonly string _sandbox;

    public DeleteAllRepositoriesCommandHandlerTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), $"PoRepoLineTracker.RemoveAllTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true); }
        catch { /* best-effort teardown; the OS reclaims temp anyway */ }
    }

    private DeleteAllRepositoriesCommandHandler HandlerFor(string? localReposPath)
    {
        var settings = new Dictionary<string, string?>();
        if (localReposPath is not null) settings[ConfigKeys.GitHub.LocalReposPath] = localReposPath;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new DeleteAllRepositoriesCommandHandler(
            _dataService, configuration, Substitute.For<ILogger<DeleteAllRepositoriesCommandHandler>>());
    }

    private Task<MediatR.Unit> WhenRemovingAll(DeleteAllRepositoriesCommandHandler handler) =>
        handler.Handle(new DeleteAllRepositoriesCommand(_userId), CancellationToken.None);

    private string GivenRepositoryTree()
    {
        var repos = Path.Combine(_sandbox, "repos");
        var nested = Path.Combine(repos, "acme", "api", ".git", "objects");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(repos, "acme", "api", "README.md"), "# api");
        File.WriteAllText(Path.Combine(nested, "pack-01.idx"), "binary-ish");
        return repos;
    }

    // ─── Storage ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemovesEveryRepositoryForTheRequestingUser_AndOnlyThatUser()
    {
        var handler = HandlerFor(localReposPath: null);

        await WhenRemovingAll(handler);

        await _dataService.Received(1).DeleteAllRepositoriesAsync(_userId);
        await _dataService.DidNotReceive().DeleteAllRepositoriesAsync(Arg.Is<UserId>(id => id != _userId));
    }

    /// <summary>
    /// The storage half is the operation. If it fails the caller must hear about it — answering
    /// 204 would tell the user their data is gone when it is not.
    /// </summary>
    [Fact]
    public async Task WhenStorageFails_TheFailurePropagates()
    {
        _dataService.DeleteAllRepositoriesAsync(_userId).ThrowsAsync(new InvalidOperationException("Table unavailable"));
        var handler = HandlerFor(GivenRepositoryTree());

        var act = async () => await WhenRemovingAll(handler);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Table unavailable");
    }

    // ─── Local file system ───────────────────────────────────────────────────

    [Fact]
    public async Task DeletesTheConfiguredRepositoriesTree()
    {
        var repos = GivenRepositoryTree();
        var handler = HandlerFor(repos);

        await WhenRemovingAll(handler);

        Directory.Exists(repos).Should().BeFalse();
    }

    /// <summary>
    /// The blast-radius guard. Everything outside the configured path must survive — this is the
    /// property whose absence once wiped a test run's own scratch files.
    /// </summary>
    [Fact]
    public async Task DeletesNothingOutsideTheConfiguredPath()
    {
        var repos = GivenRepositoryTree();
        var sibling = Path.Combine(_sandbox, "not-repos");
        Directory.CreateDirectory(sibling);
        var bystander = Path.Combine(sibling, "important.txt");
        File.WriteAllText(bystander, "do not delete");

        await WhenRemovingAll(HandlerFor(repos));

        Directory.Exists(repos).Should().BeFalse();
        File.Exists(bystander).Should().BeTrue();
        Directory.Exists(_sandbox).Should().BeTrue("only the configured subtree is in scope");
    }

    [Fact]
    public async Task WhenTheDirectoryDoesNotExist_ItIsANoOp_NotAnError()
    {
        var missing = Path.Combine(_sandbox, "never-created");
        var handler = HandlerFor(missing);

        var act = async () => await WhenRemovingAll(handler);

        await act.Should().NotThrowAsync();
        await _dataService.Received(1).DeleteAllRepositoriesAsync(_userId);
    }

    /// <summary>
    /// An unconfigured path must skip cleanup rather than fall back to a default. There is no safe
    /// default for "directory to delete recursively".
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WhenThePathIsNotConfigured_CleanupIsSkippedAndStorageStillRuns(string? configured)
    {
        var handler = HandlerFor(configured);

        var act = async () => await WhenRemovingAll(handler);

        await act.Should().NotThrowAsync();
        await _dataService.Received(1).DeleteAllRepositoriesAsync(_userId);
    }

    /// <summary>
    /// Local cleanup is secondary to the data cleanup. A file the OS will not release — a routine
    /// occurrence with Git pack files on Windows — must not turn a successful purge into a 500.
    /// </summary>
    [Fact]
    public async Task WhenALocalFileIsLocked_TheRequestStillSucceeds()
    {
        var repos = GivenRepositoryTree();
        var locked = Path.Combine(repos, "acme", "api", ".git", "objects", "pack-01.idx");

        using (var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = async () => await WhenRemovingAll(HandlerFor(repos));

            await act.Should().NotThrowAsync();
        }

        // The point is the absence of a throw: the storage purge is what the caller asked for and
        // it completed. Whether the locked file survived is up to the OS.
        await _dataService.Received(1).DeleteAllRepositoriesAsync(_userId);
    }
}
