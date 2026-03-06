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

    private readonly SarifStateService _stateService;
    private readonly IFileReader _fileReader;
    private readonly ITreeSitterEngine _treeSitterEngine;
    private readonly SnippetCacheService _snippetCache;
    private readonly string _workspaceRoot;
    private readonly ILogger<SnippetWarmupService> _logger;

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
        ArgumentNullException.ThrowIfNull(findingIds);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var findingId in findingIds)
        {
            if (string.IsNullOrWhiteSpace(findingId))
            {
                continue;
            }

            var shouldQueue = false;
            lock (_queueLock)
            {
                shouldQueue = _queuedFindingIds.Add(findingId);
            }

            if (shouldQueue)
            {
                if (!_findingQueue.Writer.TryWrite(findingId))
                {
                    lock (_queueLock)
                    {
                        _queuedFindingIds.Remove(findingId);
                    }
                }
            }
        }

        return ValueTask.CompletedTask;
    }

    internal async Task PreloadSnippetsAsync(int maxFindings, CancellationToken cancellationToken)
    {
        if (maxFindings <= 0)
        {
            return;
        }

        var findings = await _stateService.GetFindingsAsync(cancellationToken).ConfigureAwait(false);
        var candidates = findings
            .OrderByDescending(finding => finding.PriorityScore)
            .ThenBy(finding => finding.FindingId, StringComparer.Ordinal)
            .Take(maxFindings)
            .ToList();

        foreach (var finding in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WarmFindingAsync(finding, cancellationToken).ConfigureAwait(false);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var findingId in _findingQueue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await WarmFindingAsync(findingId, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (InvalidOperationException exception)
            {
                _logger.LogWarning(exception, "Snippet warmup skipped for finding {FindingId} due to invalid state.", findingId);
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "Snippet warmup skipped for finding {FindingId} due to I/O failure.", findingId);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogWarning(exception, "Snippet warmup skipped for finding {FindingId} due to access restrictions.", findingId);
            }
            finally
            {
                lock (_queueLock)
                {
                    _queuedFindingIds.Remove(findingId);
                }
            }
        }
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
