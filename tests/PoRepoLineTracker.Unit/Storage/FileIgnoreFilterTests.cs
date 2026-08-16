using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// Unit tests for <see cref="FileIgnoreFilter"/>.
/// Consolidated: representative samples per category to stay within the 100-test budget.
/// </summary>
public class FileIgnoreFilterTests
{
    private readonly FileIgnoreFilter _filter;

    public FileIgnoreFilterTests()
    {
        var logger = Substitute.For<ILogger<FileIgnoreFilter>>();
        _filter = new FileIgnoreFilter(logger);
    }

    #region ShouldIgnoreFile — files that SHOULD be ignored

    // Representative package manager / lock files
    [Theory]
    [InlineData("packages.config")]
    [InlineData("package-lock.json")]
    [InlineData("yarn.lock")]
    [InlineData("launchsettings.json")]
    public void ShouldIgnoreFile_PackageManagerFiles_ReturnsTrue(string fileName) =>
        _filter.ShouldIgnoreFile(fileName, "").Should().BeTrue($"{fileName} is a package manager file");

    // Representative binary / generated-code extensions
    [Theory]
    [InlineData("mylib.dll")]
    [InlineData("file.designer.cs")]
    [InlineData("file.g.cs")]
    [InlineData("jquery.min.js")]
    public void ShouldIgnoreFile_IgnoredExtensions_ReturnsTrue(string fileName) =>
        _filter.ShouldIgnoreFile(fileName, "").Should().BeTrue($"{fileName} has an ignored extension");

    // Representative well-known pattern matches
    [Theory]
    [InlineData("Reference.cs")]
    [InlineData("AssemblyInfo.cs")]
    [InlineData("jquery.js")]
    public void ShouldIgnoreFile_IgnoredPatterns_ReturnsTrue(string fileName) =>
        _filter.ShouldIgnoreFile(fileName, "").Should().BeTrue($"{fileName} matches an ignored pattern");

    [Theory]
    [InlineData("socket.io.js")]
    [InlineData("socket_io.js")]
    [InlineData("socket-io-client.js")]
    public void ShouldIgnoreFile_BundledLibraryFiles_ReturnsTrue(string fileName) =>
        _filter.ShouldIgnoreFile(fileName, $"wwwroot/js/{fileName}").Should().BeTrue($"{fileName} is a bundled third-party asset");

    // Files in a Migrations folder are always ignored
    [Fact]
    public void ShouldIgnoreFile_MigrationFolder_ReturnsTrue() =>
        _filter.ShouldIgnoreFile("20231001_Init.cs", "src/Migrations/20231001_Init.cs")
               .Should().BeTrue("file is in a migrations folder");

    #endregion

    #region ShouldIgnoreFile — files that should NOT be ignored

    // Representative source code files
    [Theory]
    [InlineData("Program.cs")]
    [InlineData("index.html")]
    [InlineData("app.js")]
    [InlineData("package.json")]
    public void ShouldIgnoreFile_SourceCodeFiles_ReturnsFalse(string fileName) =>
        _filter.ShouldIgnoreFile(fileName, "").Should().BeFalse($"{fileName} is a source code file");

    // Non-migrations folder is not ignored
    [Fact]
    public void ShouldIgnoreFile_NonMigrationFolder_ReturnsFalse() =>
        _filter.ShouldIgnoreFile("MyService.cs", "src/Services/MyService.cs")
               .Should().BeFalse("file is not in a migrations folder");

    #endregion

    #region ShouldIgnoreDirectory — directories that SHOULD be ignored

