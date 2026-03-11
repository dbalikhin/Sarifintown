using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sarifintown.Core;
using Sarifintown.Helpers;
using Sarifintown.Models;

namespace Sarifintown.AgentEngine;

internal sealed class SnippetWarmupService : BackgroundService
{
    private static readonly TimeSpan TreeSitterWarmupTimeout = TimeSpan.FromSeconds(2);
    private readonly Channel<string> _findingQueue = Channel.CreateUnbounded<string>();
    private readonly HashSet<string> _queuedFindingIds = new(StringComparer.Ordinal);
    private readonly object _queueLock = new();
    private readonly SemaphoreSlim _preloadLock = new(1, 1);
    private readonly object _preloadStatusLock = new();

    private readonly SarifStateService _stateService;
    private readonly IFileReader _fileReader;
    private readonly ITreeSitterEngine _treeSitterEngine;
    private readonly SnippetCacheService _snippetCache;
    private readonly string _workspaceRoot;
    private readonly ILogger<SnippetWarmupService> _logger;
    private SnippetPreloadState _preloadState = SnippetPreloadState.NotStarted;
    private string _preloadMessage = "not_started";
    private TaskCompletionSource<bool> _preloadCompletion = CreatePreloadCompletionSource();

    internal SnippetWarmupService(
        SarifStateService stateService,
        IFileReader fileReader,
        ITreeSitterEngine treeSitterEngine,
        SnippetCacheService snippetCache,
        string workspaceRoot,
        ILogger<SnippetWarmupService> logger)
    {
        ArgumentNullException.ThrowIfNull(stateService);
        ArgumentNullException.ThrowIfNull(fileReader);
        ArgumentNullException.ThrowIfNull(treeSitterEngine);
        ArgumentNullException.ThrowIfNull(snippetCache);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(logger);

        _stateService = stateService;
        _fileReader = fileReader;
        _treeSitterEngine = treeSitterEngine;
        _snippetCache = snippetCache;
        _workspaceRoot = workspaceRoot;
        _logger = logger;
    }

    internal ValueTask QueueFindingsAsync(IEnumerable<string> findingIds, CancellationToken cancellationToken)
    {
        // Queue warmup is intentionally disabled to avoid V8 concurrency deadlocks.
        return ValueTask.CompletedTask;
    }

    internal Task PreloadSnippetsAsync(int maxFindings, CancellationToken cancellationToken)
    {
        if (maxFindings <= 0)
        {
            return Task.CompletedTask;
        }

        return PreloadCoreAsync(skip: 0, take: maxFindings, cancellationToken);
    }

    internal Task PreloadRemainingSnippetsAsync(int skipFindings, CancellationToken cancellationToken)
    {
        var safeSkip = Math.Max(0, skipFindings);
        return PreloadCoreAsync(skip: safeSkip, take: int.MaxValue, cancellationToken);
    }

    internal SnippetPreloadStatusSnapshot GetPreloadStatus()
    {
        lock (_preloadStatusLock)
        {
            return new SnippetPreloadStatusSnapshot(_preloadState, _preloadMessage);
        }
    }

    internal async Task<SnippetPreloadStatusSnapshot> WaitForPreloadAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(timeout.Ticks);

        var snapshot = GetPreloadStatus();
        if (snapshot.State != SnippetPreloadState.InProgress)
        {
            return snapshot;
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            await _preloadCompletion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // timeout reached; caller can continue with fallback behavior
        }

