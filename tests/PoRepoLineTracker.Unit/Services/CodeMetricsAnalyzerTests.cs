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
    public void ClassifiesLines_DistinguishingCodeCommentsAndBlanks()
    {
        var mixed = Analyze("""
            using System;

            // a note
            var x = 1;
            """);
        mixed.CodeLines.Should().Be(2);
        mixed.CommentLines.Should().Be(1);
        mixed.BlankLines.Should().Be(1);

        var trailing = Analyze("var x = 1; // why\n");
        trailing.CodeLines.Should().Be(1);
        trailing.CommentLines.Should().Be(0);

        var block = Analyze("""
            /* one
               two
               three */
            var x = 1;
            """);
        block.CommentLines.Should().Be(3);
        block.CodeLines.Should().Be(1);
    }

    [Fact]
    public void CalculatesComplexity_AcrossLanguagesAndIgnoresComments()
    {
        Analyze("var x = 1;\nvar y = 2;\n").CyclomaticComplexity.Should().Be(1);

        var branches = Analyze("""
            if (a && b) { return 1; }
            foreach (var x in xs) { }
            """);
        branches.CyclomaticComplexity.Should().Be(4);

        var withComments = Analyze("""
            // if we ever need to loop for each item, while holding the lock
            /* if (a && b) { foreach (var x in xs) { } } */
            var x = 1;
            """);
        withComments.CyclomaticComplexity.Should().Be(1, "commented-out branches are not branches");

        var python = Analyze("if a and b:\n    pass\nelif c:\n    pass\n", ".py");
        python.CyclomaticComplexity.Should().Be(4);

        var markup = Analyze("<div class=\"if-for-while\">text</div>\n", ".html");
        markup.CyclomaticComplexity.Should().Be(1, "markup has no control flow to count");
    }

    [Fact]
    public void MeasuresNestingDepth_RegardlessOfIndentStyle()
    {
        var nested = Analyze("""
            class C {
                void M() {
                    if (x) {
                        Do();
                    }
                }
            }
            """);
        nested.NestingDepth.Should().Be(3);

        var spaces = Analyze("class C {\n    void M() {\n        Do();\n    }\n}\n");
        var tabs = Analyze("class C {\n\tvoid M() {\n\t\tDo();\n\t}\n}\n");
        tabs.NestingDepth.Should().Be(spaces.NestingDepth);
    }

    [Fact]
    public void CountsDebtMarkersAndLongLines()
    {
        Analyze("// TODO: fix\n").DebtMarkers.Should().Be(1);
        Analyze("// FIXME and HACK\n").DebtMarkers.Should().Be(2);
        Analyze("var todoList = 1;\n").DebtMarkers.Should().Be(0);

        var longLineMetrics = Analyze($"var a = 1;\nvar b = \"{new string('x', 121)}\";\n");
        longLineMetrics.LongLines.Should().Be(1);
    }

    [Fact]
    public void HandlesSpecialCases_EmptyFilesAndCssUrls()
    {
        var empty = Analyze(string.Empty);
        empty.CodeLines.Should().Be(0);
        empty.CommentLines.Should().Be(0);
        empty.CyclomaticComplexity.Should().Be(1);

        var css = Analyze("@import url(https://x.example/a.css);\n", ".css");
        css.CodeLines.Should().Be(1);
        css.CommentLines.Should().Be(0);
    }
}
