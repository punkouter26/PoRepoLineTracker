using Microsoft.Extensions.Logging;
using PoRepoLineTracker.Application.Interfaces;
using System.Text.RegularExpressions;

namespace PoRepoLineTracker.Application.Services;

/// <summary>
/// AI Detection Service using heuristic-based pattern matching.
/// Detects common patterns that indicate AI-generated code without using external APIs.
/// </summary>
public class AiDetectionService : IAiDetectionService
{
    private readonly ILogger<AiDetectionService> _logger;

    // Patterns that are more common in AI-generated code
    private static readonly List<AiPattern> AiPatterns = new()
    {
        // Common AI boilerplate comments
        new AiPattern(@"Here's (a|an|the)", 0.5, "AI introductory phrase"),
        new AiPattern(@"Certainly!", 2.0, "AI enthusiasm marker"),
        new AiPattern(@"I (can|will|would) (help|assist|provide)", 1.5, "AI assistance phrase"),
        new AiPattern(@"Below is (a|an|the)", 1.5, "AI introductory phrase"),
        new AiPattern(@"This code (demonstrates|shows|illustrates)", 1.5, "AI explanation phrase"),
        new AiPattern(@"Please note (that|however)", 1.0, "AI caveat phrase"),
        new AiPattern(@"Feel free to (ask|let me know|contact)", 1.0, "AI offer phrase"),
        new AiPattern(@"Let me (know|explain|break down)", 1.0, "AI offer phrase"),
        new AiPattern(@"As an AI", 3.0, "AI self-reference"),
        new AiPattern(@"I'?m an AI language model", 5.0, "AI self-identification"),
        new AiPattern(@"my knowledge cutoff", 4.0, "AI limitation statement"),
        new AiPattern(@"I don'?t have the ability to", 3.0, "AI limitation statement"),
        
        // Common AI code patterns
        new AiPattern(@"TODO: Add implementation", 2.0, "Unimplemented AI code"),
        new AiPattern(@"// TODO", 1.0, "Unimplemented code marker"),
        new AiPattern(@"pass  # TODO", 2.5, "Incomplete AI code"),
        new AiPattern(@"Your code here", 3.0, "AI placeholder"),
        new AiPattern(@"your_code_here", 2.5, "AI placeholder"),
        new AiPattern(@"sample_", 1.0, "Sample naming"),
        new AiPattern(@"example_", 1.0, "Example naming"),
        
        // Overly verbose comments
        new AiPattern(@"The following code", 1.5, "AI transition phrase"),
        new AiPattern(@"In this (example|code snippet|function)", 1.0, "AI explanation"),
        
        // Very long single-line constructs (common in AI output)
        new AiPattern(@"^\s*{.*,.*,.*,.*,.*,.*,.*,.*,.*,.*}.*$", 2.0, "Verbose object/array"),
        
        // Magic numbers without explanation (AI often does this)
        new AiPattern(@"\b42\b", 1.0, "Magic number"),
        new AiPattern(@"\b1337\b", 1.5, "Hacker number"),
        
        // Common AI variable naming patterns
        new AiPattern(@"_\d+", 0.5, "AI numbered variables"),
        new AiPattern(@"temp\d*", 0.5, "AI temp naming"),
        
        // Excessive newlines (AI formatting artifact)
        new AiPattern(@"\n\n\n\n", 1.5, "Excessive spacing"),
        
        // Import everything pattern (AI common)
        new AiPattern(@"from \w+ import \*", 2.0, "Wildcard import"),
        new AiPattern(@"using namespace std", 1.5, "Namespace pollution"),
    };

    // Patterns that are more common in human-written code
    private static readonly List<string> HumanPatterns = new()
    {
        @"//.*\w{2,}.*//",  // Single-line comment with actual content
        @"#ifndef|#define|#pragma",
        @"@property.*\n.*@synthesize",  // ObjC patterns
        @"__init__.*super\(\)",  // Python init patterns
        @"def __init__\(self",  // Python constructor
        @"console\.log\(",  // Debug statements (usually human)
        @"//.*TODO:.*\d{4}",  // Dated TODO (human)
        @"//.*FIXME",  // Human bug markers
        @"//.*HACK",  // Human hack markers
        @"assert\(",  // Human testing
        @"#pragma mark",
        @"@throws",
        @"throws\s+\w+Exception",
        @"SQL injection",
        @"XSS",
        @"sanitize",
        @"null.*check",
        @"edge case",
        @"performance.*concern",
    };

    public AiDetectionService(ILogger<AiDetectionService> logger)
    {
        _logger = logger;
    }

