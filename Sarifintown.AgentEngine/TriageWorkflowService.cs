using Sarifintown.Core;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.Text.RegularExpressions;

namespace Sarifintown.AgentEngine;

internal sealed class TriageWorkflowService
{
    private static readonly TimeSpan TreeSitterExtractionTimeout = TimeSpan.FromSeconds(2);
    private readonly IFileReader _fileReader;
    private readonly ITreeSitterEngine _treeSitterEngine;
    private readonly SarifStateService _stateService;
    private readonly SnippetCacheService? _snippetCache;
    private readonly SnippetWarmupService? _snippetWarmupService;
    private readonly string _workspaceRoot;

    public TriageWorkflowService(
        IFileReader fileReader,
        ITreeSitterEngine treeSitterEngine,
        SarifStateService stateService,
        string workspaceRoot,
        SnippetCacheService? snippetCache = null,
        SnippetWarmupService? snippetWarmupService = null)
    {
        ArgumentNullException.ThrowIfNull(fileReader);
        ArgumentNullException.ThrowIfNull(treeSitterEngine);
        ArgumentNullException.ThrowIfNull(stateService);
        ArgumentNullException.ThrowIfNull(workspaceRoot);

        _fileReader = fileReader;
        _treeSitterEngine = treeSitterEngine;
        _stateService = stateService;
        _snippetCache = snippetCache;
        _snippetWarmupService = snippetWarmupService;
        _workspaceRoot = workspaceRoot;
    }

    public async Task<TriageStatusResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var findings = await LoadFindingsAsync(cancellationToken);

