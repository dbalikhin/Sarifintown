using Bunit;
using Microsoft.AspNetCore.Components;
using NUnit.Framework;

namespace Sarifintown.Tests;

/// <summary>
/// NUnit base class for bUnit component tests.
/// </summary>
[NonParallelizable]
public abstract class BunitTestContext : IDisposable
{
    private BunitContext? _context;

    [SetUp]
    public void SetUp()
    {
        _context = new BunitContext();
        _context.JSInterop.Mode = JSRuntimeMode.Loose;
    }

    protected BunitJSInterop JSInterop => _context!.JSInterop;

    protected IRenderedComponent<TComponent> Render<TComponent>(
        Action<ComponentParameterCollectionBuilder<TComponent>>? parameters = null)
        where TComponent : IComponent
        => _context!.Render<TComponent>(parameters);


    [TearDown]
    public void TearDown() => Dispose();

    public void Dispose()
    {
        _context?.Dispose();
        _context = null;
    }
}