    public async Task<double> AnalyzeContentAsync(string content, string fileExtension)
    {
        return await Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(content))
                return 0.0;

            double aiScore = 0.0;
            int aiMatches = 0;
            int humanMatches = 0;

            // Analyze AI patterns
            foreach (var pattern in AiPatterns)
            {
                try
                {
                    var matches = Regex.Matches(content, pattern.Pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if (matches.Count > 0)
                    {
                        aiScore += pattern.Weight * Math.Min(matches.Count, 3); // Cap at 3 matches per pattern
                        aiMatches += matches.Count;
                        _logger.LogDebug("AI pattern '{Pattern}' matched {Count} times (weight: {Weight})",
                            pattern.Description, matches.Count, pattern.Weight);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    _logger.LogWarning("Regex timeout for pattern: {Pattern}", pattern.Pattern);
                }
            }

            // Analyze human patterns
            foreach (var pattern in HumanPatterns)
            {
                try
                {
                    if (Regex.IsMatch(content, pattern, RegexOptions.IgnoreCase))
                    {
                        humanMatches++;
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    // Skip on timeout
                }
            }

            // Additional heuristics based on code characteristics
            var lines = content.Split('\n');
            var codeLines = lines.Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith("//") && !l.TrimStart().StartsWith("#")).ToList();

            // Check for excessive uniformity (AI tends to be very consistent)
            if (codeLines.Count > 5)
            {
                var avgLineLength = codeLines.Average(l => l.Trim().Length);
                var lineLengthVariance = codeLines.Select(l => Math.Pow(l.Trim().Length - avgLineLength, 2)).Average();

                // Very low variance suggests AI (too uniform)
                if (lineLengthVariance < 5 && avgLineLength > 30)
                {
                    aiScore += 3.0;
                    _logger.LogDebug("High uniformity detected (variance: {Variance}, avg length: {AvgLength})",
                        lineLengthVariance, avgLineLength);
                }

                // Very high average line length suggests AI verbosity
                if (avgLineLength > 100)
                {
                    aiScore += 5.0;
                    _logger.LogDebug("Long average lines detected: {AvgLength}", avgLineLength);
                }
            }

            // Check comment density
            var commentLines = lines.Count(l => l.TrimStart().StartsWith("//") || l.TrimStart().StartsWith("#"));
            var commentRatio = (double)commentLines / Math.Max(lines.Length, 1);

            // Very high comment ratio might indicate AI over-explaining
            if (commentRatio > 0.3 && commentRatio < 0.7)
            {
                aiScore += 2.0;
                _logger.LogDebug("High comment ratio detected: {Ratio:P}", commentRatio);
            }

            // Check for repetitive structures (AI tendency)
            var uniqueLineCount = codeLines.Select(l => l.Trim()).Distinct().Count();
            var repetitionRatio = 1.0 - ((double)uniqueLineCount / Math.Max(codeLines.Count, 1));
            if (repetitionRatio > 0.5)
            {
                aiScore += 3.0;
                _logger.LogDebug("High repetition detected: {Ratio:P}", repetitionRatio);
            }

            // Normalize score based on content length and factors
            var baseScore = aiScore;

            // Reduce score if human patterns found
            baseScore -= humanMatches * 1.5;

            // Normalize to 0-100 scale
            var normalizedScore = Math.Max(0, Math.Min(100, baseScore));

            _logger.LogDebug("AI detection result for {Extension}: {Score}/100 (AI matches: {AiMatches}, Human matches: {HumanMatches})",
                fileExtension, normalizedScore, aiMatches, humanMatches);

            return Math.Round(normalizedScore, 2);
        });
    }

    public async Task<double> AnalyzeMultipleFilesAsync(Dictionary<string, string> files)
    {
        if (files == null || files.Count == 0)
            return 0.0;

        var scores = new List<double>();

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.Key).ToLowerInvariant();
            var score = await AnalyzeContentAsync(file.Value, extension);
            scores.Add(score);
        }

        // Return average score
        var averageScore = scores.Count > 0 ? scores.Average() : 0.0;
        _logger.LogDebug("Multi-file AI detection: {Count} files analyzed, average score: {Score}/100",
            files.Count, averageScore);

        return Math.Round(averageScore, 2);
    }

    private class AiPattern
    {
        public string Pattern { get; }
        public double Weight { get; }
        public string Description { get; }

        public AiPattern(string pattern, double weight, string description)
        {
            Pattern = pattern;
            Weight = weight;
            Description = description;
        }
    }
}
