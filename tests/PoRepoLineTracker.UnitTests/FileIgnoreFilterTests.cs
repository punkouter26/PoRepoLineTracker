using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.Infrastructure.FileFilters;
using Xunit;

namespace PoRepoLineTracker.UnitTests;

/// <summary>
/// Unit tests for <see cref="FileIgnoreFilter"/>.
/// </summary>
public class FileIgnoreFilterTests
{
    private readonly FileIgnoreFilter _filter;

    public FileIgnoreFilterTests()
    {
        var logger = Substitute.For<ILogger<FileIgnoreFilter>>();
        _filter = new FileIgnoreFilter(logger);
    }

    #region ShouldIgnoreFile - Exact Match Tests

    [Theory]
    [InlineData("packages.config")]
    [InlineData("package-lock.json")]
    [InlineData("yarn.lock")]
    [InlineData("paket.lock")]
    [InlineData("paket.dependencies")]
    [InlineData("launchsettings.json")]
    public void ShouldIgnoreFile_PackageManagerFiles_ReturnsTrue(string fileName)
    {
        // Arrange & Act
        var result = _filter.ShouldIgnoreFile(fileName, "");

        // Assert
        result.Should().BeTrue($"{fileName} is a package manager file");
    }

    #endregion

    #region ShouldIgnoreFile - Extension Suffix Tests

    [Theory]
    [InlineData("mylib.dll")]
    [InlineData("app.exe")]
    [InlineData("debug.pdb")]
    [InlineData("package.nupkg")]
    [InlineData("file.designer.cs")]
    [InlineData("file.g.cs")]
    [InlineData("file.g.i.cs")]
    [InlineData("jquery.min.js")]
    [InlineData("bootstrap.min.css")]
    [InlineData("file.user")]
    [InlineData("font.woff")]
    [InlineData("font.woff2")]
    [InlineData("strings.resources")]
    public void ShouldIgnoreFile_IgnoredExtensions_ReturnsTrue(string fileName)
    {
        // Arrange & Act
        var result = _filter.ShouldIgnoreFile(fileName, "");

        // Assert
        result.Should().BeTrue($"{fileName} has an ignored extension");
    }

    [Theory]
    [InlineData("Program.cs")]
    [InlineData("index.html")]
    [InlineData("app.js")]
    [InlineData("styles.css")]
    [InlineData("MyClass.cs")]
    [InlineData("README.md")]
    [InlineData("package.json")]
    public void ShouldIgnoreFile_SourceCodeFiles_ReturnsFalse(string fileName)
    {
        // Arrange & Act
        var result = _filter.ShouldIgnoreFile(fileName, "");

        // Assert
        result.Should().BeFalse($"{fileName} is a source code file");
    }

    #endregion

    #region ShouldIgnoreFile - Pattern Match Tests

    [Theory]
    [InlineData("Reference.cs")]
    [InlineData("TemporaryGeneratedFile_SomeHash.cs")]
    [InlineData("AssemblyInfo.cs")]
    [InlineData("jquery.js")]
    [InlineData("jquery-ui.js")]
    [InlineData("bootstrap.js")]
    [InlineData("bootstrap-datepicker.js")]
    public void ShouldIgnoreFile_IgnoredPatterns_ReturnsTrue(string fileName)
    {
        // Arrange & Act
        var result = _filter.ShouldIgnoreFile(fileName, "");

        // Assert
        result.Should().BeTrue($"{fileName} matches an ignored pattern");
    }

    #endregion

    #region ShouldIgnoreFile - Migration Folder Tests

    [Fact]
    public void ShouldIgnoreFile_MigrationFolder_ReturnsTrue()
    {
        // Arrange
        var filePath = "src/Migrations/20231001_InitialMigration.cs";
        var fileName = "20231001_InitialMigration.cs";

        // Act
        var result = _filter.ShouldIgnoreFile(fileName, filePath);

        // Assert
        result.Should().BeTrue("file is in a migrations folder");
    }

    [Fact]
    public void ShouldIgnoreFile_NonMigrationFolder_ReturnsFalse()
    {
        // Arrange
        var filePath = "src/Services/MyService.cs";
        var fileName = "MyService.cs";

        // Act
        var result = _filter.ShouldIgnoreFile(fileName, filePath);

        // Assert
        result.Should().BeFalse("file is not in a migrations folder");
    }

    #endregion

    #region ShouldIgnoreDirectory Tests

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("debug")]
    [InlineData("release")]
    [InlineData("node_modules")]
    [InlineData(".vs")]
    [InlineData(".vscode")]
    [InlineData(".idea")]
    [InlineData(".git")]
    [InlineData("packages")]
    [InlineData("wwwroot/lib")]
    public void ShouldIgnoreDirectory_IgnoredDirectories_ReturnsTrue(string directoryPath)
    {
        // Arrange & Act
        var result = _filter.ShouldIgnoreDirectory(directoryPath);

        // Assert
        result.Should().BeTrue($"{directoryPath} is an ignored directory");
    }

    [Theory]
    [InlineData("src/bin")]
    [InlineData("project/obj")]
    [InlineData("app/node_modules")]
    [InlineData("repo/.git")]
    [InlineData("solution/packages")]
    public void ShouldIgnoreDirectory_NestedIgnoredDirectories_ReturnsTrue(string directoryPath)
    {
        // Arrange & Act
        var result = _filter.ShouldIgnoreDirectory(directoryPath);

        // Assert
        result.Should().BeTrue($"{directoryPath} contains an ignored directory");
    }

    [Theory]
    [InlineData("src")]
    [InlineData("src/Services")]
    [InlineData("src/Controllers")]
    [InlineData("tests")]
    [InlineData("docs")]
    public void ShouldIgnoreDirectory_SourceDirectories_ReturnsFalse(string directoryPath)
    {
        // Arrange & Act
        var result = _filter.ShouldIgnoreDirectory(directoryPath);

        // Assert
        result.Should().BeFalse($"{directoryPath} is a source directory");
    }

    #endregion

    #region Case Insensitivity Tests

    [Theory]
    [InlineData("PACKAGES.CONFIG")]
    [InlineData("Packages.Config")]
    [InlineData("MyLib.DLL")]
    [InlineData("app.EXE")]
    [InlineData("file.Designer.CS")]
    public void ShouldIgnoreFile_CaseInsensitive_ReturnsTrue(string fileName)
    {
        // Arrange & Act
        var result = _filter.ShouldIgnoreFile(fileName, "");

        // Assert
        result.Should().BeTrue($"{fileName} should be case-insensitive");
    }

    [Theory]
    [InlineData("BIN")]
    [InlineData("Bin")]
    [InlineData("OBJ")]
    [InlineData("Obj")]
    [InlineData("NODE_MODULES")]
    [InlineData("Node_Modules")]
    public void ShouldIgnoreDirectory_CaseInsensitive_ReturnsTrue(string directoryPath)
    {
        // Arrange & Act
        var result = _filter.ShouldIgnoreDirectory(directoryPath);

        // Assert
        result.Should().BeTrue($"{directoryPath} should be case-insensitive");
    }

    #endregion
}
