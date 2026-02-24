using Bunit;
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

        Assert.That(cut.Find("code").ClassList, Does.Contain("language-csharp"));
    }

    [Test]
    public void CodeBlock_StoryboardLanguage_MapsToXml()
    {
        var cut = Render<CodeBlock>(p => p
            .Add(c => c.Language, "storyboard")
            .Add(c => c.CodeContent, "<xml/>")
            .Add(c => c.StartLineNumber, 1));

        Assert.That(cut.Find("code").ClassList, Does.Contain("language-xml"));
    }

    [Test]
    public void CodeBlock_EmptyContent_DoesNotInvokeJS()
    {
        var act = () => Render<CodeBlock>(p => p
            .Add(c => c.Language, "csharp")
            .Add(c => c.CodeContent, "")
            .Add(c => c.StartLineNumber, 1));

        Assert.DoesNotThrow(() => act());
    }

    [Test]
    public void CodeBlock_StartLineNumber_SetAsDataAttribute()
    {
        var cut = Render<CodeBlock>(p => p
            .Add(c => c.Language, "csharp")
            .Add(c => c.CodeContent, "int x;")
            .Add(c => c.StartLineNumber, 42));

        Assert.That(cut.Find("pre").GetAttribute("data-start"), Is.EqualTo("42"));
    }
}