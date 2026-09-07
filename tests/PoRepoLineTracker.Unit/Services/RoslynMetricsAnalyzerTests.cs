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
    public void CalculatesCyclomaticComplexity_ForStraightLineAndBranchingCode()
    {
        var straight = Analyze("""
            class C
            {
                int Add(int a, int b) => a + b;
            }
            """);
        straight.CyclomaticComplexity.Should().Be(1);
        straight.MemberCount.Should().Be(1);

        var branching = Analyze("""
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
        branching.CyclomaticComplexity.Should().Be(6);

        var shortCircuit = Analyze("""
            class C
            {
                bool Check(string? a, string? b) => a != null && b != null || a == "x";
            }
            """);
        shortCircuit.CyclomaticComplexity.Should().Be(3);

        var nullCoalesce = Analyze("""
            class C
            {
                string Get(string? a) => a ?? "fallback";
            }
            """);
        nullCoalesce.CyclomaticComplexity.Should().Be(2);
    }

    [Fact]
    public void MeasuresMemberCountsAndLineTypes()
    {
        var members = Analyze("""
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
        members.MemberCount.Should().Be(4);

        var sourceLines = Analyze("""
            // a comment line
            class C
            {

                // another comment

                void M() { }
            }
            """);
        sourceLines.SourceLines.Should().Be(4);

        var execLines = Analyze("""
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
        execLines.ExecutableLines.Should().Be(3);
    }

    [Fact]
    public void ComputesMaintainabilityIndex_AccuratelyAndAveragesPerMember()
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

        var body = string.Join("\n", Enumerable.Range(0, 200)
            .Select(i => $"    int M{i}(int a, int b) => a > b ? a + {i} : b - {i};"));
        var averaged = Analyze($"class C\n{{\n{body}\n}}");
        averaged.MemberCount.Should().Be(200);
        averaged.CyclomaticComplexity.Should().Be(400);
        averaged.MaintainabilityIndex.Should().NotBeNull();
        averaged.MaintainabilityIndex!.Value.Should().BeGreaterThan(50);

        var noMembers = Analyze("""
            class C
            {
                public int Value;
            }
            """);
        noMembers.MemberCount.Should().Be(0);
        noMembers.MaintainabilityIndex.Should().BeNull();
    }

    [Fact]
    public void ClampsAndGuardsMaintainabilityIndex_AtBoundaries()
    {
        RoslynMetricsAnalyzer.MaintainabilityIndex(halsteadVolume: 5_000_000, cyclomaticComplexity: 900, linesOfCode: 4000)
            .Should().Be(0);

        var value = RoslynMetricsAnalyzer.MaintainabilityIndex(halsteadVolume: 0, cyclomaticComplexity: 0, linesOfCode: 0);
        value.Should().Be(100);
        double.IsFinite(value).Should().BeTrue();
    }

    [Fact]
    public void HandlesUnparseableFiles_AndRanksWorstMembers()
    {
        var accumulator = new RoslynMetricsAccumulator();
        var act = () => RoslynMetricsAnalyzer.AnalyzeFile("Broken.cs", "class C { void M( { { {", accumulator);
        act.Should().NotThrow();

        var body = string.Join("\n", Enumerable.Range(0, 60)
            .Select(i => $"    int M{i}(int a) {{ {string.Join(" ", Enumerable.Range(0, i).Select(j => $"if (a == {j}) a++;"))} return a; }}"));
        var result = Analyze($"class C\n{{\n{body}\n}}");
        var worst = result.WorstMembers();
        worst.Should().HaveCount(RoslynMetricsAnalyzer.WorstMemberLimit);
        worst.Should().BeInAscendingOrder(m => m.MaintainabilityIndex);
        worst[0].CyclomaticComplexity.Should().BeGreaterThan(result.CyclomaticComplexity / 60);

        var accessor = Analyze("""
            class C
            {
                public int Total
                {
                    get { if (true) return 1; return 0; }
                }
            }
            """);
        accessor.WorstMembers().Should().ContainSingle()
            .Which.Member.Should().Be("Total (get)");
    }
}
