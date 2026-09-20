using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.API.Analysis;

namespace PoRepoLineTracker.Unit.Analysis;

public class CustomGlobFilterTests
{
    private readonly FileIgnoreFilter _filter;

    public CustomGlobFilterTests()
    {
        var logger = Substitute.For<ILogger<FileIgnoreFilter>>();
        _filter = new FileIgnoreFilter(logger);
    }

    [Theory]
    [InlineData("src/Generated/Models.cs", "src/Generated/**", true)]
    [InlineData("src/Data/UserDbContextModelSnapshot.g.cs", "**/*.g.cs", true)]
    [InlineData("docs/README.md", "docs/**", true)]
    [InlineData("src/Controllers/HomeController.cs", "**/*.g.cs", false)]
    [InlineData("src/Services/PaymentService.cs", "docs/**", false)]
    public void MatchesCustomGlobs_correctly_matches_glob_patterns(string path, string pattern, bool expected)
    {
        var result = FileIgnoreFilter.MatchesCustomGlobs(path, [pattern]);
        result.Should().Be(expected);
    }

    [Fact]
    public void ShouldIgnoreFile_prunes_file_when_matching_active_custom_globs()
    {
        _filter.CustomIgnoreGlobs = ["**/fixtures/**", "**/*.fixture.json"];

        _filter.ShouldIgnoreFile("user.fixture.json", "tests/fixtures/user.fixture.json").Should().BeTrue();
        _filter.ShouldIgnoreFile("sample.txt", "tests/fixtures/sample.txt").Should().BeTrue();

        // Non-matching file is not pruned by custom globs
        _filter.ShouldIgnoreFile("UserService.cs", "src/Services/UserService.cs").Should().BeFalse();
    }

    [Fact]
    public void ShouldIgnoreDirectory_prunes_directory_when_matching_active_custom_globs()
    {
        _filter.CustomIgnoreGlobs = ["**/testdata/**"];

        _filter.ShouldIgnoreDirectory("tests/testdata").Should().BeTrue();
        _filter.ShouldIgnoreDirectory("src/Services").Should().BeFalse();
    }
}

