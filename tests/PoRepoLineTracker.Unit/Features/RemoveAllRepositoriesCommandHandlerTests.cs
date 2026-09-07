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
public sealed class RemoveAllRepositoriesCommandHandlerTests : IDisposable
{
    private readonly IRepositoryDataService _dataService = Substitute.For<IRepositoryDataService>();
    private readonly UserId _userId = UserId.New();
    private readonly string _sandbox;

    public RemoveAllRepositoriesCommandHandlerTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), $"PoRepoLineTracker.RemoveAllTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true); }
        catch { /* best-effort teardown; the OS reclaims temp anyway */ }
    }

    private RemoveAllRepositoriesCommandHandler HandlerFor(string? localReposPath)
    {
        var settings = new Dictionary<string, string?>();
        if (localReposPath is not null) settings[ConfigKeys.GitHub.LocalReposPath] = localReposPath;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        return new RemoveAllRepositoriesCommandHandler(
            _dataService, configuration, Substitute.For<ILogger<RemoveAllRepositoriesCommandHandler>>());
    }

    private Task<MediatR.Unit> WhenRemovingAll(RemoveAllRepositoriesCommandHandler handler) =>
        handler.Handle(new RemoveAllRepositoriesCommand(_userId), CancellationToken.None);

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
    public async Task RemovesTheUsersStorageRowsAndTheConfiguredRepositoriesTree()
    {
        var repos = GivenRepositoryTree();
        var handler = HandlerFor(repos);

        await WhenRemovingAll(handler);

        await _dataService.Received(1).RemoveAllRepositoriesAsync(_userId);
        await _dataService.DidNotReceive().RemoveAllRepositoriesAsync(Arg.Is<UserId>(id => id != _userId));
        Directory.Exists(repos).Should().BeFalse();
    }

    [Fact]
    public async Task WhenStorageFails_TheFailurePropagates()
    {
        _dataService.RemoveAllRepositoriesAsync(_userId).ThrowsAsync(new InvalidOperationException("Table unavailable"));
        var handler = HandlerFor(GivenRepositoryTree());

        var act = async () => await WhenRemovingAll(handler);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Table unavailable");
    }

    // ─── Local file system ───────────────────────────────────────────────────

    [Fact]
    public async Task LocalFileSystem_DeletesOnlyConfiguredPathAndSurvivesLockedFiles()
    {
        var repos = GivenRepositoryTree();
        var sibling = Path.Combine(_sandbox, "not-repos");
        Directory.CreateDirectory(sibling);
        var bystander = Path.Combine(sibling, "important.txt");
        File.WriteAllText(bystander, "do not delete");

        var locked = Path.Combine(repos, "acme", "api", ".git", "objects", "pack-01.idx");
        using (var hold = new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var act = async () => await WhenRemovingAll(HandlerFor(repos));
            await act.Should().NotThrowAsync();
        }

        File.Exists(bystander).Should().BeTrue();
        Directory.Exists(_sandbox).Should().BeTrue();
        await _dataService.Received(1).RemoveAllRepositoriesAsync(_userId);
    }

    [Fact]
    public async Task LocalFileSystem_MissingOrUnconfiguredPath_SkipsCleanupWithoutError()
    {
        var missingHandler = HandlerFor(Path.Combine(_sandbox, "never-created"));
        var actMissing = async () => await WhenRemovingAll(missingHandler);
        await actMissing.Should().NotThrowAsync();

        var nullHandler = HandlerFor(null);
        var actNull = async () => await WhenRemovingAll(nullHandler);
        await actNull.Should().NotThrowAsync();

        var emptyHandler = HandlerFor("");
        var actEmpty = async () => await WhenRemovingAll(emptyHandler);
        await actEmpty.Should().NotThrowAsync();

        await _dataService.Received(3).RemoveAllRepositoriesAsync(_userId);
    }
}
