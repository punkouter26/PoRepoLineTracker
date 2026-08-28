using FluentAssertions;
using PoRepoLineTracker.API.Services;

namespace PoRepoLineTracker.Unit.Services;

/// <summary>
/// The parsed C# metrics — Visual Studio's Code Metrics figures, computed from the syntax tree.
///
/// <para>These pin the arithmetic against hand-written source whose expected values can be counted
/// by eye, which is the whole reason the analyzer takes text rather than a repository.</para>
/// </summary>
public class RoslynMetricsAnalyzerTests
{
    private static RoslynMetricsAccumulator Analyze(string source, string path = "Test.cs")
    {
        var accumulator = new RoslynMetricsAccumulator();
        RoslynMetricsAnalyzer.AnalyzeFile(path, source, accumulator);
        return accumulator;
    }

    // ── Cyclomatic complexity ───────────────────────────────────────────────────────────────

    [Fact]
    public void StraightLineMethod_HasComplexityOfOne()
    {
        var result = Analyze("""
            class C
            {
                int Add(int a, int b) => a + b;
            }
            """);

        result.CyclomaticComplexity.Should().Be(1, "one path through the method is one path");
        result.MemberCount.Should().Be(1);
    }

    [Fact]
    public void EachDecisionPoint_AddsOne()
    {
        var result = Analyze("""
            class C
            {
                int Classify(int n)
                {
                    if (n < 0) return -1;
                    foreach (var _ in new int[0]) { }
                    while (n > 100) n--;
                    return n switch { 0 => 0, _ => 1 };
                }
            }
            """);

        // 1 baseline + if + foreach + while + two switch arms.
        result.CyclomaticComplexity.Should().Be(6);
    }

    /// <summary>
    /// A short-circuiting operator is a branch with no braces around it. Missing these is the
    /// classic way a complexity metric under-reports exactly the dense code it exists to find.
    /// </summary>
    [Fact]
    public void ShortCircuitOperators_CountAsBranches()
    {
        var result = Analyze("""
            class C
            {
                bool Check(string? a, string? b) => a != null && b != null || a == "x";
            }
            """);

        result.CyclomaticComplexity.Should().Be(3, "1 baseline + && + ||");
    }

    [Fact]
    public void NullCoalescing_CountsAsABranch()
    {
        var result = Analyze("""
            class C
            {
                string Get(string? a) => a ?? "fallback";
            }
            """);

        result.CyclomaticComplexity.Should().Be(2);
    }

    // ── Members measured ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Accessors and local functions hold real logic. Excluding them would attribute a branched
    /// getter's complexity to nothing at all.
    /// </summary>
    [Fact]
    public void AccessorsAndLocalFunctions_AreMeasuredAsMembers()
    {
        var result = Analyze("""
            class C
            {
                private int _v;
                public int V
                {
                    get { return _v; }
                    set { _v = value; }
                }

                void Outer()
                {
                    int Inner(int x) => x * 2;
                    Inner(1);
                }
            }
            """);

        // get, set, Outer, Inner.
        result.MemberCount.Should().Be(4);
    }

    // ── Line counts ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SourceLines_ExcludeBlankAndCommentOnlyLines()
    {
        var result = Analyze("""
            // a comment line
            class C
            {

                // another comment

                void M() { }
            }
            """);

        // class C, {, void M() { }, } — the comment-only and blank lines carry no token.
        result.SourceLines.Should().Be(4);
    }

    [Fact]
    public void ExecutableLines_CountStatements_NotBraces()
    {
        var result = Analyze("""
            class C
            {
                void M()
                {
                    var a = 1;
                    var b = 2;
                    System.Console.WriteLine(a + b);
                }
            }
            """);

        result.ExecutableLines.Should().Be(3, "three statements, and a block is not execution");
    }

    // ── Maintainability index ───────────────────────────────────────────────────────────────

    [Fact]
    public void MaintainabilityIndex_IsBetterForSimpleCode_ThanForTangledCode()
    {
        var simple = Analyze("""
            class C
            {
                int Add(int a, int b) => a + b;
            }
            """);

        var tangled = Analyze("""
            class C
            {
                int Mess(int a, int b, int c)
                {
                    if (a > 0 && b > 0) { if (c > 0) { for (var i = 0; i < a; i++) { if (i % 2 == 0 || i % 3 == 0) { a += i; } } } }
                    else if (b < 0) { while (b < 0) { b++; if (b == -1 && a > 2) break; } }
                    return a > b ? a : b < c ? b : c;
                }
            }
            """);

        simple.MaintainabilityIndex.Should().NotBeNull();
        tangled.MaintainabilityIndex.Should().NotBeNull();
        tangled.MaintainabilityIndex!.Value.Should().BeLessThan(simple.MaintainabilityIndex!.Value);
    }

