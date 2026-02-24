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

        Assert.That(result.Snippet, Is.EqualTo("line5"));
        Assert.That(result.StartLine, Is.EqualTo(5));
        Assert.That(result.EndLine, Is.EqualTo(5));
    }

    [Test]
    public void ExtractCodeSnippet_ContextIncludesThreeLinesEachSide()
    {
        var result = SnippetHelper.ExtractCodeSnippet(SampleCode, 5, 1, 5, 5);

        Assert.That(result.VisibleStartLine, Is.EqualTo(2)); // 5 - 3
        Assert.That(result.VisibleEndLine, Is.EqualTo(8));   // 5 + 3
    }

    [Test]
    public void ExtractCodeSnippet_NearStart_ClampedToLineOne()
    {
        var result = SnippetHelper.ExtractCodeSnippet(SampleCode, 1, 1, 1, 5);

        Assert.That(result.VisibleStartLine, Is.EqualTo(1));
    }

    [Test]
    public void ExtractCodeSnippet_NearEnd_ClampedToLastLine()
    {
        var result = SnippetHelper.ExtractCodeSnippet(SampleCode, 10, 1, 10, 5);

        Assert.That(result.VisibleEndLine, Is.EqualTo(10));
    }

    [Test]
    public void ExtractCodeSnippet_OutOfRangeStartLine_Throws()
    {
        var act = () => SnippetHelper.ExtractCodeSnippet(SampleCode, 99, 1, 99, 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => act());
    }

    [Test]
    public void ExtractCodeSnippet_CrLfContent_SplitsCorrectly()
    {
        var crlf = "line1\r\nline2\r\nline3";
        var result = SnippetHelper.ExtractCodeSnippet(crlf, 2, 1, 2, 5);

        Assert.That(result.Snippet, Is.EqualTo("line2"));
    }

    [Test]
    public void ExtractCodeSnippet_WithRegion_DelegatesToOverload()
    {
        var region = new Region { StartLine = 3, StartColumn = 1, EndLine = 3, EndColumn = 5 };
        var result = SnippetHelper.ExtractCodeSnippet(SampleCode, region);

        Assert.That(result.Snippet, Is.EqualTo("line3"));
    }

    [Test]
    public void HighlightSnippet_ValidRegion_ReturnsHighlightedSnippet()
    {
        string fileContent = "line1\nline2\nline3\nline4\nline5";
        var region = new Region { StartLine = 2, StartColumn = 1, EndLine = 4, EndColumn = 5 };

        var result = SnippetHelper.HighlightSnippet(fileContent, region);

        Assert.That(result, Does.Contain("<mark>line2\nline3\nline</mark>4"));
    }
}