    // Top-level ignored directories
    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    [InlineData(".git")]
    [InlineData("packages")]
    [InlineData("vendor")]
    [InlineData("third-party")]
    public void ShouldIgnoreDirectory_IgnoredDirectories_ReturnsTrue(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeTrue($"{directoryPath} is an ignored directory");

    // Nested paths containing an ignored segment
    [Theory]
    [InlineData("src/bin")]
    [InlineData("app/node_modules")]
    public void ShouldIgnoreDirectory_NestedIgnoredDirectories_ReturnsTrue(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeTrue($"{directoryPath} contains an ignored directory");

    // Unity's (and Java's) reverse-domain package naming convention — nobody names their own app
    // code this way, so it is treated as vendored regardless of which folder it sits under.
    [Theory]
    [InlineData("Packages/com.unity.ml-agents")]
    [InlineData("Training/ml-agents/com.unity.ml-agents/Runtime")]
    [InlineData("com.google.firebase.analytics")]
    public void ShouldIgnoreDirectory_ReverseDomainPackageDirectories_ReturnsTrue(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeTrue($"{directoryPath} is a vendored reverse-domain package");

    [Theory]
    [InlineData("external")]
    [InlineData("externals")]
    public void ShouldIgnoreDirectory_ExternalDirectories_ReturnsTrue(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeTrue($"{directoryPath} is a vendored-code directory");

    [Theory]
    [InlineData("Assets/Plugins")]
    [InlineData("Assets/Plugins/NuGet")]
    [InlineData("Assets/ThirdParty")]
    [InlineData("lib/site-packages")]
    [InlineData("tools/.venv")]
    [InlineData("src/__pycache__")]
    [InlineData("deps")]
    [InlineData("submodules")]
    public void ShouldIgnoreDirectory_VendorConventionDirectories_ReturnsTrue(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeTrue($"{directoryPath} holds third-party code by convention");

    // Unity's generated caches. Matched at the repository root ONLY — see the pair of negative
    // cases below, which are the reason this is not a blanket name match.
    [Theory]
    [InlineData("Library")]
    [InlineData("Temp")]
    [InlineData("Logs")]
    [InlineData("Builds")]
    public void ShouldIgnoreDirectory_UnityGeneratedCachesAtRoot_ReturnsTrue(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeTrue($"{directoryPath} at the root is a generated Unity cache");

    #endregion

    #region ShouldIgnoreDirectory — directories that should NOT be ignored

    [Theory]
    [InlineData("src")]
    [InlineData("tests")]
    public void ShouldIgnoreDirectory_SourceDirectories_ReturnsFalse(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeFalse($"{directoryPath} is a source directory");

    // The root-only list must not reach into the tree: "Library" at the top of a Unity project is
    // a generated cache, but `src/Library/` is somebody's own code and counting it is the point.
    [Theory]
    [InlineData("src/Library")]
    [InlineData("app/Temp")]
    [InlineData("Assets/Scripts/Logs")]
    public void ShouldIgnoreDirectory_GenericNamesBelowTheRoot_ReturnsFalse(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeFalse($"{directoryPath} is nested, so the name is not a Unity cache");

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

    [Theory]
    [InlineData("LICENSE.md")]
    [InlineData(".github")]
    public void ShouldIgnoreDirectory_SingleRootMarker_ReturnsFalse(string marker) =>
        _filter.ShouldIgnoreDirectory("src/Feature", new[] { marker, "Handler.cs", "Model.cs" })
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
    [InlineData("https://user:pass@github.com/o/r", "https://github.com/o/r")]
    [InlineData("https://github.com/o/r.git", "https://github.com/o/r.git")]
    public void StripCredentials_RemovesUserInfoOnly(string input, string expected) =>
        GitClient.StripCredentials(input).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("git@github.com:o/r.git")]
    public void StripCredentials_LeavesUnparseableInputAlone(string input) =>
        GitClient.StripCredentials(input).Should().Be(input);

    #endregion

    #region Case Insensitivity

    // File matching is case-insensitive
    [Theory]
    [InlineData("PACKAGES.CONFIG")]
    [InlineData("MyLib.DLL")]
    public void ShouldIgnoreFile_CaseInsensitive_ReturnsTrue(string fileName) =>
        _filter.ShouldIgnoreFile(fileName, "").Should().BeTrue($"{fileName} should be matched case-insensitively");

    // Directory matching is case-insensitive
    [Theory]
    [InlineData("BIN")]
    [InlineData("OBJ")]
    [InlineData("NODE_MODULES")]
    public void ShouldIgnoreDirectory_CaseInsensitive_ReturnsTrue(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeTrue($"{directoryPath} should be matched case-insensitively");

    #endregion
}
