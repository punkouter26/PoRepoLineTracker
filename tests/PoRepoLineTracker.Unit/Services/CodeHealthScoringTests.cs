using FluentAssertions;
using PoRepoLineTracker.API.Services;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The judging half of code health. Scoring is all judgement calls, so what is worth pinning is
/// not the exact numbers but the PROPERTIES a reader will assume hold: that every factor points
/// the same way, that the composite honours its weights, that worse input never scores better, and
/// that the hotspot list surfaces real offenders rather than trivia.
/// </summary>
public class CodeHealthScoringTests
{
    private static FileMetrics File(
        string path,
        int codeLines,
        int complexity = 1,
        int nesting = 2,
        int comments = 0,
        int longLines = 0,
        int debt = 0) =>
        new(path, Path.GetExtension(path), codeLines, comments, 0, complexity, nesting, longLines, debt);

    [Fact]
    public void NoFiles_ReportsNoData()
    {
        var report = CodeHealthScoring.Build([]);

        report.HasData.Should().BeFalse();
        report.Factors.Should().BeEmpty();
    }

    [Fact]
    public void FilesWithNoCodeLines_ReportNoData()
    {
        // A tree of nothing but comments and blanks has nothing to judge — and would otherwise
        // divide by zero computing every density.
        var report = CodeHealthScoring.Build([File("a.cs", codeLines: 0, comments: 20)]);

        report.HasData.Should().BeFalse();
    }

    [Fact]
    public void EveryFactorIsScoredZeroToOneHundred_AndWeightsSumToOneHundred()
    {
        var report = CodeHealthScoring.Build([File("a.cs", codeLines: 200, complexity: 30, comments: 40)]);

        report.Factors.Should().HaveCount(6);
        report.Factors.Should().OnlyContain(f => f.Score >= 0 && f.Score <= 100);
        report.Factors.Sum(f => f.Weight).Should().Be(100, "the composite is a weighted average out of 100");
        report.Factors.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Measurement));
        report.Factors.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Explanation));
    }

    [Fact]
    public void CleanCode_ScoresWellAndGradesHigh()
    {
        // Modest branching, shallow nesting, small files, reasonable comments, no debt.
        var files = Enumerable.Range(0, 10)
            .Select(i => File($"src/File{i}.cs", codeLines: 120, complexity: 12, nesting: 2, comments: 25))
            .ToList();

        var report = CodeHealthScoring.Build(files);

        report.Score.Should().BeGreaterThan(80);
        report.Grade.Should().BeOneOf("A", "B");
    }

    [Fact]
    public void TangledCode_ScoresPoorlyAndGradesLow()
    {
        var files = Enumerable.Range(0, 10)
            .Select(i => File($"src/File{i}.cs", codeLines: 900, complexity: 400, nesting: 9, comments: 5, longLines: 300, debt: 20))
            .ToList();

        var report = CodeHealthScoring.Build(files);

        report.Score.Should().BeLessThan(45);
        report.Grade.Should().Be("F");
    }

    /// <summary>
    /// Monotonicity. Whatever the exact bands, adding branches to otherwise identical code must
    /// never improve the score — a metric that can be gamed in the wrong direction is worse than
    /// none.
    /// </summary>
    [Fact]
    public void MoreBranchesNeverScoresBetter()
    {
        var simple = CodeHealthScoring.Build([File("a.cs", codeLines: 300, complexity: 20)]);
        var tangled = CodeHealthScoring.Build([File("a.cs", codeLines: 300, complexity: 120)]);

        tangled.Score.Should().BeLessThan(simple.Score);
    }

    [Fact]
    public void DeeperNestingNeverScoresBetter()
    {
        var shallow = CodeHealthScoring.Build([File("a.cs", codeLines: 300, nesting: 2)]);
        var deep = CodeHealthScoring.Build([File("a.cs", codeLines: 300, nesting: 10)]);

        deep.Score.Should().BeLessThan(shallow.Score);
    }

    /// <summary>
    /// Documentation is the one non-monotonic factor: a very high comment ratio usually means
    /// commented-out code, not unusually good documentation, so both extremes score below the band.
    /// </summary>
    [Fact]
    public void DocumentationScoresBestInsideItsBand_AndFallsAwayOnBothSides()
    {
        int DocScore(int comments) => CodeHealthScoring
            .Build([File("a.cs", codeLines: 100, comments: comments)])
            .Factors.Single(f => f.Key == "documentation").Score;

        var none = DocScore(0);
        var healthy = DocScore(25);   // 20% of comment+code
        var drowning = DocScore(400); // 80%

        healthy.Should().Be(100);
        none.Should().BeLessThan(healthy);
        drowning.Should().BeLessThan(healthy);
    }

    [Fact]
    public void FileSizeIsMeasuredByShareOfCode_NotByFileCount()
    {
        // One enormous file among many small ones must still register: measuring "how many files
        // are large" would let it hide behind the count.
        var withGiant = CodeHealthScoring.Build(
        [
            File("giant.cs", codeLines: 5_000),
            .. Enumerable.Range(0, 20).Select(i => File($"small{i}.cs", codeLines: 50))
        ]);

        var allSmall = CodeHealthScoring.Build(
            Enumerable.Range(0, 20).Select(i => File($"small{i}.cs", codeLines: 50)).ToList());

        var giantScore = withGiant.Factors.Single(f => f.Key == "file-size").Score;
        var smallScore = allSmall.Factors.Single(f => f.Key == "file-size").Score;

        giantScore.Should().BeLessThan(smallScore);
    }

    // ─── Hotspots ────────────────────────────────────────────────────────────

    [Fact]
    public void Hotspots_ListTheWorstFilesFirst()
    {
        var report = CodeHealthScoring.Build(
        [
            File("src/Fine.cs", codeLines: 100, complexity: 8, nesting: 2),
            File("src/Awful.cs", codeLines: 1_500, complexity: 500, nesting: 11, debt: 15),
            File("src/Middling.cs", codeLines: 300, complexity: 60, nesting: 5)
        ]);

        report.Hotspots.First().Path.Should().Be("src/Awful.cs");
        report.Hotspots.First().PrimaryConcern.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Without a size floor the list fills with trivia: a four-line file holding one `if` has a
    /// dreadful complexity DENSITY and nothing wrong with it, and it would push the real offenders
    /// off the end.
    /// </summary>
    [Fact]
    public void Hotspots_IgnoreTinyFiles()
    {
        var report = CodeHealthScoring.Build(
        [
            File("src/Tiny.cs", codeLines: 4, complexity: 4, nesting: 3),
            File("src/Real.cs", codeLines: 600, complexity: 200, nesting: 8)
        ]);

        report.Hotspots.Should().NotContain(h => h.Path == "src/Tiny.cs");
        report.Hotspots.Should().Contain(h => h.Path == "src/Real.cs");
    }

    // ─── By language ─────────────────────────────────────────────────────────

    [Fact]
    public void ByLanguage_ScoresEachExtensionSeparately_LargestFirst()
    {
        var report = CodeHealthScoring.Build(
        [
            File("a.cs", codeLines: 1_000, complexity: 300, nesting: 9),
            File("b.ts", codeLines: 200, complexity: 20, nesting: 2, comments: 40)
        ]);

        report.ByLanguage.Should().HaveCount(2);
        report.ByLanguage.First().Extension.Should().Be(".cs", "ordered by how much code there is");

        var cs = report.ByLanguage.Single(l => l.Extension == ".cs");
        var ts = report.ByLanguage.Single(l => l.Extension == ".ts");
        ts.Score.Should().BeGreaterThan(cs.Score, "the TypeScript here is genuinely healthier");
    }

    [Fact]
    public void GradeBandsFollowTheScore()
    {
        var report = CodeHealthScoring.Build([File("a.cs", codeLines: 200, complexity: 20, comments: 40)]);

        var expected = report.Score switch
        {
            >= 90 => "A",
            >= 80 => "B",
            >= 70 => "C",
            >= 60 => "D",
            _ => "F"
        };

        report.Grade.Should().Be(expected);
    }
}

