using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Domain.Models;

namespace PoRepoLineTracker.Application.Services;

/// <summary>
/// CommitTagger: Algorithmic commit classification service.
/// Assigns descriptive tags to commits based on their characteristics
/// (AI percentage, line counts, diff ratios) without any external API calls.
/// </summary>
public static class CommitTaggerService
{
    /// <summary>
    /// Classifies a commit and returns a list of descriptive tags.
    /// Tags are assigned based on pure algorithmic rules — no external calls.
    /// </summary>
    public static List<string> ClassifyCommit(CommitLineCount commit)
    {
        var tags = new List<string>();

        // ── AI-related tags ──────────────────────────────────────────────────────
        if (commit.AiPercentage >= 80)
            tags.Add("ai-heavy");
        else if (commit.AiPercentage >= 50)
            tags.Add("ai-moderate");
        else if (commit.AiPercentage >= 25)
            tags.Add("ai-light");

        // AI Burst: large commit with high AI percentage
        if (commit.LinesAdded >= 100 && commit.AiPercentage >= 50)
            tags.Add("ai-burst");

        // ── Size-based tags ──────────────────────────────────────────────────────
        if (commit.LinesAdded >= 500)
            tags.Add("hot-streak");
        else if (commit.LinesAdded <= 5 && commit.LinesRemoved <= 5)
            tags.Add("tiny");

        // ── Diff pattern tags ────────────────────────────────────────────────────
        if (commit.LinesRemoved > commit.LinesAdded && commit.LinesAdded > 0)
        {
            // More lines removed than added — likely bug fix or cleanup
            var removalRatio = (double)commit.LinesRemoved / commit.LinesAdded;
            if (removalRatio >= 2.0)
                tags.Add("bug-fix");
        }

        if (commit.LinesAdded > 0 && commit.LinesRemoved > 0)
        {
            // Similar lines added and removed — likely refactor
            var ratio = (double)commit.LinesAdded / commit.LinesRemoved;
            if (ratio is >= 0.8 and <= 1.2 && commit.LinesAdded >= 10)
                tags.Add("refactor");
        }

        // Pure addition (no removals) — new feature or addition
        if (commit.LinesAdded > 20 && commit.LinesRemoved == 0)
            tags.Add("new-code");

        // Pure deletion — cleanup or dead code removal
        if (commit.LinesRemoved > 20 && commit.LinesAdded == 0)
            tags.Add("cleanup");

        return tags;
    }

    /// <summary>
    /// Returns a human-readable display string for a tag.
    /// </summary>
    public static string GetTagDisplay(string tag) => tag switch
    {
        "ai-heavy" => "🤖 AI Heavy",
        "ai-moderate" => "🤖 AI Moderate",
        "ai-light" => "🤖 AI Light",
        "ai-burst" => "🤖 AI Burst",
        "hot-streak" => "🔥 Hot Streak",
        "tiny" => "🔹 Tiny",
        "bug-fix" => "🐛 Bug Fix",
        "refactor" => "🧹 Refactor",
        "new-code" => "✨ New Code",
        "cleanup" => "🧹 Cleanup",
        _ => tag
    };

    /// <summary>
    /// Returns a CSS color class for a tag for chart rendering.
    /// </summary>
    public static string GetTagColor(string tag) => tag switch
    {
        "ai-heavy" or "ai-burst" => "#ef4444",     // red
        "ai-moderate" => "#f97316",                  // orange
        "ai-light" => "#eab308",                     // yellow
        "hot-streak" => "#f59e0b",                   // amber
        "tiny" => "#94a3b8",                         // slate
        "bug-fix" => "#22c55e",                      // green
        "refactor" => "#8b5cf6",                     // violet
        "new-code" => "#3b82f6",                     // blue
        "cleanup" => "#6b7280",                      // gray
        _ => "#6b7280"
    };
}
