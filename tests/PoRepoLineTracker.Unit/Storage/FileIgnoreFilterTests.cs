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

    #endregion

    #region ShouldIgnoreDirectory — directories that should NOT be ignored

    [Theory]
    [InlineData("src")]
    [InlineData("tests")]
    public void ShouldIgnoreDirectory_SourceDirectories_ReturnsFalse(string directoryPath) =>
        _filter.ShouldIgnoreDirectory(directoryPath).Should().BeFalse($"{directoryPath} is a source directory");

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
