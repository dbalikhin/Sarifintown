using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Sarifintown.AgentEngine;
using Sarifintown.Core;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");

var discovery = WorkspaceSarifDiscovery.Discover();

// Register Headless Implementations
builder.Services.AddSingleton<IFileReader>(new NativeFileReader(discovery.WorkspaceRoot));
builder.Services.AddSingleton<ITreeSitterEngine, V8TreeSitterEngine>();

// Register MCP Server (if using the prerelease SDK)
builder.Services.AddMcpServer()
       .WithStdioServerTransport()
       .WithToolsFromAssembly();

var app = builder.Build();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// Ensure TreeSitter is initialized before accepting AI requests
var treeSitter = app.Services.GetRequiredService<ITreeSitterEngine>();
await treeSitter.InitializeAsync();

// Inject dependencies into SarifTools
SarifTools.FileReader = app.Services.GetRequiredService<IFileReader>();
SarifTools.TreeSitterEngine = treeSitter;
SarifTools.SetDiscoveredSarifFiles(discovery.SarifFiles);
SarifTools.SetLocalUiBaseUrl(string.Empty);
SarifTools.SetWorkspaceRoot(discovery.WorkspaceRoot);

await app.StartAsync();

var localUiBaseUrl = app.Urls.FirstOrDefault(url =>
    url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));

if (string.IsNullOrWhiteSpace(localUiBaseUrl))
{
    var server = app.Services.GetRequiredService<IServer>();
    var addressesFeature = server.Features.Get<IServerAddressesFeature>();
    localUiBaseUrl = addressesFeature?.Addresses.FirstOrDefault(url =>
        url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase));
}

SarifTools.SetLocalUiBaseUrl(localUiBaseUrl ?? string.Empty);

await app.WaitForShutdownAsync();
