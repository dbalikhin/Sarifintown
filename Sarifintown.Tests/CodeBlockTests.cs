using Bunit;
using FluentAssertions;
using NUnit.Framework;
using Sarifintown.Pages;

namespace Sarifintown.Tests;

[TestFixture]
[NonParallelizable]
public class CodeBlockTests : BunitTestContext
{
    [Test]
    public void CodeBlock_RendersPreElementWithLanguageClass()
    {
        var cut = Render<CodeBlock>(p => p
            .Add(c => c.Language, "csharp")
            .Add(c => c.CodeContent, "var x = 1;")
            .Add(c => c.StartLineNumber, 1));

        cut.Find("code").ClassList.Should().Contain("language-csharp");
    }

    [Test]
    public void CodeBlock_StoryboardLanguage_MapsToXml()
    {
        var cut = Render<CodeBlock>(p => p
            .Add(c => c.Language, "storyboard")
            .Add(c => c.CodeContent, "<xml/>")
            .Add(c => c.StartLineNumber, 1));

        cut.Find("code").ClassList.Should().Contain("language-xml");
    }

    [Test]
    public void CodeBlock_EmptyContent_DoesNotInvokeJS()
    {
        var act = () => Render<CodeBlock>(p => p
            .Add(c => c.Language, "csharp")
            .Add(c => c.CodeContent, "")
            .Add(c => c.StartLineNumber, 1));

        act.Should().NotThrow();
    }

    [Test]
    public void CodeBlock_StartLineNumber_SetAsDataAttribute()
    {
        var cut = Render<CodeBlock>(p => p
            .Add(c => c.Language, "csharp")
            .Add(c => c.CodeContent, "int x;")
            .Add(c => c.StartLineNumber, 42));

        cut.Find("pre").GetAttribute("data-start").Should().Be("42");
    }
}