/// <summary>
/// The single combined figure. It exists because the two analysers were being shown side by side
/// and visibly disagreed — one repository read "maintainability 62" next to grade A while another
/// read "67" next to grade F, and nothing can be ranked on two scales at once.
/// </summary>
public class CodeHealthScoringCombineTests
{
    [Fact]
    public void PureCSharpRepository_ScoresItsMaintainabilityIndex()
    {
        var (score, grade) = CodeHealthScoring.Combine(
            maintainabilityIndex: 82, csharpLines: 9_000, heuristicScore: 0, otherLines: 0);

        score.Should().Be(82);
        grade.Should().Be("B");
    }

    [Fact]
    public void RepositoryWithNoCSharp_ScoresTheHeuristicComposite()
    {
        var (score, grade) = CodeHealthScoring.Combine(
            maintainabilityIndex: null, csharpLines: 0, heuristicScore: 74, otherLines: 5_000);

        score.Should().Be(74);
        grade.Should().Be("C");
    }

    /// <summary>
    /// The reason it is weighted rather than averaged. A few hundred lines of config must not
    /// swing the verdict on fifty thousand lines of code — the same rule contributor share follows.
    /// </summary>
    [Fact]
    public void TheLargerLanguage_DominatesTheCombinedScore()
    {
        var (score, _) = CodeHealthScoring.Combine(
            maintainabilityIndex: 90, csharpLines: 50_000,
            heuristicScore: 10, otherLines: 500);

        score.Should().BeGreaterThan(85, "500 lines of YAML cannot outvote 50,000 lines of C#");

        // A plain average would land near 50 and call a healthy repository a failure.
        score.Should().NotBeInRange(40, 60);
    }

    [Fact]
    public void EqualSizedHalves_LandBetweenTheirTwoScores()
    {
        var (score, _) = CodeHealthScoring.Combine(
            maintainabilityIndex: 80, csharpLines: 1_000,
            heuristicScore: 60, otherLines: 1_000);

        score.Should().Be(70);
    }

    [Fact]
    public void NothingMeasured_ScoresZeroWithNoGrade()
    {
        var (score, grade) = CodeHealthScoring.Combine(
            maintainabilityIndex: null, csharpLines: 0, heuristicScore: 0, otherLines: 0);

        score.Should().Be(0);
        grade.Should().BeEmpty("an ungraded repository must not be handed an F it did not earn");
    }

    /// <summary>
    /// A repository can hold C# whose line count did not register. The C# side must still count,
    /// or its index would be silently discarded and the repository judged on its config files.
    /// </summary>
    [Fact]
    public void CSharpWithZeroRecordedLines_StillContributes()
    {
        var (score, _) = CodeHealthScoring.Combine(
            maintainabilityIndex: 40, csharpLines: 0, heuristicScore: 100, otherLines: 0);

        score.Should().Be(40);
    }

    [Fact]
    public void CombinedScore_IsAlwaysWithinRange()
    {
        foreach (var mi in new[] { 0, 50, 100 })
        foreach (var heuristic in new[] { 0, 50, 100 })
        {
            var (score, _) = CodeHealthScoring.Combine(mi, 1_000, heuristic, 1_000);
            score.Should().BeInRange(0, 100);
        }
    }
}