        var severityCounts = findings
            .GroupBy(finding => finding.Severity, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        EnsureSeverityBuckets(severityCounts);

        var ruleCounts = findings
            .GroupBy(finding => finding.RuleCategory, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var truePositiveCount = findings.Count(finding => finding.State == TriageFindingState.TP);
        var falsePositiveCount = findings.Count(finding => finding.State == TriageFindingState.FP);
        var triagedCount = truePositiveCount + falsePositiveCount;
        var openCount = findings.Count - triagedCount;

        return new TriageStatusResult(
            findings.Count,
            severityCounts,
            ruleCounts,
            openCount,
            triagedCount,
            truePositiveCount,
            falsePositiveCount);
    }

    public async Task<IReadOnlyList<TriageListItem>> ListAsync(TriageQueryOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var findings = await LoadFindingsAsync(cancellationToken);
        var filtered = ApplyFilters(findings, options);

        var ordered = filtered
            .OrderByDescending(finding => finding.PriorityScore)
            .ThenBy(finding => finding.FindingId, StringComparer.Ordinal)
            .Take(options.Limit <= 0 ? 10 : options.Limit)
            .ToList();

        if (_snippetWarmupService != null)
        {
            await _snippetWarmupService.QueueFindingsAsync(
                ordered.Select(finding => finding.FindingId),
                cancellationToken).ConfigureAwait(false);
        }

        return ordered
            .Select(finding => new TriageListItem(
                finding.FindingId,
                finding.RuleName,
                finding.FilePath,
                finding.LineNumber,
                finding.Severity,
                finding.PriorityScore,
                finding.State.ToString()))
            .ToList();
    }

    public async Task<TriageInspectResult?> InspectAsync(
        string findingId,
        string evidenceMode = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(findingId))
        {
            throw new ArgumentException("Finding identifier is required.", nameof(findingId));
        }

        var resolvedEvidenceMode = ParseEvidenceMode(evidenceMode);

        var findings = await LoadFindingsAsync(cancellationToken);
        var finding = findings.FirstOrDefault(item => string.Equals(item.FindingId, findingId, StringComparison.Ordinal));

        if (finding == null)
        {
            return null;
        }

        return await BuildInspectResultAsync(finding, resolvedEvidenceMode, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, TriageInspectResult>> InspectManyAsync(
        IEnumerable<string> findingIds,
        string evidenceMode = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(findingIds);

        var orderedIds = findingIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (orderedIds.Count == 0)
        {
            return new Dictionary<string, TriageInspectResult>(StringComparer.Ordinal);
        }

        var resolvedEvidenceMode = ParseEvidenceMode(evidenceMode);
        var findings = await LoadFindingsAsync(cancellationToken);
        var findingsById = findings.ToDictionary(item => item.FindingId, StringComparer.Ordinal);

        var results = new Dictionary<string, TriageInspectResult>(StringComparer.Ordinal);
        foreach (var findingId in orderedIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!findingsById.TryGetValue(findingId, out var finding))
            {
                continue;
            }

            var result = await BuildInspectResultAsync(finding, resolvedEvidenceMode, cancellationToken);
            results[findingId] = result;
        }

        return results;
    }

    private async Task<TriageInspectResult> BuildInspectResultAsync(
        TriageFindingEnvelope finding,
        TriageEvidenceMode resolvedEvidenceMode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finding);

        var steps = new List<TriageInspectStep>();
        var flowLocations = finding.Result.CodeFlows?
            .SelectMany(flow => flow.ThreadFlows ?? new List<ThreadFlow>())
            .SelectMany(threadFlow => threadFlow.Locations ?? new List<ThreadFlowLocation>())
            .ToList() ?? new List<ThreadFlowLocation>();

        for (var index = 0; index < flowLocations.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var flowLocation = flowLocations[index];
            var physicalLocation = flowLocation.Location?.PhysicalLocation;
            if (physicalLocation == null)
            {
                continue;
            }

            var resolvedPath = ResolveFindingPath(finding, physicalLocation.ArtifactLocation);
            var snippet = await ExtractSnippetAsync(resolvedPath, physicalLocation.Region, resolvedEvidenceMode, cancellationToken);

            steps.Add(new TriageInspectStep(
                index + 1,
                physicalLocation.ArtifactLocation?.Uri ?? string.Empty,
                physicalLocation.Region?.StartLine,
                flowLocation.Location?.Message?.Text ?? string.Empty,
                snippet));
        }

        var evidenceBlocks = await BuildEvidenceBlocksAsync(finding, steps, resolvedEvidenceMode, cancellationToken);

        return new TriageInspectResult(
            finding.FindingId,
            finding.Result.RuleId ?? string.Empty,
            finding.RuleName,
            finding.Severity,
            finding.State.ToString(),
            finding.Result.Message?.Text ?? string.Empty,
            finding.RuleDescription,
            finding.Remediation,
            steps,
            ToEvidenceModeValue(resolvedEvidenceMode),
            evidenceBlocks);
    }

    public Task<TriageInspectResult?> InspectAsync(string findingId, CancellationToken cancellationToken = default)
    {
        return InspectAsync(findingId, string.Empty, cancellationToken);
    }

    private async Task<IReadOnlyList<TriageEvidenceBlock>> BuildEvidenceBlocksAsync(
        TriageFindingEnvelope finding,
        IReadOnlyList<TriageInspectStep> steps,
        TriageEvidenceMode mode,
        CancellationToken cancellationToken)
    {
        if (steps.Count == 0)
        {
            return Array.Empty<TriageEvidenceBlock>();
        }

        if (mode == TriageEvidenceMode.LineWindowStrict)
        {
            return steps
                .Select(step => new TriageEvidenceBlock(
                    step.Index,
                    step.Index,
                    step.FilePath,
                    step.StartLine,
                    step.StartLine,
                    ToEvidenceModeValue(mode),
                    new[] { step.Index },
                    step.CodeSnippet))
                .ToList();
        }

        var groupedBlocks = new List<TriageEvidenceBlock>();

        for (var index = 0; index < steps.Count;)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var blockSteps = new List<TriageInspectStep> { steps[index] };
            var nextIndex = index + 1;

            while (nextIndex < steps.Count && ShouldMergeSteps(steps[nextIndex - 1], steps[nextIndex], mode))
            {
                blockSteps.Add(steps[nextIndex]);
                nextIndex++;
            }

            var first = blockSteps[0];
            var last = blockSteps[^1];
            var snippet = await ResolveBlockSnippetAsync(finding, blockSteps, mode, cancellationToken);

            groupedBlocks.Add(new TriageEvidenceBlock(
                first.Index,
                last.Index,
                first.FilePath,
                first.StartLine,
                last.StartLine,
                ToEvidenceModeValue(mode),
                blockSteps.Select(step => step.Index).ToArray(),
                snippet));

            index = nextIndex;
        }