    /// <summary>
    /// The trap this whole analyzer nearly fell into. The published formula was calibrated on ONE
    /// member; fed repository-wide totals, the 0.23 * complexity term alone reaches several hundred
    /// and clamps every repository of any size to 0 — a number that looks like a verdict and is
    /// actually a unit error. Averaging over members is what keeps it in range.
    /// </summary>
    [Fact]
    public void MaintainabilityIndex_IsAveragedPerMember_NotComputedOverWholeFileTotals()
    {
        // Many simple members. Summed, their complexity would drag the index to zero; averaged,
        // each is individually simple and the file scores well.
        var body = string.Join("\n", Enumerable.Range(0, 200)
            .Select(i => $"    int M{i}(int a, int b) => a > b ? a + {i} : b - {i};"));
        var result = Analyze($"class C\n{{\n{body}\n}}");

        result.MemberCount.Should().Be(200);
        result.CyclomaticComplexity.Should().Be(400, "each member is 1 baseline + 1 conditional");

        result.MaintainabilityIndex.Should().NotBeNull();
        result.MaintainabilityIndex!.Value.Should().BeGreaterThan(50,
            "200 individually simple members are maintainable; a total-based formula would report 0");
    }

    [Fact]
    public void MaintainabilityIndex_IsNull_WhenNoMemberWasMeasured()
    {
        var result = Analyze("""
            class C
            {
                public int Value;
            }
            """);

        result.MemberCount.Should().Be(0);
        result.MaintainabilityIndex.Should().BeNull("an average of nothing is not 100");
    }

    [Fact]
    public void MaintainabilityIndex_IsClampedToZero_NeverNegative()
    {
        // The raw formula runs negative for sufficiently awful code; a negative maintainability
        // reads as a bug rather than a verdict.
        RoslynMetricsAnalyzer.MaintainabilityIndex(halsteadVolume: 5_000_000, cyclomaticComplexity: 900, linesOfCode: 4000)
            .Should().Be(0);
    }

    [Fact]
    public void MaintainabilityIndex_HandlesAnEmptyMember_WithoutProducingInfinity()
    {
        // ln(0) is negative infinity. An abstract or empty member legitimately has neither volume
        // nor lines, so the guard has to hold rather than the value racing off to NaN.
        var value = RoslynMetricsAnalyzer.MaintainabilityIndex(halsteadVolume: 0, cyclomaticComplexity: 0, linesOfCode: 0);

        value.Should().Be(100);
        double.IsFinite(value).Should().BeTrue();
    }

    // ── Robustness ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A repository can hold C# that does not parse — a partial checkout, a newer language version
    /// than the analyzer's. One such file must not lose the report for the other nine hundred.
    /// </summary>
    [Fact]
    public void UnparseableFile_IsCountedNotThrown()
    {
        var accumulator = new RoslynMetricsAccumulator();

        var act = () => RoslynMetricsAnalyzer.AnalyzeFile("Broken.cs", "class C { void M( { { {", accumulator);

        act.Should().NotThrow();
    }

    [Fact]
    public void WorstMembers_AreReturnedLeastMaintainableFirst_AndBounded()
    {
        var body = string.Join("\n", Enumerable.Range(0, 60)
            .Select(i => $"    int M{i}(int a) {{ {string.Join(" ", Enumerable.Range(0, i).Select(j => $"if (a == {j}) a++;"))} return a; }}"));
        var result = Analyze($"class C\n{{\n{body}\n}}");

        var worst = result.WorstMembers();

        worst.Should().HaveCount(RoslynMetricsAnalyzer.WorstMemberLimit, "the report is a summary, not a dump");
        worst.Should().BeInAscendingOrder(m => m.MaintainabilityIndex);

        // The most branched member must be in the list — it is the one with an action attached.
        worst[0].CyclomaticComplexity.Should().BeGreaterThan(result.CyclomaticComplexity / 60);
    }

    [Fact]
    public void AccessorMembers_AreNamedForTheirProperty_NotForTheKeywordAlone()
    {
        var result = Analyze("""
            class C
            {
                public int Total
                {
                    get { if (true) return 1; return 0; }
                }
            }
            """);

        result.WorstMembers().Should().ContainSingle()
            .Which.Member.Should().Be("Total (get)", "\"get\" alone does not say which property");
    }
}