        return GetPreloadStatus();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Background queue warmup remains disabled to avoid V8 concurrency deadlocks.
        return Task.CompletedTask;
    }

    private async Task PreloadCoreAsync(int skip, int take, CancellationToken cancellationToken)
    {
        await _preloadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetPreloadStatus(SnippetPreloadState.InProgress, "in_progress");
            _preloadCompletion = CreatePreloadCompletionSource();

            var findings = await _stateService.GetFindingsAsync(cancellationToken).ConfigureAwait(false);
            var targets = findings
                .OrderByDescending(item => item.PriorityScore)
                .ThenBy(item => item.FindingId, StringComparer.Ordinal)
                .Skip(skip)
                .Take(take)
                .ToList();

            var failures = 0;
            foreach (var finding in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await WarmFindingAsync(finding, cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                {
                    failures++;
                    _logger.LogWarning(exception, "Snippet preload skipped finding {FindingId}: invalid operation.", finding.FindingId);
                }
                catch (IOException exception)
                {
                    failures++;
                    _logger.LogWarning(exception, "Snippet preload skipped finding {FindingId}: I/O error.", finding.FindingId);
                }
                catch (UnauthorizedAccessException exception)
                {
                    failures++;
                    _logger.LogWarning(exception, "Snippet preload skipped finding {FindingId}: access denied.", finding.FindingId);
                }
            }

            if (failures > 0)
            {
                SetPreloadStatus(SnippetPreloadState.Failed, "failed");
                _preloadCompletion.TrySetResult(false);
                return;
            }

            SetPreloadStatus(SnippetPreloadState.Completed, "completed");
            _preloadCompletion.TrySetResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetPreloadStatus(SnippetPreloadState.Failed, "canceled");
            _preloadCompletion.TrySetCanceled(cancellationToken);
            throw;
        }
        finally
        {
            _preloadLock.Release();
        }
    }

    private void SetPreloadStatus(SnippetPreloadState state, string message)
    {
        lock (_preloadStatusLock)
        {
            _preloadState = state;
            _preloadMessage = message;
        }
    }

    private static TaskCompletionSource<bool> CreatePreloadCompletionSource()
    {
        return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task WarmFindingAsync(string findingId, CancellationToken cancellationToken)
    {
        var findings = await _stateService.GetFindingsAsync(cancellationToken).ConfigureAwait(false);
        var finding = findings.FirstOrDefault(item => string.Equals(item.FindingId, findingId, StringComparison.Ordinal));
        if (finding == null)
        {
            return;
        }
        await WarmFindingAsync(finding, cancellationToken).ConfigureAwait(false);
    }

    private async Task WarmFindingAsync(TriageFindingEnvelope finding, CancellationToken cancellationToken)
    {

        var flowLocations = finding.Result.CodeFlows?
            .SelectMany(flow => flow.ThreadFlows ?? new List<ThreadFlow>())
            .SelectMany(threadFlow => threadFlow.Locations ?? new List<ThreadFlowLocation>())
            .ToList() ?? new List<ThreadFlowLocation>();

        foreach (var flowLocation in flowLocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var physicalLocation = flowLocation.Location?.PhysicalLocation;
            if (physicalLocation == null)
            {
                continue;
            }

            var sourcePath = FileHelper.ResolvePathForWorkspace(physicalLocation.ArtifactLocation, finding.Run, _workspaceRoot);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                continue;
            }

            var startLine = physicalLocation.Region?.StartLine ?? 1;
            var endLine = physicalLocation.Region?.EndLine > 0 ? physicalLocation.Region.EndLine : startLine;
            var cacheKey = TriageWorkflowService.BuildSnippetCacheKey(sourcePath, startLine, endLine, "tree-sitter-method");

            if (_snippetCache.TryGet(cacheKey, out _))
            {
                continue;
            }

            var snippet = await ExtractSnippetAsync(sourcePath, physicalLocation.Region, cancellationToken).ConfigureAwait(false);
            _snippetCache.Set(cacheKey, snippet);
        }
    }

    private async Task<string> ExtractSnippetAsync(string sourcePath, Region? region, CancellationToken cancellationToken)
    {
        var sourceCode = await _fileReader.ReadFileAsync(sourcePath).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return string.Empty;
        }

        var windowStartLine = region?.StartLine ?? 1;
        var windowEndLine = region?.EndLine > 0 ? region.EndLine : windowStartLine;
        var startLine = Math.Max(0, windowStartLine - 1);
        var endLine = Math.Max(startLine, windowEndLine - 1);
        var language = CodeLanguageResolver.GetLanguageFromExtension(Path.GetExtension(sourcePath));

        if (string.IsNullOrWhiteSpace(language))
        {
            return SnippetHelper.ExtractLineWindow(sourceCode, windowStartLine, windowEndLine);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var extractionTask = _treeSitterEngine.ExtractMethodAsync(sourceCode, language, startLine, endLine);
        var timeoutTask = Task.Delay(TreeSitterWarmupTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(extractionTask, timeoutTask).ConfigureAwait(false);

        if (completedTask != extractionTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SnippetHelper.ExtractLineWindow(sourceCode, windowStartLine, windowEndLine);
        }

        var extractedMethod = await extractionTask.ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(extractedMethod)
            && !extractedMethod.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
        {
            return extractedMethod.Trim();
        }

        return SnippetHelper.ExtractLineWindow(sourceCode, windowStartLine, windowEndLine);
    }
}

internal enum SnippetPreloadState
{
    NotStarted = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3
}

internal sealed record SnippetPreloadStatusSnapshot(SnippetPreloadState State, string Message);
