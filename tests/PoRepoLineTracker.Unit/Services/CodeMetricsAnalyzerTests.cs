using FluentAssertions;
using PoRepoLineTracker.API.Services;

namespace PoRepoLineTracker.Unit;

/// <summary>
/// The measuring half of code health. Every figure here is a proxy rather than a parser result, so
/// what these pin is that each proxy measures the thing it claims to — a complexity count that
/// includes commented-out branches, or a comment ratio that counts blank lines, would still
/// produce a plausible score while ranking the wrong files first.
/// </summary>
public class CodeMetricsAnalyzerTests
{
    private readonly CodeMetricsAnalyzer _sut = new();

    private FileMetrics Analyze(string content, string extension = ".cs") =>
        _sut.Analyze("src/Test" + extension, extension, content);

    // ─── Line classification ─────────────────────────────────────────────────

    [Fact]
    public void SeparatesCode_CommentAndBlankLines()
    {
        var metrics = Analyze("""
            using System;

            // a note
            var x = 1;
            """);

        metrics.CodeLines.Should().Be(2);
        metrics.CommentLines.Should().Be(1);
        metrics.BlankLines.Should().Be(1);
    }

    [Fact]
    public void TrailingComment_LeavesTheLineAsCode()
    {
        var metrics = Analyze("var x = 1; // why\n");

        metrics.CodeLines.Should().Be(1);
        metrics.CommentLines.Should().Be(0);
    }

    [Fact]
    public void BlockComment_CountsEveryLineItSpans()
    {
        var metrics = Analyze("""
            /* one
               two
               three */
            var x = 1;
            """);

        metrics.CommentLines.Should().Be(3);
        metrics.CodeLines.Should().Be(1);
    }

    // ─── Complexity ──────────────────────────────────────────────────────────

    [Fact]
    public void ComplexityStartsAtOne_ForAFileWithNoBranches()
    {
        Analyze("var x = 1;\nvar y = 2;\n").CyclomaticComplexity.Should().Be(1);
    }

    [Fact]
    public void CountsBranchesAndShortCircuitOperators()
    {
        var metrics = Analyze("""
            if (a && b) { return 1; }
            foreach (var x in xs) { }
            """);

        // 1 baseline + if + && + foreach
        metrics.CyclomaticComplexity.Should().Be(4);
    }

    /// <summary>
    /// The failure that makes a text-based complexity metric untrustworthy: counting keywords in
    /// prose. A file of commented-out code would otherwise rank as the most complex in the
    /// repository.
    /// </summary>
    [Fact]
    public void IgnoresBranchKeywordsInsideComments()
    {
        var withComments = Analyze("""
            // if we ever need to loop for each item, while holding the lock
            /* if (a && b) { foreach (var x in xs) { } } */
            var x = 1;
            """);

        withComments.CyclomaticComplexity.Should().Be(1, "commented-out branches are not branches");
    }

    [Fact]
    public void UsesPythonKeywords_ForPythonFiles()
    {
        var metrics = Analyze("if a and b:\n    pass\nelif c:\n    pass\n", ".py");

        // 1 baseline + if + and + elif
        metrics.CyclomaticComplexity.Should().Be(4);
    }

    [Fact]
    public void MarkupHasNoComplexity_BeyondTheBaseline()
    {
        var metrics = Analyze("<div class=\"if-for-while\">text</div>\n", ".html");

        metrics.CyclomaticComplexity.Should().Be(1, "markup has no control flow to count");
    }

    // ─── Nesting ─────────────────────────────────────────────────────────────

    [Fact]
    public void NestingIsTheDeepestIndentReached()
    {
        var metrics = Analyze("""
            class C {
                void M() {
                    if (x) {
                        Do();
                    }
                }
            }
            """);

        metrics.NestingDepth.Should().Be(3, "four-space indents, deepest line is three levels in");
    }

    [Fact]
    public void ATabCountsAsOneLevel_NotFour()
    {
        var spaces = Analyze("class C {\n    void M() {\n        Do();\n    }\n}\n");
        var tabs = Analyze("class C {\n\tvoid M() {\n\t\tDo();\n\t}\n}\n");

        tabs.NestingDepth.Should().Be(spaces.NestingDepth,
            "indent style must not change the measured structure");
    }

    // ─── Debt and line length ────────────────────────────────────────────────

    [Theory]
    [InlineData("// TODO: fix\n", 1)]
    [InlineData("// FIXME and HACK\n", 2)]
    [InlineData("var todoList = 1;\n", 0)]
    public void CountsDebtMarkersOnWordBoundaries(string content, int expected)
    {
        Analyze(content).DebtMarkers.Should().Be(expected);
    }

    [Fact]
    public void CountsLinesOverTheLengthLimit()
    {
        var longLine = new string('x', 121);
        var metrics = Analyze($"var a = 1;\nvar b = \"{longLine}\";\n");

        metrics.LongLines.Should().Be(1);
    }

    [Fact]
    public void EmptyFile_MeasuresAsNothing()
    {
        var metrics = Analyze(string.Empty);

        metrics.CodeLines.Should().Be(0);
        metrics.CommentLines.Should().Be(0);
        metrics.CyclomaticComplexity.Should().Be(1, "the baseline path exists even in an empty file");
    }

    /// <summary>
    /// CSS has no line-comment syntax, so a URL must survive intact — the same defect the line
    /// counter had, and it would land here as a comment line that is really code.
    /// </summary>
    [Fact]
    public void Css_DoesNotTreatDoubleSlashAsAComment()
    {
        var metrics = Analyze("@import url(https://x.example/a.css);\n", ".css");

        metrics.CodeLines.Should().Be(1);
        metrics.CommentLines.Should().Be(0);
    }
}