        return groupedBlocks;
    }

    private async Task<string> ResolveBlockSnippetAsync(
        TriageFindingEnvelope finding,
        IReadOnlyList<TriageInspectStep> blockSteps,
        TriageEvidenceMode mode,
        CancellationToken cancellationToken)
    {
        if (blockSteps.Count == 0)
        {
            return string.Empty;
        }

        if (mode == TriageEvidenceMode.TreeSitterMethod)
        {
            return blockSteps[0].CodeSnippet;
        }

        var firstPath = blockSteps[0].FilePath;
        if (string.IsNullOrWhiteSpace(firstPath))
        {
            return blockSteps[0].CodeSnippet;
        }

        var resolvedPath = ResolveFindingPath(
            finding,
            new PhysicalLocation.PhysicalLocationArtifactLocation { Uri = firstPath });

        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            return blockSteps[0].CodeSnippet;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var sourceCode = await _fileReader.ReadFileAsync(resolvedPath);
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return blockSteps[0].CodeSnippet;
        }

        var minLine = blockSteps
            .Where(step => step.StartLine.HasValue)
            .Select(step => step.StartLine!.Value)
            .DefaultIfEmpty(1)
            .Min();

        var maxLine = blockSteps
            .Where(step => step.StartLine.HasValue)
            .Select(step => step.StartLine!.Value)
            .DefaultIfEmpty(minLine)
            .Max();

        return SnippetHelper.ExtractLineWindow(sourceCode, minLine, maxLine);
    }

    private static bool ShouldMergeSteps(TriageInspectStep previous, TriageInspectStep current, TriageEvidenceMode mode)
    {
        if (!string.Equals(previous.FilePath, current.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return mode switch
        {
            TriageEvidenceMode.LineWindowConcatenated => CanMergeByLineDistance(previous.StartLine, current.StartLine),
            TriageEvidenceMode.TreeSitterMethod => CanMergeByMethodSnippet(previous.CodeSnippet, current.CodeSnippet),
            _ => false
        };
    }

    private static bool CanMergeByLineDistance(int? previousLine, int? currentLine)
    {
        if (!previousLine.HasValue || !currentLine.HasValue)
        {
            return false;
        }

        return currentLine.Value - previousLine.Value <= 6;
    }

    private static bool CanMergeByMethodSnippet(string previousSnippet, string currentSnippet)
    {
        if (string.IsNullOrWhiteSpace(previousSnippet)
            || string.IsNullOrWhiteSpace(currentSnippet)
            || previousSnippet.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)
            || currentSnippet.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(previousSnippet, currentSnippet, StringComparison.Ordinal);
    }

    private static TriageEvidenceMode ParseEvidenceMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return TriageEvidenceMode.TreeSitterMethod;
        }

        var normalized = mode.Trim().ToLowerInvariant();
        return normalized switch
        {
            "line-window-strict" or "strict" or "option2.1" => TriageEvidenceMode.LineWindowStrict,
            "line-window-concatenated" or "concatenated" or "option2.2" => TriageEvidenceMode.LineWindowConcatenated,
            "tree-sitter-method" or "tree-sitter" or "option2.3" => TriageEvidenceMode.TreeSitterMethod,
            _ => TriageEvidenceMode.TreeSitterMethod
        };
    }

    private static string ToEvidenceModeValue(TriageEvidenceMode mode)
    {
        return mode switch
        {
            TriageEvidenceMode.LineWindowStrict => "line-window-strict",
            TriageEvidenceMode.LineWindowConcatenated => "line-window-concatenated",
            _ => "tree-sitter-method"
        };
    }

    public async Task<TriageOperationResult> TriageAsync(
        string findingId,
        string state,
        string reason,
        string author,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(findingId))
        {
            throw new ArgumentException("Finding identifier is required.", nameof(findingId));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Triage reason is required.", nameof(reason));
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            throw new ArgumentException("Triage author is required.", nameof(author));
        }

        if (!TryParseDecisionState(state, out var parsedState))
        {
            throw new ArgumentException("State must be TP or FP.", nameof(state));
        }

        var findings = await LoadFindingsAsync(cancellationToken);
        var findingExists = findings.Any(item => string.Equals(item.FindingId, findingId, StringComparison.Ordinal));

        if (!findingExists)
        {
            return new TriageOperationResult(
                false,
                $"Finding not found: {findingId}",
                findingId,
                parsedState.ToString(),
                reason,
                author,
                DateTime.UtcNow);
        }

        var document = await LoadTriageStateDocumentAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var existingIndex = document.Entries.FindIndex(entry =>
            string.Equals(entry.FindingId, findingId, StringComparison.Ordinal));

        var entry = new TriageStateEntry
        {
            FindingId = findingId,
            State = parsedState.ToString(),
            Reason = reason,
            Author = author,
            UpdatedUtc = now
        };

        if (existingIndex >= 0)
        {
            document.Entries[existingIndex] = entry;
        }
        else
        {
            document.Entries.Add(entry);
        }

        await SaveTriageStateDocumentAsync(document, cancellationToken);

        return new TriageOperationResult(
            true,
            "Triage decision saved.",
            findingId,
            parsedState.ToString(),
            reason,
            author,
            now);
    }

    public async Task<TriageBulkResult> TriageBulkAsync(
        string state,
        string reason,
        TriageQueryOptions options,
        bool dryRun,
        string author,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Triage reason is required.", nameof(reason));
        }

        if (string.IsNullOrWhiteSpace(author))
        {
            throw new ArgumentException("Triage author is required.", nameof(author));
        }

        if (!TryParseDecisionState(state, out var parsedState))
        {
            throw new ArgumentException("State must be TP or FP.", nameof(state));
        }

        if (string.IsNullOrWhiteSpace(options.Severity)
            && string.IsNullOrWhiteSpace(options.Rule)
            && string.IsNullOrWhiteSpace(options.File))
        {
            return new TriageBulkResult(
                false,
                "At least one filter is required for bulk triage.",
                0,
                Array.Empty<string>(),
                dryRun);
        }

        var findings = await LoadFindingsAsync(cancellationToken);
        var filtered = ApplyFilters(findings, options)
            .Select(finding => finding.FindingId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (dryRun)
        {
            return new TriageBulkResult(
                true,
                $"Dry run complete. {filtered.Count} findings would be triaged.",
                filtered.Count,
                filtered,
                true);
        }

        var document = await LoadTriageStateDocumentAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var findingId in filtered)
        {
            var entry = new TriageStateEntry
            {
                FindingId = findingId,
                State = parsedState.ToString(),
                Reason = reason,
                Author = author,
                UpdatedUtc = now
            };

            var existingIndex = document.Entries.FindIndex(existing =>
                string.Equals(existing.FindingId, findingId, StringComparison.Ordinal));

            if (existingIndex >= 0)
            {
                document.Entries[existingIndex] = entry;
            }
            else
            {
                document.Entries.Add(entry);
            }
        }

        await SaveTriageStateDocumentAsync(document, cancellationToken);

        return new TriageBulkResult(
            true,
            $"Bulk triage updated {filtered.Count} findings.",
            filtered.Count,
            filtered,
            false);
    }

    private async Task<List<TriageFindingEnvelope>> LoadFindingsAsync(CancellationToken cancellationToken)
    {
        var findings = await _stateService.GetFindingsAsync(cancellationToken);
        return findings.ToList();
    }

    private static IEnumerable<TriageFindingEnvelope> ApplyFilters(IEnumerable<TriageFindingEnvelope> findings, TriageQueryOptions options)
    {
        var filtered = findings;

        if (!string.IsNullOrWhiteSpace(options.Severity))
        {
            var severities = SplitCsv(options.Severity)
                .Select(NormalizeSeverity)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            filtered = filtered.Where(finding => severities.Contains(finding.Severity));
        }

        if (!string.IsNullOrWhiteSpace(options.Rule))
        {
            var ruleFilters = SplitCsv(options.Rule).ToArray();
            filtered = filtered.Where(finding => ruleFilters.Any(rule =>
                string.Equals(finding.Result.RuleId, rule, StringComparison.OrdinalIgnoreCase)
                || string.Equals(finding.RuleName, rule, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(options.File))
        {
            var fileFilters = SplitCsv(options.File).ToArray();
            filtered = filtered.Where(finding => fileFilters.Any(filter => MatchesFileFilter(finding.FilePath, filter)));
        }

        if (!string.IsNullOrWhiteSpace(options.State)
            && Enum.TryParse<TriageFindingState>(options.State, true, out var requiredState))
        {
            filtered = filtered.Where(finding => finding.State == requiredState);
        }

        return filtered;
    }

    private static bool MatchesFileFilter(string filePath, string filter)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(filter))
        {
            return false;
        }

        var normalizedPath = filePath.Replace('\\', '/');
        var normalizedFilter = filter.Replace('\\', '/');

        if (normalizedFilter.Contains('*') || normalizedFilter.Contains('?'))
        {
            var pattern = "^" + Regex.Escape(normalizedFilter)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";

            return Regex.IsMatch(normalizedPath, pattern, RegexOptions.IgnoreCase)
                || Regex.IsMatch(Path.GetFileName(normalizedPath), pattern, RegexOptions.IgnoreCase);
        }

        return normalizedPath.Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(normalizedPath).Contains(normalizedFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitCsv(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item));
    }

    private static string NormalizeSeverity(string? rawSeverity)
    {
        if (string.IsNullOrWhiteSpace(rawSeverity))
        {
            return "Medium";
        }

        var normalized = rawSeverity.Trim().ToLowerInvariant();

        return normalized switch
        {
            "critical" => "Critical",
            "error" => "High",
            "high" => "High",
            "warning" => "Medium",
            "medium" => "Medium",
            "note" => "Low",
            "low" => "Low",
            _ => "Medium"
        };
    }

    private static void EnsureSeverityBuckets(IDictionary<string, int> severityCounts)
    {
        var buckets = new[] { "Critical", "High", "Medium", "Low" };
        foreach (var bucket in buckets)
        {
            if (!severityCounts.ContainsKey(bucket))
            {
                severityCounts[bucket] = 0;
            }
        }
    }

    private bool TryParseDecisionState(string state, out TriageFindingState parsed)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            parsed = TriageFindingState.Open;
            return false;
        }

        var normalized = state.Trim().Replace('-', ' ').Replace('_', ' ');
        normalized = Regex.Replace(normalized, "\\s+", " ");

        if (string.Equals(normalized, "TP", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "TRUE POSITIVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "TRUEPOSITIVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "TRUEPOS", StringComparison.OrdinalIgnoreCase))
        {
            parsed = TriageFindingState.TP;
            return true;
        }

        if (string.Equals(normalized, "FP", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "FALSE POSITIVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "FALSEPOSITIVE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "FALSEPOS", StringComparison.OrdinalIgnoreCase))
        {
            parsed = TriageFindingState.FP;
            return true;
        }

        if (Enum.TryParse<TriageFindingState>(state, true, out parsed)
            && parsed is TriageFindingState.TP or TriageFindingState.FP)
        {
            return true;
        }

        parsed = TriageFindingState.Open;
        return false;
    }

    private async Task<TriageStateDocument> LoadTriageStateDocumentAsync(CancellationToken cancellationToken)
    {
        return await _stateService.GetTriageStateDocumentAsync(cancellationToken);
    }

    private async Task SaveTriageStateDocumentAsync(TriageStateDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        await _stateService.SaveTriageStateDocumentAsync(document, cancellationToken);
    }

    private string ResolveFindingPath(TriageFindingEnvelope finding, PhysicalLocation.PhysicalLocationArtifactLocation? artifactLocation)
    {
        if (artifactLocation == null)
        {
            return string.Empty;
        }

        return FileHelper.ResolvePathForWorkspace(artifactLocation, finding.Run, _workspaceRoot);
    }

    private async Task<string> ExtractSnippetAsync(
        string sourcePath,
        Region? region,
        TriageEvidenceMode evidenceMode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return "Source file unavailable";
        }

        var sourceCode = await _fileReader.ReadFileAsync(sourcePath);
        if (string.IsNullOrWhiteSpace(sourceCode))
        {
            return string.Empty;
        }

        if (evidenceMode is TriageEvidenceMode.LineWindowStrict or TriageEvidenceMode.LineWindowConcatenated)
        {
            var windowStart = region?.StartLine ?? 1;
            var windowEnd = region?.EndLine > 0 ? region.EndLine : windowStart;
            var windowCacheKey = BuildSnippetCacheKey(sourcePath, windowStart, windowEnd, ToEvidenceModeValue(evidenceMode));

            if (_snippetCache != null && _snippetCache.TryGet(windowCacheKey, out var cachedWindowSnippet))
            {
                return cachedWindowSnippet;
            }

            var windowSnippet = SnippetHelper.ExtractLineWindow(sourceCode, windowStart, windowEnd);
            _snippetCache?.Set(windowCacheKey, windowSnippet);

            return windowSnippet;
        }

        var windowStartLine = region?.StartLine ?? 1;
        var windowEndLine = region?.EndLine > 0 ? region.EndLine : windowStartLine;
        var cacheKey = BuildSnippetCacheKey(sourcePath, windowStartLine, windowEndLine, ToEvidenceModeValue(evidenceMode));

        if (_snippetCache != null && _snippetCache.TryGet(cacheKey, out var cachedSnippet))
        {
            return cachedSnippet;
        }

        var startLine = Math.Max(0, windowStartLine - 1);
        var endLine = Math.Max(startLine, windowEndLine - 1);
        var language = CodeLanguageResolver.GetLanguageFromExtension(Path.GetExtension(sourcePath));

        string finalSnippet;
        if (string.IsNullOrWhiteSpace(language))
        {
            finalSnippet = SnippetHelper.ExtractLineWindow(sourceCode, windowStartLine, windowEndLine);
            _snippetCache?.Set(cacheKey, finalSnippet);
            return finalSnippet;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var extractionTask = _treeSitterEngine.ExtractMethodAsync(sourceCode, language, startLine, endLine);
        var timeoutTask = Task.Delay(TreeSitterExtractionTimeout, cancellationToken);
        var completedTask = await Task.WhenAny(extractionTask, timeoutTask);

        if (completedTask != extractionTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
            finalSnippet = SnippetHelper.ExtractLineWindow(sourceCode, windowStartLine, windowEndLine);
            _snippetCache?.Set(cacheKey, finalSnippet);
            return finalSnippet;
        }

        var extractedMethod = await extractionTask;
        if (!string.IsNullOrWhiteSpace(extractedMethod) && !extractedMethod.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
        {
            finalSnippet = extractedMethod.Trim();
            _snippetCache?.Set(cacheKey, finalSnippet);
            return finalSnippet;
        }

        finalSnippet = SnippetHelper.ExtractLineWindow(sourceCode, windowStartLine, windowEndLine);
        _snippetCache?.Set(cacheKey, finalSnippet);
        return finalSnippet;
    }

    internal static string BuildSnippetCacheKey(string sourcePath, int startLine, int endLine, string mode)
    {
        var normalizedMode = string.IsNullOrWhiteSpace(mode)
            ? "tree-sitter-method"
            : mode.Trim().ToLowerInvariant();

        var normalizedPath = Path.GetFullPath(sourcePath).Replace('\\', '/');
        if (OperatingSystem.IsWindows())
        {
            normalizedPath = normalizedPath.ToLowerInvariant();
        }

        return string.Join("|", normalizedMode, normalizedPath, startLine, endLine);
    }

}
