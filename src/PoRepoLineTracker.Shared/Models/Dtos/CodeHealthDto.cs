using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Shared.Models.Dtos;

/// <summary>
/// A repository's maintainability report, measured at its most recent analysed commit.
///
/// <para><b>What the score is.</b> A weighted composite of six line-oriented factors, each scored
/// 0–100 and each a well-understood proxy for maintainability: branch density, nesting depth, file
/// size, comment ratio, debt markers and line length. It is deliberately NOT presented as Visual
/// Studio's Maintainability Index, which is derived from Halstead volume and a real control-flow
/// graph — that needs a parser per language and, for C#, a resolved compilation. This app reads
/// blobs out of the git object store across a dozen languages and builds nothing, so those inputs
/// do not exist here.</para>
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
