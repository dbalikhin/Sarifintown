using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Protocol;
using Sarifintown.AgentEngine;
using Sarifintown.AgentEngine.Configuration;
using Sarifintown.Core;

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    if (eventArgs.ExceptionObject is Exception exception)
    {
        WriteStartupError("Unhandled exception", exception);
        return;
    }

    WriteStartupError($"Unhandled non-exception error: {eventArgs.ExceptionObject}");
};

TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
{
    WriteStartupError("Unobserved task exception", eventArgs.Exception);
    eventArgs.SetObserved();
};

var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = ConsoleFormatterNames.Simple;
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});
builder.Logging.AddSimpleConsole(options =>
{
    options.TimestampFormat = "HH:mm:ss.fff ";
    options.SingleLine = true;
    options.ColorBehavior = LoggerColorBehavior.Disabled;
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.WebHost.UseUrls("http://127.0.0.1:0");

var discovery = WorkspaceSarifDiscovery.Discover();
var promptCompletionCache = new PromptCompletionCache(discovery.WorkspaceRoot, discovery.SarifFiles);
WriteStartupInfo($"Workspace root: '{discovery.WorkspaceRoot}'");
WriteStartupInfo($"Discovered SARIF files: {discovery.SarifFiles.Count}");

builder.Configuration
    .AddJsonFile(Path.Combine(discovery.WorkspaceRoot, ".sarif", "engine.json"), optional: true)
    .AddEnvironmentVariables(prefix: "SARIFINTOWN_");

builder.Services.Configure<SarifPreloadOptions>(
    builder.Configuration.GetSection(SarifPreloadOptions.SectionName));

// Register Headless Implementations
builder.Services.AddSingleton<IFileReader>(new NativeFileReader(discovery.WorkspaceRoot));
builder.Services.AddSingleton<ITreeSitterEngine, V8TreeSitterEngine>();
builder.Services.AddSingleton<IPromptAssemblyService>(_ => new PromptAssemblyService(discovery.WorkspaceRoot));
builder.Services.AddSingleton<SnippetCacheService>();
builder.Services.AddSingleton<SarifStateService>(serviceProvider =>
    new SarifStateService(
        serviceProvider.GetRequiredService<IFileReader>(),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SarifPreloadOptions>>(),
        discovery.WorkspaceRoot,
        discovery.SarifFiles));
builder.Services.AddSingleton<SnippetWarmupService>(serviceProvider =>
    new SnippetWarmupService(
        serviceProvider.GetRequiredService<SarifStateService>(),
        serviceProvider.GetRequiredService<IFileReader>(),
        serviceProvider.GetRequiredService<ITreeSitterEngine>(),
        serviceProvider.GetRequiredService<SnippetCacheService>(),
        discovery.WorkspaceRoot,
        serviceProvider.GetRequiredService<ILogger<SnippetWarmupService>>()));
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<SnippetWarmupService>());

// Register MCP Server (if using the prerelease SDK)
builder.Services.AddMcpServer()
       .WithStdioServerTransport()
       .WithToolsFromAssembly()
       .WithPromptsFromAssembly()
       .WithCompleteHandler((request, cancellationToken) =>
           HandleCompletionRequestAsync(request.Params, cancellationToken, promptCompletionCache));

var app = builder.Build();
WriteStartupInfo("Web application built.");

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");

// Ensure TreeSitter is initialized before accepting AI requests
var treeSitter = app.Services.GetRequiredService<ITreeSitterEngine>();
await RunStartupStageAsync("TreeSitter initialization", () => treeSitter.InitializeAsync());

var sarifStateService = app.Services.GetRequiredService<SarifStateService>();
await RunStartupStageAsync("SARIF state initialization", () => sarifStateService.InitializeAsync());

var snippetCacheService = app.Services.GetRequiredService<SnippetCacheService>();
var snippetWarmupService = app.Services.GetRequiredService<SnippetWarmupService>();
var preloadOptions = app.Services.GetRequiredService<IOptions<SarifPreloadOptions>>().Value;

// Inject dependencies into SarifTools
SarifTools.FileReader = app.Services.GetRequiredService<IFileReader>();
SarifTools.TreeSitterEngine = treeSitter;
SarifTools.StateService = sarifStateService;
SarifTools.SnippetCache = snippetCacheService;
SarifTools.SnippetWarmupService = snippetWarmupService;
SarifTools.SetDiscoveredSarifFiles(discovery.SarifFiles);
SarifTools.SetLocalUiBaseUrl(string.Empty);
SarifTools.SetWorkspaceRoot(discovery.WorkspaceRoot);
WriteStartupInfo("MCP tool dependencies configured.");

await RunStartupStageAsync("Web host start", () => app.StartAsync());

if (preloadOptions.EnableSnippetPreload)
{
    _ = RunSnippetPreloadInBackgroundAsync(
        snippetWarmupService,
        preloadOptions.MaxPreloadedSnippets,
        app.Lifetime.ApplicationStopping);
    WriteStartupInfo($"Snippet preload ({preloadOptions.MaxPreloadedSnippets} max): scheduled in background");
}

static async ValueTask<CompleteResult> HandleCompletionRequestAsync(
    CompleteRequestParams request,
    CancellationToken cancellationToken,
    PromptCompletionCache completionCache)
{
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(completionCache);

    var requestElement = JsonSerializer.SerializeToElement(request);
    if (!TryGetProperty(requestElement, "ref", out var referenceElement)
        || !TryGetProperty(referenceElement, "name", out var promptNameElement)
        || promptNameElement.ValueKind != JsonValueKind.String)
    {
        return CreateCompletionResult(Array.Empty<string>());
    }

    var promptName = promptNameElement.GetString()?.Trim();
    if (string.IsNullOrWhiteSpace(promptName)
        || !TryGetProperty(requestElement, "argument", out var argumentElement)
        || !TryGetProperty(argumentElement, "name", out var argumentNameElement)
        || argumentNameElement.ValueKind != JsonValueKind.String)
    {
        return CreateCompletionResult(Array.Empty<string>());
    }

    var argumentName = argumentNameElement.GetString()?.Trim();
    if (string.IsNullOrWhiteSpace(argumentName))
    {
        return CreateCompletionResult(Array.Empty<string>());
    }

    var argumentPrefix = string.Empty;
    if (TryGetProperty(argumentElement, "value", out var valueElement)
        && valueElement.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False)
    {
        argumentPrefix = valueElement.ToString();
    }

    var completionData = await completionCache.GetAsync(cancellationToken);
    var completionValues = ResolveCompletionValues(promptName, argumentName, argumentPrefix, completionData);
    return CreateCompletionResult(completionValues);
}

static IReadOnlyList<string> ResolveCompletionValues(
    string promptName,
    string argumentName,
    string prefix,
    PromptCompletionData completionData)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(promptName);
    ArgumentException.ThrowIfNullOrWhiteSpace(argumentName);
    ArgumentNullException.ThrowIfNull(completionData);

    var normalizedPrompt = promptName.Trim();
    var normalizedArgument = argumentName.Trim();

    IEnumerable<string> candidates = normalizedPrompt switch
    {
        "sarif.triage.list" => normalizedArgument switch
        {
            "severity" => completionData.Severities,
            "rule" => completionData.Rules,
            "file" => completionData.Files,
            "state" => completionData.ListStates,
            "limit" => completionData.Limits,
            _ => Array.Empty<string>()
        },
        "sarif.triage.inspect" => normalizedArgument switch
        {
            "findingId" => completionData.FindingIds,
            "evidenceMode" => completionData.EvidenceModes,
            _ => Array.Empty<string>()
        },
        "sarif.triage.apply" => normalizedArgument switch
        {
            "findingId" => completionData.FindingIds,
            "state" => completionData.DecisionStates,
            "reason" => completionData.Reasons,
            "author" => completionData.Authors,
            _ => Array.Empty<string>()
        },
        "sarif.triage.bulk" => normalizedArgument switch
        {
            "state" => completionData.DecisionStates,
            "reason" => completionData.Reasons,
            "severity" => completionData.Severities,
            "rule" => completionData.Rules,
            "file" => completionData.Files,
            "dryRun" => completionData.DryRunValues,
            "author" => completionData.Authors,
            _ => Array.Empty<string>()
        },
        "sarif.triage.status" => Array.Empty<string>(),
        _ => Array.Empty<string>()
    };

    return ApplyPrefixFilter(candidates, prefix);
}

