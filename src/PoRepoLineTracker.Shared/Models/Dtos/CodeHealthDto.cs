using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// A repository's maintainability report, measured at its most recent analysed commit.
///
/// <para><b>What the score is.</b> A weighted composite of six line-oriented factors, each scored
/// 0–100 and each a well-understood proxy for maintainability: branch density, nesting depth, file
/// size, comment ratio, debt markers and line length. It is NOT Visual Studio's Maintainability
/// Index, and must not be labelled as one — it is an estimate over a dozen languages, none of
/// which are parsed.</para>
///
/// <para><b>For C#, the real thing sits beside it in <see cref="Metrics"/>.</b> The reasoning
/// this comment used to give — that the Maintainability Index needs "Halstead volume and a real
/// control-flow graph", so a resolved compilation — was half wrong, and the half that was wrong
/// is the useful half: Halstead volume is purely LEXICAL, counted from the token stream, and
/// cyclomatic complexity comes straight off the syntax tree. Neither needs symbols, so both are
/// computed for C# with no build. Depth of Inheritance and Class Coupling are the two that really
/// do need a compilation, and those are the two not reported.</para>
///
/// <para><b>How to read it.</b> The ranking is worth more than the absolute number. The score
/// answers "is this repository drifting", the sub-scores answer "in which direction", and
/// <see cref="Hotspots"/> answers the question people actually have — which file to open first.
/// Every figure is arithmetic over the file's own characters; there is no model anywhere in this
/// app.</para>
/// </summary>
public sealed class CodeHealthDto
{
    public RepositoryId RepositoryId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>The commit the report describes — the repository's most recent.</summary>
    public string CommitSha { get; set; } = string.Empty;
    public DateTime CommitDate { get; set; }

    /// <summary>False when the repository has no counted source files to measure.</summary>
    public bool HasData { get; set; }

    /// <summary>Weighted composite of <see cref="Factors"/>, 0–100.</summary>
    public int Score { get; set; }

    /// <summary>A–F, from <see cref="Score"/>. Bands are on the scoring type in Shared.</summary>
    public string Grade { get; set; } = string.Empty;

    // ── What was measured ───────────────────────────────────────────────────────────────────

    public int FilesAnalyzed { get; set; }
    public int CodeLines { get; set; }
    public int CommentLines { get; set; }
    public int BlankLines { get; set; }

    /// <summary>Sum of every file's cyclomatic approximation. See <see cref="CodeHealthFactorDto"/>.</summary>
    public int TotalComplexity { get; set; }

    /// <summary>Debt markers (TODO, FIXME, HACK, XXX, BUG) found anywhere in the source.</summary>
    public int DebtMarkers { get; set; }

    // ── The breakdown ───────────────────────────────────────────────────────────────────────

    /// <summary>One entry per scored factor, in the order the report presents them.</summary>
    public List<CodeHealthFactorDto> Factors { get; set; } = [];

    /// <summary>
    /// The files most worth looking at, worst first. This is the part of the report with a
    /// concrete next action attached to it.
    /// </summary>
    public List<CodeHealthFileDto> Hotspots { get; set; } = [];

    /// <summary>Score per file extension, so a report can say "the TypeScript is fine, the C# is not".</summary>
    public List<CodeHealthLanguageDto> ByLanguage { get; set; } = [];

    /// <summary>
    /// The repository's single health figure, 0-100: the parsed C# and the estimated rest folded
    /// together, weighted by lines. This is the number to rank on — <see cref="Score"/> describes
    /// only the non-C# half and <see cref="CodeMetricsReportDto.MaintainabilityIndex"/> only the
    /// C# half, and showing those two side by side produced visibly contradictory verdicts.
    /// </summary>
    public int OverallScore { get; set; }

    /// <summary>A-F from <see cref="OverallScore"/>.</summary>
    public string OverallGrade { get; set; } = string.Empty;

    /// <summary>
    /// Visual Studio's Code Metrics for the C# in this repository, or null when there is no C#.
    /// Unlike everything above it, these are parsed rather than estimated — see
    /// <see cref="CodeMetricsReportDto"/>.
    /// </summary>
    public CodeMetricsReportDto? Metrics { get; set; }
}

/// <summary>
/// The Code Metrics window's figures for the repository's C#, measured rather than approximated.
///
/// <para><b>How this differs from everything else in the report.</b> The factors above are
/// line-oriented proxies applied to a dozen languages without parsing any of them. These come from
/// a real C# parse, and are the same quantities Visual Studio reports — so unlike the composite
/// score, the absolute numbers here are comparable with what the IDE shows.</para>
///
/// <para><b>Two of the six columns are missing, deliberately.</b> Depth of Inheritance and Class
/// Coupling need a resolved compilation — a base type or a referenced type may live in another
/// assembly, and no syntax tree can follow that. Producing them means restoring and building the
/// repository, which the deployed App Service cannot do and which would execute the measured
/// repository's own code. The four reported here need only the source text.</para>
/// </summary>
public sealed class CodeMetricsReportDto
{
    /// <summary>
    /// 0–100, higher is better; the mean of the repository's members. Null when no C# member was
    /// measured — an average of nothing is not 100.
    /// </summary>
    public int? MaintainabilityIndex { get; set; }

    /// <summary>Sum over every measured member. Unlike the index, this one is meaningfully additive.</summary>
    public int CyclomaticComplexity { get; set; }

    /// <summary>Physical lines carrying at least one token — blank and comment-only lines excluded.</summary>
    public int LinesOfSourceCode { get; set; }

