using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Playwright.NUnit;

namespace Sarifintown.Tests;

[TestFixture]
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

        // Use Debug or Release folder based on build configuration
        var buildConfigFolder = isDebug ? "Debug" : "Release";

        // TODO: .NET version should not be hardcoded
        var dotNetVersion = "net9.0";
        var webRootPath = Path.GetFullPath($"../../../../Sarifintown/bin/{buildConfigFolder}/{dotNetVersion}/wwwroot");
        builder.Environment.WebRootPath = webRootPath;

        // Let the OS assign a dynamic port
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        var app = builder.Build();

        // This serves all static assets (JS, CSS, WASM, etc.) from the web root.
        app.UseStaticFiles();

        // This handles SPA routing by serving index.html for any unknown paths.
        app.MapFallbackToFile("index.html");

        _appHost = app;
        await _appHost.StartAsync();

        // Get the dynamically assigned URL
        _appUrl = _appHost.Services.GetServerAddresses().First();
       
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
        await Page.GotoAsync(_appUrl!);

        await Page.GotoAsync($"{_appUrl}/analysis");

        await Page.GotoAsync($"{_appUrl}/settings");
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