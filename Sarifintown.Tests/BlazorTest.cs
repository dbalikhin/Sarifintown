using System.Reflection;
using System.Runtime.Versioning;
using FluentAssertions;
using NUnit.Framework;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
// Playwright e2e tests are disabled in this run to avoid launching browsers

namespace Sarifintown.Tests;

[TestFixture]
[Category("Playwright")]
[Explicit("Run Playwright e2e tests on demand")]
public class BlazorTest : PageTest
{
    private IHost? _appHost;
    private string? _appUrl;

    [OneTimeSetUp]
    public async Task OneTimeSetup()
    {
        var builder = WebApplication.CreateBuilder();

        var configuration = builder.Configuration;
        var isDebug = configuration.GetValue<string>("DOTNET_ENVIRONMENT") == "Development"
            || builder.Environment.IsDevelopment();

        var buildConfigFolder = isDebug ? "Debug" : "Release";

        var dotNetVersion = GetTargetFrameworkMoniker();
        var solutionRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
        var webRootPath = Path.Combine(solutionRoot, "Sarifintown", "bin", buildConfigFolder, dotNetVersion, "wwwroot");

        if (!Directory.Exists(webRootPath))
        {
            throw new DirectoryNotFoundException($"Blazor WebAssembly assets not found at '{webRootPath}'. Build the app before running tests.");
        }

        builder.Environment.WebRootPath = webRootPath;

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");

        _appHost = app;
        await _appHost.StartAsync();

        _appUrl = _appHost.Services.GetServerAddresses().FirstOrDefault();
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        if (_appHost != null)
        {
            await _appHost.StopAsync();
            _appHost.Dispose();
        }
    }

    [Test]
    public async Task VisitAllPages()
    {
        var baseUrl = _appUrl ?? throw new InvalidOperationException("App url not set");

        await Page.GotoAsync(baseUrl);
        Page.Url.Should().StartWith(baseUrl);

        await Page.GotoAsync($"{baseUrl}/analysis");
        Page.Url.Should().Contain("/analysis");

        await Page.GotoAsync($"{baseUrl}/settings");
        Page.Url.Should().Contain("/settings");
    }

    private static string GetTargetFrameworkMoniker()
    {
        var attribute = typeof(Sarifintown.Program).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()
            ?? throw new InvalidOperationException("Target framework could not be determined.");

        var framework = new FrameworkName(attribute.FrameworkName);
        return $"net{framework.Version!.Major}.{framework.Version.Minor}";
    }
}

// Helper extension method to get server addresses
public static class IHostExtensions
{
    public static IEnumerable<string> GetServerAddresses(this IServiceProvider services)
    {
        var server = services.GetService<Microsoft.AspNetCore.Hosting.Server.IServer>();
        var addressFeature = server?.Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();
        return addressFeature?.Addresses ?? Enumerable.Empty<string>();
    }
}