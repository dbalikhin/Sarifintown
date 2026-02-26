using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sarifintown.AgentEngine;
using Sarifintown.Core;

var builder = Host.CreateApplicationBuilder(args);

// Register Headless Implementations
builder.Services.AddSingleton<IFileReader>(new NativeFileReader("C:/Path/To/Source/Code"));
builder.Services.AddSingleton<ITreeSitterEngine, V8TreeSitterEngine>();

// Register your existing Core logic
// e.g., builder.Services.AddTransient<AnalysisHelper>();

// Register MCP Server (if using the prerelease SDK)
builder.Services.AddMcpServer()
       .WithStdioServerTransport()
       .WithToolsFromAssembly();

var app = builder.Build();

var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "ModelContextProtocol");
if (assembly != null)
{
    var type = assembly.GetType("Microsoft.Extensions.DependencyInjection.McpServerBuilderExtensions");
    if (type != null)
    {
        var methods = type.GetMethods().Select(m => m.Name).Distinct().ToList();
        System.IO.File.WriteAllLines("methods.txt", methods);
    }
}
return;

// Ensure TreeSitter is initialized before accepting AI requests
var treeSitter = app.Services.GetRequiredService<ITreeSitterEngine>();
await treeSitter.InitializeAsync();

await app.RunAsync();