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

    [Fact]
    public void MatchesCustomGlobs_correctly_matches_glob_patterns()
    {
        FileIgnoreFilter.MatchesCustomGlobs("src/Generated/Models.cs", ["src/Generated/**"]).Should().BeTrue();
        FileIgnoreFilter.MatchesCustomGlobs("src/Data/UserDbContextModelSnapshot.g.cs", ["**/*.g.cs"]).Should().BeTrue();
        FileIgnoreFilter.MatchesCustomGlobs("docs/README.md", ["docs/**"]).Should().BeTrue();
        FileIgnoreFilter.MatchesCustomGlobs("src/Controllers/HomeController.cs", ["**/*.g.cs"]).Should().BeFalse();
        FileIgnoreFilter.MatchesCustomGlobs("src/Services/PaymentService.cs", ["docs/**"]).Should().BeFalse();
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