    /// <summary>Lines carrying a statement that actually runs.</summary>
    public int LinesOfExecutableCode { get; set; }

    public int FilesAnalyzed { get; set; }

    /// <summary>Files that did not parse. Non-zero is a fact about the checkout, not a failure.</summary>
    public int FilesSkipped { get; set; }

    /// <summary>Methods, accessors and local functions the index was averaged over.</summary>
    public int MembersMeasured { get; set; }

    /// <summary>Least maintainable members, worst first — the part with an action attached.</summary>
    public List<CodeMetricsMemberDto> LeastMaintainable { get; set; } = [];
}

/// <summary>One member's metrics, for the least-maintainable list.</summary>
public sealed class CodeMetricsMemberDto
{
    public string File { get; set; } = string.Empty;
    public string Member { get; set; } = string.Empty;
    public int MaintainabilityIndex { get; set; }
    public int CyclomaticComplexity { get; set; }
    public int LinesOfCode { get; set; }
}

/// <summary>One scored factor: what it measured, how it did, and how much it counted.</summary>
public sealed class CodeHealthFactorDto
{
    /// <summary>Stable machine-readable key, for the UI to pick an icon or colour by.</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>0–100. Higher is healthier, for every factor.</summary>
    public int Score { get; set; }

    /// <summary>Share of the composite this factor carries, in points out of 100.</summary>
    public int Weight { get; set; }

    /// <summary>The raw measurement behind the score, already formatted (e.g. "18.4 per 100 lines").</summary>
    public string Measurement { get; set; } = string.Empty;

    /// <summary>One sentence on what the factor means and why it is weighted as it is.</summary>
    public string Explanation { get; set; } = string.Empty;
}

/// <summary>One file's contribution, for the hotspot table.</summary>
public sealed class CodeHealthFileDto
{
    public string Path { get; set; } = string.Empty;
    public int CodeLines { get; set; }
    public int CyclomaticComplexity { get; set; }
    public int NestingDepth { get; set; }
    public int DebtMarkers { get; set; }

    /// <summary>0–100, same direction as the repository score: lower means more worth looking at.</summary>
    public int Score { get; set; }

    /// <summary>The single biggest reason this file ranked where it did.</summary>
    public string PrimaryConcern { get; set; } = string.Empty;
}

/// <summary>One language's slice of the report.</summary>
public sealed class CodeHealthLanguageDto
{
    public string Extension { get; set; } = string.Empty;
    public int Files { get; set; }
    public int CodeLines { get; set; }
    public int Score { get; set; }
}

/// <summary>
/// One repository's line on the portfolio code-health page: enough to rank it against the others
/// and nothing more.
///
/// <para><b>Why unmeasured repositories still get a row.</b> The report is computed from the
/// working clone, so a repository that has never been analysed on this host — or whose clone the
/// App Service has since recycled — cannot be scored at all. Dropping those rows would make a page
/// titled "Code Health" quietly disagree with the repository list, and a repository missing from it
/// would be indistinguishable from one that is fine. <see cref="UnmeasuredReason"/> says which it
/// is.</para>
/// </summary>
public sealed class CodeHealthSummaryDto
{
    public RepositoryId RepositoryId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>False when nothing could be measured; <see cref="UnmeasuredReason"/> then explains why.</summary>
    public bool Measured { get; set; }

    /// <summary>Null when <see cref="Measured"/> is true. A sentence, not a code — it is shown as-is.</summary>
    public string? UnmeasuredReason { get; set; }

    /// <summary>Parsed C# maintainability index, or null when the repository holds no C#.</summary>
    public int? MaintainabilityIndex { get; set; }

    /// <summary>
    /// The single health figure this page ranks on, 0-100 — the parsed C# and the estimated rest
    /// folded together and weighted by lines. See <c>CodeHealthScoring.Combine</c>.
    /// </summary>
    public int Score { get; set; }

    /// <summary>A-F from <see cref="Score"/>; empty when nothing could be scored.</summary>
    public string Grade { get; set; } = string.Empty;

    public int CyclomaticComplexity { get; set; }
    public int LinesOfSourceCode { get; set; }

    public string CommitSha { get; set; } = string.Empty;
    public DateTime CommitDate { get; set; }
}

/// <summary>
/// A repository's health month by month.
///
/// <para>Each point is measured at the last commit on or before that month's 1st — the state the
/// code was actually in that morning. Months before the repository's first commit are absent
/// rather than zero, because a zero on this chart reads as "terrible" and not as "not yet".</para>
/// </summary>
public sealed class CodeHealthTrendDto
{
    public RepositoryId RepositoryId { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>Oldest first, so the series plots left to right without the client re-sorting it.</summary>
    public List<CodeHealthTrendPointDto> Points { get; set; } = [];
}

/// <summary>One month on the trend.</summary>
public sealed class CodeHealthTrendPointDto
{
    /// <summary>First day of the month, UTC — the boundary, not the commit's own date.</summary>
    public DateTime Month { get; set; }

    /// <summary>The combined 0-100 figure, the same one the card and the ranking table show.</summary>
    public int Score { get; set; }

    public string Grade { get; set; } = string.Empty;

    /// <summary>Parsed C# index, or null when the commit held no C#.</summary>
    public int? MaintainabilityIndex { get; set; }

    public int CyclomaticComplexity { get; set; }
    public int LinesOfSourceCode { get; set; }

    /// <summary>The commit actually measured, abbreviated — so a surprising point can be traced.</summary>
    public string CommitSha { get; set; } = string.Empty;
    public DateTime CommitDate { get; set; }
}
