using System.Text;
using FluentAssertions;
using PoRepoLineTracker.Application.Services.LineCounters;

namespace PoRepoLineTracker.UnitTests;

public class CSharpLineCounterTests
{
    private readonly CSharpLineCounter _sut = new();

    [Fact]
    public void FileExtension_ShouldReturnCs()
    {
        _sut.FileExtension.Should().Be(".cs");
    }

    [Fact]
    public async Task CountLinesAsync_EmptyStream_ReturnsZero()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(""));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(0);
    }

    [Fact]
    public async Task CountLinesAsync_WhitespaceOnlyFile_ReturnsZero()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("   \n\t\n  \n"));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(0);
    }

    [Fact]
    public async Task CountLinesAsync_SkipsCommentOnlyLines()
    {
        var code = "// This is a comment\n// Another comment\n   // Indented comment\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(code));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(0);
    }

    [Fact]
    public async Task CountLinesAsync_CountsCodeLines()
    {
        var code = "using System;\nnamespace Test\n{\n    public class Foo { }\n}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(code));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(5);
    }

    [Fact]
    public async Task CountLinesAsync_MixedCodeAndComments_CountsOnlyCode()
    {
        var code = "// header comment\nusing System;\n\n// another comment\npublic class Foo\n{\n}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(code));
        var result = await _sut.CountLinesAsync(stream);
        // Lines: "using System;", "public class Foo", "{", "}" = 4 code lines
        result.Should().Be(4);
    }

    [Fact]
    public async Task CountLinesAsync_SingleCodeLine_ReturnsOne()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Console.WriteLine(\"hello\");"));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(1);
    }
}

public class DefaultLineCounterTests
{
    private readonly DefaultLineCounter _sut = new();

    [Fact]
    public void FileExtension_ShouldReturnWildcard()
    {
        _sut.FileExtension.Should().Be("*");
    }

    [Fact]
    public async Task CountLinesAsync_EmptyStream_ReturnsZero()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(""));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(0);
    }

    [Fact]
    public async Task CountLinesAsync_CountsAllLines_IncludingBlanks()
    {
        var content = "line 1\n\nline 3\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(3); // line 1, empty line, line 3
    }

    [Fact]
    public async Task CountLinesAsync_CountsCommentLines()
    {
        var content = "// this is a comment\nreal code\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(2); // Default counter counts everything
    }

    [Fact]
    public async Task CountLinesAsync_SingleLine_ReturnsOne()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("single line"));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(1);
    }
}
