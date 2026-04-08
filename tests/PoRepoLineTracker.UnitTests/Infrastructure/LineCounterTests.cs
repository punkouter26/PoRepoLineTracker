using System.Text;
using FluentAssertions;
using PoRepoLineTracker.Application.Services.LineCounters;

namespace PoRepoLineTracker.UnitTests;

// CSharpLineCounter was removed: its comment/blank-line exclusion produced inconsistent counts
// compared to all other file types. DefaultLineCounter now handles .cs files uniformly.
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
        result.Should().Be(3);
    }

    [Fact]
    public async Task CountLinesAsync_CountsCommentLines()
    {
        var content = "// this is a comment\nreal code\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(2);
    }

    [Fact]
    public async Task CountLinesAsync_SingleLine_ReturnsOne()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("single line"));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(1);
    }

    [Fact]
    public async Task CountLinesAsync_CsFile_CountsAllLines()
    {
        var code = "using System;\nnamespace Test\n{\n    public class Foo { }\n}\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(code));
        var result = await _sut.CountLinesAsync(stream);
        result.Should().Be(5);
    }
}
