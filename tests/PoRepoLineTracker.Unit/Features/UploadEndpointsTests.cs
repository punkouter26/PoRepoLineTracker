using FluentAssertions;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The zip-upload path's two pure helpers, neither of which had a test.
///
/// <para><see cref="UploadEndpoints.SanitizeRepoName"/> produces the repository's display name.
/// It does not reach the filesystem today — the extracted tree lands in <c>local_{Guid}</c> — but
/// the tests pin the leaf-name invariant anyway, so that a future caller that does build a path
/// from it cannot quietly introduce a traversal. <see cref="UploadEndpoints.FindGitFolder"/>
/// decides whether an upload is usable at all, and has to cope with the layouts real archives
/// actually arrive in.</para>
/// </summary>
public sealed class UploadEndpointsTests : IDisposable
{
    private readonly string _sandbox;

    public UploadEndpointsTests()
    {
        _sandbox = Path.Combine(Path.GetTempPath(), $"PoRepoLineTracker.UploadTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_sandbox);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_sandbox)) Directory.Delete(_sandbox, recursive: true); }
        catch { /* best-effort teardown */ }
    }

    /// <summary>A .git directory is only recognised as one if it carries a config file.</summary>
    private static void GivenGitRepositoryAt(string path)
    {
        var git = Path.Combine(path, ".git");
        Directory.CreateDirectory(git);
        File.WriteAllText(Path.Combine(git, "config"), "[core]\n\trepositoryformatversion = 0\n");
    }

    // ─── SanitizeRepoName ────────────────────────────────────────────────────

    [Theory]
    [InlineData("my-repo", "my-repo")]
    [InlineData("PoRepoLineTracker", "PoRepoLineTracker")]
    [InlineData("repo.name.with.dots", "repo.name.with.dots")]
    [InlineData("under_scores-and-dashes", "under_scores-and-dashes")]
    public void SanitizeRepoName_LeavesAnAlreadySafeNameAlone(string input, string expected) =>
        UploadEndpoints.SanitizeRepoName(input).Should().Be(expected);

    /// <summary>
    /// The invariant worth holding: whatever comes in, what comes out is a single leaf name.
    /// Note that dots survive — ".._.._etc_passwd" is the result for "../../etc/passwd", and it is
    /// inert precisely because every separator is gone.
    /// </summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..\\..\\Windows\\System32")]
    [InlineData("/absolute/path")]
    [InlineData("C:\\Users\\punko\\repo")]
    [InlineData("nested/sub/dir")]
    public void SanitizeRepoName_StripsEveryPathSeparator(string hostile)
    {
        var sanitized = UploadEndpoints.SanitizeRepoName(hostile);

        sanitized.Should().NotContain("/");
        sanitized.Should().NotContain("\\");
        Path.GetFileName(sanitized).Should().Be(sanitized, "the result must be a leaf name, not a path");
        Path.IsPathRooted(sanitized).Should().BeFalse();
    }

    /// <summary>
    /// The one input that IS still a traversal token after sanitising, since '.' is not an invalid
    /// filename character. Harmless while the caller ignores this value for pathing — recorded
    /// here so the next person to build a path from it sees the case first.
    /// </summary>
    [Theory]
    [InlineData("..")]
    [InlineData(".")]
    public void SanitizeRepoName_PassesBareDotsThrough_WhichIsSafeOnlyBecauseNoPathIsBuiltFromIt(string dots) =>
        UploadEndpoints.SanitizeRepoName(dots).Should().Be(dots);

    [Fact]
    public void SanitizeRepoName_FallsBackWhenNothingUsableIsLeft()
    {
        UploadEndpoints.SanitizeRepoName("///").Should().Be("uploaded-repo");
        UploadEndpoints.SanitizeRepoName("   ").Should().Be("uploaded-repo");
        UploadEndpoints.SanitizeRepoName(string.Empty).Should().Be("uploaded-repo");
    }

    /// <summary>
    /// Windows' MAX_PATH is the real constraint: this name becomes a directory that then holds a
    /// full git tree, and deep pack paths eat the remaining budget quickly.
    /// </summary>
    [Fact]
    public void SanitizeRepoName_CapsLengthAtOneHundred()
    {
        var sanitized = UploadEndpoints.SanitizeRepoName(new string('a', 500));

        sanitized.Should().HaveLength(100);
    }

    // ─── FindGitFolder ───────────────────────────────────────────────────────

    [Fact]
    public void FindGitFolder_FindsARepositoryAtTheRootOfTheArchive()
    {
        GivenGitRepositoryAt(_sandbox);

        var (gitPath, repoRoot) = UploadEndpoints.FindGitFolder(_sandbox);

        gitPath.Should().Be(Path.Combine(_sandbox, ".git"));
        repoRoot.Should().Be(_sandbox);
    }

    /// <summary>The layout GitHub's own "Download ZIP" produces: everything under one named folder.</summary>
    [Fact]
    public void FindGitFolder_FindsARepositoryWrappedInASingleFolder()
    {
        var inner = Path.Combine(_sandbox, "my-repo-main");
        Directory.CreateDirectory(inner);
        GivenGitRepositoryAt(inner);

        var (gitPath, repoRoot) = UploadEndpoints.FindGitFolder(_sandbox);

        gitPath.Should().Be(Path.Combine(inner, ".git"));
        repoRoot.Should().Be(inner);
    }

    [Fact]
    public void FindGitFolder_FindsARepositoryAmongSeveralSiblingFolders()
    {
        Directory.CreateDirectory(Path.Combine(_sandbox, "docs"));
        var repo = Path.Combine(_sandbox, "src");
        Directory.CreateDirectory(repo);
        GivenGitRepositoryAt(repo);

        var (gitPath, repoRoot) = UploadEndpoints.FindGitFolder(_sandbox);

        gitPath.Should().Be(Path.Combine(repo, ".git"));
        repoRoot.Should().Be(repo);
    }

    [Fact]
    public void FindGitFolder_ReturnsNothingForAnArchiveWithNoRepository()
    {
        Directory.CreateDirectory(Path.Combine(_sandbox, "just-files"));
        File.WriteAllText(Path.Combine(_sandbox, "readme.txt"), "no git here");

        var (gitPath, repoRoot) = UploadEndpoints.FindGitFolder(_sandbox);

        gitPath.Should().BeNull();
        repoRoot.Should().BeNull();
    }

    /// <summary>
    /// A bare .git directory with no config is not a repository — most often it is an empty
    /// folder an archiver preserved. Accepting it would hand LibGit2Sharp something it cannot open
    /// and turn a clear "no repository found" into an opaque native error.
    /// </summary>
    [Fact]
    public void FindGitFolder_RejectsAGitDirectoryWithNoConfig()
    {
        Directory.CreateDirectory(Path.Combine(_sandbox, ".git"));

        var (gitPath, _) = UploadEndpoints.FindGitFolder(_sandbox);

        gitPath.Should().BeNull();
    }

    [Fact]
    public void FindGitFolder_PrefersTheRootWhenBothRootAndASubfolderAreRepositories()
    {
        GivenGitRepositoryAt(_sandbox);
        var inner = Path.Combine(_sandbox, "vendored");
        Directory.CreateDirectory(inner);
        GivenGitRepositoryAt(inner);

        var (_, repoRoot) = UploadEndpoints.FindGitFolder(_sandbox);

        repoRoot.Should().Be(_sandbox, "the outer repository is the one that was uploaded");
    }
}