static IReadOnlyList<string> ApplyPrefixFilter(IEnumerable<string> candidates, string prefix)
{
    ArgumentNullException.ThrowIfNull(candidates);

    var normalizedPrefix = prefix?.Trim() ?? string.Empty;

    return candidates
        .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
        .Where(candidate => normalizedPrefix.Length == 0 || candidate.StartsWith(normalizedPrefix, StringComparison.OrdinalIgnoreCase))
        .Take(50)
        .ToArray();
}

static CompleteResult CreateCompletionResult(IReadOnlyList<string> values)
{
    ArgumentNullException.ThrowIfNull(values);

    var result = new CompleteResult();
    var completionProperty = typeof(CompleteResult).GetProperty("Completion");
    if (completionProperty is null)
    {
        return result;
    }

    var completionInstance = Activator.CreateInstance(completionProperty.PropertyType);
    if (completionInstance is null)
    {
        return result;
    }

    SetPropertyIfExists(completionInstance, "Values", values.ToArray());
    SetPropertyIfExists(completionInstance, "Total", values.Count);
    SetPropertyIfExists(completionInstance, "HasMore", false);
    completionProperty.SetValue(result, completionInstance);

    return result;
}

static void SetPropertyIfExists(object instance, string propertyName, object value)
{
    ArgumentNullException.ThrowIfNull(instance);
    ArgumentException.ThrowIfNullOrWhiteSpace(propertyName);

    var property = instance
        .GetType()
        .GetProperty(propertyName);

    if (property?.CanWrite == true)
    {
        property.SetValue(instance, value);
    }
}

