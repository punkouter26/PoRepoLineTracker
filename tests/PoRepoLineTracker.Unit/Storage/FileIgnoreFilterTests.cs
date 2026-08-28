using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// Unit tests for <see cref="FileIgnoreFilter"/>.
/// One or two representative rows per rule family; the embedded-repository-root (content-based)
/// rule keeps richer coverage because it is the load-bearing one — see that region.
/// </summary>
public class FileIgnoreFilterTests
{
    private readonly FileIgnoreFilter _filter;

    public FileIgnoreFilterTests()
    {
        var logger = Substitute.For<ILogger<FileIgnoreFilter>>();
        _filter = new FileIgnoreFilter(logger);
    }

    #region ShouldIgnoreFile

    // One row per family: package-manager file, ignored extension, well-known pattern, and a
    // bundled third-party asset. Case-insensitivity is covered by the directory theory below.
    [Theory]
    [InlineData("packages.config", "")]
    [InlineData("mylib.dll", "")]
    [InlineData("AssemblyInfo.cs", "")]
    [InlineData("socket.io.js", "wwwroot/js/socket.io.js")]
    public void ShouldIgnoreFile_ThirdPartyOrGeneratedFiles_ReturnsTrue(string fileName, string path) =>
        _filter.ShouldIgnoreFile(fileName, path).Should().BeTrue($"{fileName} is third-party or generated");

    // Files in a Migrations folder are always ignored
    [Fact]
    public void ShouldIgnoreFile_MigrationFolder_ReturnsTrue() =>
        _filter.ShouldIgnoreFile("20231001_Init.cs", "src/Migrations/20231001_Init.cs")
               .Should().BeTrue("file is in a migrations folder");

    // package.json is real source even though packages.config is not, and a non-migrations
    // folder must not trip the migrations rule.
    [Theory]
    [InlineData("Program.cs", "")]
    [InlineData("package.json", "")]
    [InlineData("MyService.cs", "src/Services/MyService.cs")]
    public void ShouldIgnoreFile_SourceCodeFiles_ReturnsFalse(string fileName, string path) =>
        _filter.ShouldIgnoreFile(fileName, path).Should().BeFalse($"{fileName} is a source code file");

    #endregion

    #region ShouldIgnoreDirectory

    // One or two rows per family: name-based dirs, nested paths, the reverse-domain package
    // convention (Unity's/Java's — nobody names their own app code this way, so it is treated as
    // vendored wherever it sits; Training/.../com.unity.ml-agents is the case it was written for),
    // vendor-convention folders, Unity's root-level generated caches, and case-insensitivity.
    [Theory]
    [InlineData("src/bin")]
    [InlineData("Packages/com.unity.ml-agents")]
    [InlineData("Training/ml-agents/com.unity.ml-agents/Runtime")]
    [InlineData("Assets/Plugins")]
    [InlineData("Library")]
    [InlineData("NODE_MODULES")]
    public void ShouldIgnoreDirectory_VendoredOrGeneratedDirectories_ReturnsTrue(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeTrue($"{directoryPath} is vendored or generated");

    // The root-only list must not reach into the tree: "Library" at the top of a Unity project is
    // a generated cache, but `src/Library/` is somebody's own code and counting it is the point.
    [Theory]
    [InlineData("src")]
    [InlineData("src/Library")]
    public void ShouldIgnoreDirectory_SourceDirectories_ReturnsFalse(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeFalse($"{directoryPath} is a source directory");

    #endregion

    #region Embedded repository roots (vendored third-party projects)

    /// <summary>
    /// The real case: a repository with Unity's ml-agents copied wholesale into `Training/`.
    /// The reverse-domain rule catches only the `com.unity.ml-agents/` package inside it, leaving
    /// the Python trainer, the sample Unity projects and the CI scripts counted as the author's
    /// own code — about 78% of the reported total on the repository this was measured against.
    /// </summary>
    private static readonly string[] MlAgentsRootEntries =
    [
        ".github", ".gitmodules", ".gitattributes", ".pre-commit-config.yaml",
        "CODEOWNERS", "CODE_OF_CONDUCT.md", "LICENSE.md", "Readme.md",
        "ml-agents", "ml-agents-envs", "Project", "config", "docs", "setup.cfg"
    ];

    [Fact]
    public void ShouldIgnoreDirectory_VendoredRepositoryRoot_ReturnsTrue() =>
        _filter.ShouldIgnoreDirectory("Training/ml-agents", MlAgentsRootEntries)
               .Should().BeTrue("a nested directory carrying a licence, CODEOWNERS and .github is another project copied in");

    [Fact]
    public void ShouldIgnoreDirectory_RepositoryOwnRoot_ReturnsFalse() =>
        _filter.ShouldIgnoreDirectory("", MlAgentsRootEntries)
               .Should().BeFalse("the repository's own root is supposed to carry these files");

    [Fact]
    public void ShouldIgnoreDirectory_SingleRootMarker_ReturnsFalse() =>
        _filter.ShouldIgnoreDirectory("src/Feature", new[] { "LICENSE.md", "Handler.cs", "Model.cs" })
               .Should().BeFalse("one marker is not enough — a lone licence file next to real code must not delete the folder");

    [Fact]
    public void ShouldIgnoreDirectory_OrdinarySourceFolder_ReturnsFalse() =>
        _filter.ShouldIgnoreDirectory("src/PoRepoLineTracker.API",
                   new[] { "Program.cs", "GlobalUsings.cs", "Features", "Storage", "Extensions" })
               .Should().BeFalse("this project's own source folders carry no repository-root markers");

    [Fact]
    public void ShouldIgnoreDirectory_EntryAwareOverload_StillAppliesNameRules() =>
        _filter.ShouldIgnoreDirectory("app/node_modules", new[] { "index.js" })
               .Should().BeTrue("the entry-aware overload must not lose the path-based rules");

    #endregion

    #region Credential scrubbing

    // `git clone https://x-access-token:<token>@github.com/...` persists that URL verbatim as
    // remote.origin.url, writing a live GitHub credential to disk in plaintext. GitClient scrubs
    // it immediately after cloning; these cover the scrubber itself.
    [Theory]
    [InlineData("https://x-access-token:ghp_secret@github.com/o/r.git", "https://github.com/o/r.git")]
    [InlineData("https://github.com/o/r.git", "https://github.com/o/r.git")]
    public void StripCredentials_RemovesUserInfoOnly(string input, string expected) =>
        GitClient.StripCredentials(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("git@github.com:o/r.git")]
    public void StripCredentials_LeavesUnparseableInputAlone(string input) =>
        GitClient.StripCredentials(input).Should().Be(input);

    #endregion
}
