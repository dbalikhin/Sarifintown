using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sarifintown.AgentEngine;
using Sarifintown.Core;

var builder = Host.CreateApplicationBuilder(args);

var discovery = WorkspaceSarifDiscovery.Discover();

// Register Headless Implementations
builder.Services.AddSingleton<IFileReader>(new NativeFileReader(discovery.WorkspaceRoot));
builder.Services.AddSingleton<ITreeSitterEngine, V8TreeSitterEngine>();

// Register MCP Server (if using the prerelease SDK)
builder.Services.AddMcpServer()
       .WithStdioServerTransport()
       .WithToolsFromAssembly();

var app = builder.Build();

// Ensure TreeSitter is initialized before accepting AI requests
var treeSitter = app.Services.GetRequiredService<ITreeSitterEngine>();
await treeSitter.InitializeAsync();

// Inject dependencies into SarifTools
SarifTools.FileReader = app.Services.GetRequiredService<IFileReader>();
SarifTools.TreeSitterEngine = treeSitter;
SarifTools.SetDiscoveredSarifFiles(discovery.SarifFiles);

await app.RunAsync();