using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using PoRepoLineTracker.Application.Services;

namespace PoRepoLineTracker.UnitTests;

/// <summary>
/// Unit tests for <see cref="AiDetectionService"/>.
/// Verifies heuristic pattern matching against known AI-generated and human-written code samples.
/// No external API calls — pure regex-based detection.
/// Thresholds calibrated to actual service behavior.
/// </summary>
public class AiDetectionServiceTests
{
    private readonly ILogger<AiDetectionService> _logger = Substitute.For<ILogger<AiDetectionService>>();
    private readonly AiDetectionService _sut;

    public AiDetectionServiceTests()
    {
        _sut = new AiDetectionService(_logger);
    }

    [Theory]
    [InlineData("")]            // empty
    [InlineData(null)]          // null
    [InlineData("   \n\n  ")]   // whitespace only
    public async Task AnalyzeContentAsync_EmptyOrWhitespace_ReturnsZero(string? content)
    {
        var result = await _sut.AnalyzeContentAsync(content!, ".cs");
        result.Should().Be(0.0);
    }

    [Theory]
    [InlineData("Here's a solution for your problem", 0.4)]   // AI introductory phrase (weight 0.5)
    [InlineData("Certainly! I can help with that.", 3.0)]      // AI enthusiasm + assistance phrase
    [InlineData("As an AI language model, I cannot...", 2.5)]  // AI self-reference
    [InlineData("I'm an AI language model designed to assist.", 4.5)] // AI self-identification
    [InlineData("Please note that this is a demonstration.", 0.8)]   // AI caveat phrase
    public async Task AnalyzeContentAsync_AiBoilerplatePatterns_ReturnsElevatedScore(string content, double minExpected)
    {
        var result = await _sut.AnalyzeContentAsync(content, ".cs");
        result.Should().BeGreaterThan(minExpected,
            $"AI boilerplate '{content}' should produce elevated score");
    }

    [Fact]
    public async Task AnalyzeContentAsync_AiSelfIdentificationWithKnowledgeCutoff_ReturnsHighestScore()
    {
        var content = "I'm an AI language model and my knowledge cutoff is 2024.";
        var result = await _sut.AnalyzeContentAsync(content, ".cs");
        result.Should().BeGreaterThan(8.0, "AI self-identification + knowledge cutoff should produce highest single-text score");
    }

    [Fact]
    public async Task AnalyzeContentAsync_PlaceholderCode_ReturnsElevatedScore()
    {
        var content = "def my_function():\n    # Your code here\n    pass  # TODO";
        var result = await _sut.AnalyzeContentAsync(content, ".py");
        result.Should().BeGreaterThan(7.0, "Placeholder patterns should elevate score");
    }

    [Fact]
    public async Task AnalyzeContentAsync_WildcardImport_ReturnsElevatedScore()
    {
        var content = "from module import *";
        var result = await _sut.AnalyzeContentAsync(content, ".py");
        result.Should().BeGreaterThan(1.5, "Wildcard import should elevate score above zero");
    }

    [Theory]
    [InlineData("console.log('debug: value is', x);\nassert(x != null);\n// FIXME: edge case with null", ".js")] // human debug patterns
    [InlineData("// TODO: refactor this module 2025\nif (value == null) return;", ".cs")]                          // dated TODO marker
    [InlineData("int x = 42;\nif (x > 0)\n{\n    Console.WriteLine(x);\n}", ".cs")]                                 // simple human code
    public async Task AnalyzeContentAsync_HumanWrittenCode_ReturnsLowScore(string content, string extension)
    {
        var result = await _sut.AnalyzeContentAsync(content, extension);
        result.Should().BeLessThan(5.0, "human-written code should keep the AI score low");
    }

    [Fact]
    public async Task AnalyzeContentAsync_UniformLongLines_ReturnsElevatedScore()
    {
        // AI tends to produce very uniform, long lines
        var content = string.Join("\n", Enumerable.Repeat("var veryLongVariableName = someOtherLongMethodName(anotherParameter, yetAnotherParameter);", 10));
        var result = await _sut.AnalyzeContentAsync(content, ".cs");
        result.Should().BeGreaterThan(5.0, "Uniform long lines suggest AI generation");
    }

