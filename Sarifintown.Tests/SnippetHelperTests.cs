using FluentAssertions;
using NUnit.Framework;
using Sarifintown.Helpers;
using Sarifintown.Models;

namespace Sarifintown.Tests;

[TestFixture]
public class SnippetHelperTests
{
    private const string SampleCode = "line1\nline2\nline3\nline4\nline5\nline6\nline7\nline8\nline9\nline10";

    [Test]
    public void ExtractCodeSnippet_SingleLine_ReturnsCorrectSnippet()
    {
        var result = SnippetHelper.ExtractCodeSnippet(SampleCode, 5, 1, 5, 5);

        result.Snippet.Should().Be("line5");
        result.StartLine.Should().Be(5);
        result.EndLine.Should().Be(5);
    }

    [Test]
    public void ExtractCodeSnippet_ContextIncludesThreeLinesEachSide()
    {
        var result = SnippetHelper.ExtractCodeSnippet(SampleCode, 5, 1, 5, 5);

        result.VisibleStartLine.Should().Be(2); // 5 - 3
        result.VisibleEndLine.Should().Be(8);   // 5 + 3
    }

    [Test]
    public void ExtractCodeSnippet_NearStart_ClampedToLineOne()
    {
        var result = SnippetHelper.ExtractCodeSnippet(SampleCode, 1, 1, 1, 5);

        result.VisibleStartLine.Should().Be(1);
    }

    [Test]
    public void ExtractCodeSnippet_NearEnd_ClampedToLastLine()
    {
        var result = SnippetHelper.ExtractCodeSnippet(SampleCode, 10, 1, 10, 5);

        result.VisibleEndLine.Should().Be(10);
    }

    [Test]
    public void ExtractCodeSnippet_OutOfRangeStartLine_Throws()
    {
        var act = () => SnippetHelper.ExtractCodeSnippet(SampleCode, 99, 1, 99, 5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void ExtractCodeSnippet_CrLfContent_SplitsCorrectly()
    {
        var crlf = "line1\r\nline2\r\nline3";
        var result = SnippetHelper.ExtractCodeSnippet(crlf, 2, 1, 2, 5);

        result.Snippet.Should().Be("line2");
    }

    [Test]
    public void ExtractCodeSnippet_WithRegion_DelegatesToOverload()
    {
        var region = new Region { StartLine = 3, StartColumn = 1, EndLine = 3, EndColumn = 5 };
        var result = SnippetHelper.ExtractCodeSnippet(SampleCode, region);

        result.Snippet.Should().Be("line3");
    }
}