static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
{
    if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
    {
        return true;
    }

    value = default;
    return false;
}

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
WriteStartupInfo($"Local UI base URL: '{localUiBaseUrl ?? string.Empty}'");
WriteStartupInfo("Startup sequence completed. Waiting for MCP traffic.");

await app.WaitForShutdownAsync();

static async Task RunSnippetPreloadInBackgroundAsync(
    SnippetWarmupService snippetWarmupService,
    int maxPreloadedSnippets,
    CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(snippetWarmupService);

    try
    {
        await RunStartupStageAsync(
            $"Snippet preload ({maxPreloadedSnippets} max)",
            () => snippetWarmupService.PreloadSnippetsAsync(maxPreloadedSnippets, cancellationToken));
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        WriteStartupInfo("Snippet preload canceled during shutdown.");
    }
    catch (InvalidOperationException exception)
    {
        WriteStartupError("Snippet preload failed due to invalid state.", exception);
    }
    catch (IOException exception)
    {
        WriteStartupError("Snippet preload failed due to I/O error.", exception);
    }
    catch (UnauthorizedAccessException exception)
    {
        WriteStartupError("Snippet preload failed due to access restrictions.", exception);
    }
}

static async Task RunStartupStageAsync(string stageName, Func<Task> stageAction)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(stageName);
    ArgumentNullException.ThrowIfNull(stageAction);

    var stopwatch = Stopwatch.StartNew();
    WriteStartupInfo($"{stageName}: start");

    try
    {
        await stageAction();
        WriteStartupInfo($"{stageName}: completed in {stopwatch.ElapsedMilliseconds} ms");
    }
    catch (Exception exception)
    {
        WriteStartupError($"{stageName}: failed after {stopwatch.ElapsedMilliseconds} ms", exception);
        throw;
    }
}

static void WriteStartupInfo(string message)
{
    if (string.IsNullOrWhiteSpace(message))
    {
        return;
    }

    WriteStartupError($"[INFO] {message}");
}

static void WriteStartupError(string message, Exception? exception = null)
{
    if (string.IsNullOrWhiteSpace(message))
    {
        return;
    }

    try
    {
        Console.Error.WriteLine($"[sarifintown-mcp] {DateTimeOffset.UtcNow:O} {message}");
        if (exception is not null)
        {
            Console.Error.WriteLine(exception.ToString());
        }
    }
    catch
    {
        // best-effort logging only; avoid failing startup because stderr is unavailable
    }
}