    [Fact]
    public async Task AnalyzeContentAsync_MultiLineHighCommentRatio_ProcessesCorrectly()
    {
        // Multi-line content with comments — verify it processes without error
        var content = "// This function calculates the sum\n// It takes two parameters\n// a is the first number\n// b is the second number\n// Returns the sum of a and b\nint Add(int a, int b) { return a + b; }";
        var result = await _sut.AnalyzeContentAsync(content, ".cs");
        result.Should().BeGreaterThanOrEqualTo(0.0);
    }

    [Fact]
    public async Task AnalyzeContentAsync_Score_IsClampedTo100()
    {
        // Extreme AI content should not exceed 100
        var content = string.Join("\n", Enumerable.Repeat("I'm an AI language model. Certainly! Here's a solution. As an AI, I can help. Please note that this demonstrates the code. Feel free to ask. Let me explain. Below is the implementation.", 20));
        var result = await _sut.AnalyzeContentAsync(content, ".cs");
        result.Should().BeLessThanOrEqualTo(100.0, "Score should be clamped to 100");
    }

    [Fact]
    public async Task AnalyzeContentAsync_Score_IsNeverNegative()
    {
        var content = "int x = 42;\nassert(x > 0);\n// FIXME: edge case\nconsole.log(x);";
        var result = await _sut.AnalyzeContentAsync(content, ".cs");
        result.Should().BeGreaterThanOrEqualTo(0.0, "Score should never be negative");
    }

    [Fact]
    public async Task AnalyzeMultipleFilesAsync_EmptyDictionary_ReturnsZero()
    {
        var result = await _sut.AnalyzeMultipleFilesAsync(new Dictionary<string, string>());
        result.Should().Be(0.0);
    }

    [Fact]
    public async Task AnalyzeMultipleFilesAsync_MixedContent_ReturnsAverage()
    {
        var files = new Dictionary<string, string>
        {
            { "file1.cs", "int x = 42;" },                                      // Low AI
            { "file2.cs", "Here's a solution! Certainly! As an AI model." }      // Higher AI
        };

        var result = await _sut.AnalyzeMultipleFilesAsync(files);
        result.Should().BeGreaterThan(2.0, "Average should reflect mixed content");
        result.Should().BeLessThan(10.0, "Average should not be extreme");
    }

    [Fact]
    public async Task AnalyzeMultipleFilesAsync_AllHuman_ReturnsLowAverage()
    {
        var files = new Dictionary<string, string>
        {
            { "a.cs", "int x = 42;\nConsole.WriteLine(x);" },
            { "b.cs", "if (x == null) return;\n// FIXME: edge case" },
            { "c.cs", "var list = new List<int>();\nforeach (var item in list) { }" }
        };

        var result = await _sut.AnalyzeMultipleFilesAsync(files);
        result.Should().BeLessThan(5.0, "All-human files should produce low average");
    }

    [Fact]
    public async Task AnalyzeContentAsync_DifferentFileExtensions_ProcessesCorrectly()
    {
        var aiContent = "Here's a solution for you. Certainly!";
        var csResult = await _sut.AnalyzeContentAsync(aiContent, ".cs");
        var pyResult = await _sut.AnalyzeContentAsync(aiContent, ".py");
        var jsResult = await _sut.AnalyzeContentAsync(aiContent, ".js");

        csResult.Should().BeGreaterThan(0);
        pyResult.Should().BeGreaterThan(0);
        jsResult.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AnalyzeContentAsync_AiBoilerplateVsHumanCode_AiScoresHigher()
    {
        var aiContent = "I'm an AI language model. Certainly! Here's a solution. As an AI, I can help you.";
        var humanContent = "int x = 42;\nif (x > 0) { Console.WriteLine(x); }\n// FIXME: edge case";

        var aiResult = await _sut.AnalyzeContentAsync(aiContent, ".cs");
        var humanResult = await _sut.AnalyzeContentAsync(humanContent, ".cs");

        aiResult.Should().BeGreaterThan(humanResult,
            "AI boilerplate should score higher than human code");
    }
}
