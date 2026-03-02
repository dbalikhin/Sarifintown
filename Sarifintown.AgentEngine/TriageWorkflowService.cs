using Sarifintown.Core;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Sarifintown.AgentEngine;

internal sealed class TriageWorkflowService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly IFileReader _fileReader;
    private readonly ITreeSitterEngine _treeSitterEngine;
    private readonly string _workspaceRoot;
    private readonly IReadOnlyList<string> _sarifFiles;

    public TriageWorkflowService(
        IFileReader fileReader,
        ITreeSitterEngine treeSitterEngine,
        string workspaceRoot,
        IEnumerable<string> sarifFiles)
    {
        ArgumentNullException.ThrowIfNull(fileReader);
        ArgumentNullException.ThrowIfNull(treeSitterEngine);
        ArgumentNullException.ThrowIfNull(workspaceRoot);
        ArgumentNullException.ThrowIfNull(sarifFiles);

        _fileReader = fileReader;
        _treeSitterEngine = treeSitterEngine;
        _workspaceRoot = workspaceRoot;
        _sarifFiles = sarifFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            .Select(finding => new TriageListItem(
                finding.FindingId,
                finding.RuleName,
                finding.FilePath,
                finding.LineNumber,
                finding.Severity,
                finding.PriorityScore,
                finding.State.ToString()))
            .ToList();

        return ordered;
    }

    public async Task<TriageInspectResult?> InspectAsync(string findingId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(findingId))
        {
            throw new ArgumentException("Finding identifier is required.", nameof(findingId));
        }

        var findings = await LoadFindingsAsync(cancellationToken);
        var finding = findings.FirstOrDefault(item => string.Equals(item.FindingId, findingId, StringComparison.Ordinal));

        if (finding == null)
        {
            return null;
        }

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
            var snippet = await ExtractSnippetAsync(resolvedPath, physicalLocation.Region, cancellationToken);

            steps.Add(new TriageInspectStep(
                index + 1,
                physicalLocation.ArtifactLocation?.Uri ?? string.Empty,
                physicalLocation.Region?.StartLine,
                flowLocation.Location?.Message?.Text ?? string.Empty,
                snippet));
        }

        return new TriageInspectResult(
            finding.FindingId,
            finding.Result.RuleId ?? string.Empty,
            finding.RuleName,
            finding.Severity,
            finding.State.ToString(),
            finding.Result.Message?.Text ?? string.Empty,
            finding.RuleDescription,
            finding.Remediation,
            steps);
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
        var triageState = await LoadTriageStateDocumentAsync(cancellationToken);
        var triageMap = triageState.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.FindingId))
            .GroupBy(entry => entry.FindingId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedUtc).First(), StringComparer.Ordinal);

        var findings = new List<TriageFindingEnvelope>();

        foreach (var sarifFile in _sarifFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(sarifFile))
            {
                continue;
            }

            var content = await _fileReader.ReadFileAsync(sarifFile);
            var sarifLog = JsonSerializer.Deserialize<SarifLog>(content, JsonOptions);
            if (sarifLog?.Runs == null)
            {
                continue;
            }

            foreach (var run in sarifLog.Runs)
            {
                var rules = (run.Tool?.Driver?.Rule ?? new List<Rule>())
                    .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
                    .GroupBy(rule => rule.Id, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                foreach (var result in run.Results ?? new List<Result>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(result.ResultIdentity))
                    {
                        result.ResultIdentity = SarifTriageIdentityHelper.BuildIdentity(result);
                    }

                    var findingId = result.ResultIdentity;
                    if (string.IsNullOrWhiteSpace(findingId))
                    {
                        continue;
                    }

                    var rule = ResolveRule(rules, run, result);
                    var severity = ResolveSeverity(result, rule);
                    var state = ResolveState(triageMap, findingId);
                    var filePath = ResolveResultFilePath(run, result);
                    var lineNumber = result.Locations?.FirstOrDefault()?.PhysicalLocation?.Region?.StartLine;
                    var priority = CalculatePriorityScore(severity, result);
                    var ruleName = !string.IsNullOrWhiteSpace(rule?.Name)
                        ? rule!.Name
                        : result.RuleId ?? "UnknownRule";
                    var ruleCategory = ResolveRuleCategory(rule, result);
                    var ruleDescription = rule?.ShortDescription?.Text ?? string.Empty;
                    var remediation = ResolveRemediation(rule, result);

                    findings.Add(new TriageFindingEnvelope(
                        findingId,
                        result,
                        run,
                        sarifFile,
                        severity,
                        state,
                        ruleName,
                        filePath,
                        lineNumber,
                        priority,
                        ruleCategory,
                        ruleDescription,
                        remediation));
                }
            }
        }

        return findings;
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

    private static Rule? ResolveRule(
        IReadOnlyDictionary<string, Rule> rules,
        Run run,
        Result result)
    {
        if (!string.IsNullOrWhiteSpace(result.RuleId)
            && rules.TryGetValue(result.RuleId, out var byRuleId))
        {
            return byRuleId;
        }

        if (result.RuleIndex >= 0
            && run.Tool?.Driver?.Rule != null
            && result.RuleIndex < run.Tool.Driver.Rule.Count)
        {
            return run.Tool.Driver.Rule[result.RuleIndex];
        }

        return result.Rule;
    }

    private static string ResolveSeverity(Result result, Rule? rule)
    {
        if (TryResolveSecuritySeverity(result.Properties, out var fromResultProperties))
        {
            return fromResultProperties;
        }

        if (TryResolveSecuritySeverity(rule?.Properties, out var fromRuleProperties))
        {
            return fromRuleProperties;
        }

        var level = result.Level;
        if (string.IsNullOrWhiteSpace(level))
        {
            level = rule?.DefaultConfiguration?.Level;
        }

        return NormalizeSeverity(level);
    }

    private static bool TryResolveSecuritySeverity(Dictionary<string, object>? properties, out string severity)
    {
        severity = string.Empty;

        if (properties == null || properties.Count == 0)
        {
            return false;
        }

        if (!properties.TryGetValue("security-severity", out var rawValue) || rawValue == null)
        {
            return false;
        }

        if (!TryConvertToDouble(rawValue, out var score))
        {
            return false;
        }

        severity = score switch
        {
            >= 9.0 => "Critical",
            >= 7.0 => "High",
            >= 4.0 => "Medium",
            _ => "Low"
        };

        return true;
    }

    private static bool TryResolveSecuritySeverity(Dictionary<string, JsonElement>? properties, out string severity)
    {
        severity = string.Empty;

        if (properties == null || properties.Count == 0)
        {
            return false;
        }

        if (!properties.TryGetValue("security-severity", out var rawValue))
        {
            return false;
        }

        if (!TryConvertToDouble(rawValue, out var score))
        {
            return false;
        }

        severity = score switch
        {
            >= 9.0 => "Critical",
            >= 7.0 => "High",
            >= 4.0 => "Medium",
            _ => "Low"
        };

        return true;
    }

    private static bool TryConvertToDouble(object value, out double parsed)
    {
        parsed = 0;

        return value switch
        {
            double doubleValue => (parsed = doubleValue) >= 0,
            float floatValue => (parsed = floatValue) >= 0,
            decimal decimalValue => (parsed = (double)decimalValue) >= 0,
            int intValue => (parsed = intValue) >= 0,
            long longValue => (parsed = longValue) >= 0,
            JsonElement element => TryConvertToDouble(element, out parsed),
            string text => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed),
            _ => false
        };
    }

    private static bool TryConvertToDouble(JsonElement element, out double parsed)
    {
        parsed = 0;

        return element.ValueKind switch
        {
            JsonValueKind.Number => element.TryGetDouble(out parsed),
            JsonValueKind.String => double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed),
            _ => false
        };
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

    private static string ResolveRuleCategory(Rule? rule, Result result)
    {
        if (!string.IsNullOrWhiteSpace(result.RuleId))
        {
            return result.RuleId;
        }

        if (!string.IsNullOrWhiteSpace(rule?.Id))
        {
            return rule.Id;
        }

        return "UnknownRule";
    }

    private static string ResolveRemediation(Rule? rule, Result result)
    {
        if (TryResolvePropertyText(result.Properties, "remediation", out var remediationFromResult))
        {
            return remediationFromResult;
        }

        if (TryResolvePropertyText(rule?.Properties, "help", out var remediationFromRuleHelp))
        {
            return remediationFromRuleHelp;
        }

        if (TryResolvePropertyText(rule?.Properties, "recommendation", out var recommendationFromRule))
        {
            return recommendationFromRule;
        }

        return string.Empty;
    }

    private static bool TryResolvePropertyText(Dictionary<string, object>? properties, string key, out string text)
    {
        text = string.Empty;

        if (properties == null || !properties.TryGetValue(key, out var rawValue) || rawValue == null)
        {
            return false;
        }

        text = rawValue switch
        {
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonElement element => element.ToString(),
            _ => rawValue.ToString() ?? string.Empty
        };

        return !string.IsNullOrWhiteSpace(text);
    }

    private static bool TryResolvePropertyText(Dictionary<string, JsonElement>? properties, string key, out string text)
    {
        text = string.Empty;

        if (properties == null || !properties.TryGetValue(key, out var element))
        {
            return false;
        }

        text = element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.ToString();

        return !string.IsNullOrWhiteSpace(text);
    }

    private static TriageFindingState ResolveState(
        IReadOnlyDictionary<string, TriageStateEntry> triageMap,
        string findingId)
    {
        if (!triageMap.TryGetValue(findingId, out var triageEntry)
            || string.IsNullOrWhiteSpace(triageEntry.State))
        {
            return TriageFindingState.Open;
        }

        if (Enum.TryParse<TriageFindingState>(triageEntry.State, true, out var parsedState))
        {
            return parsedState;
        }

        return TriageFindingState.Open;
    }

    private static string ResolveResultFilePath(Run run, Result result)
    {
        var firstLocation = result.Locations?.FirstOrDefault()?.PhysicalLocation?.ArtifactLocation;
        var resolved = FileHelper.ResolveArtifactPath(firstLocation, run);

        if (!string.IsNullOrWhiteSpace(resolved))
        {
            return resolved;
        }

        return firstLocation?.Uri ?? string.Empty;
    }

    private static double CalculatePriorityScore(string severity, Result result)
    {
        var severityWeight = severity switch
        {
            "Critical" => 100,
            "High" => 80,
            "Medium" => 50,
            "Low" => 20,
            _ => 40
        };

        var flowComplexity = result.CodeFlows?
            .SelectMany(codeFlow => codeFlow.ThreadFlows ?? new List<ThreadFlow>())
            .Sum(threadFlow => threadFlow.Locations?.Count ?? 0) ?? 0;

        return severityWeight + (flowComplexity * 5);
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
        var triagePath = GetTriageFilePath();

        if (!File.Exists(triagePath))
        {
            return new TriageStateDocument();
        }

        var json = await File.ReadAllTextAsync(triagePath, cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new TriageStateDocument();
        }

        try
        {
            return JsonSerializer.Deserialize<TriageStateDocument>(json, JsonOptions) ?? new TriageStateDocument();
        }
        catch (JsonException jsonException)
        {
            throw new InvalidOperationException($"Invalid triage state file format at {triagePath}.", jsonException);
        }
    }

    private async Task SaveTriageStateDocumentAsync(TriageStateDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        var triagePath = GetTriageFilePath();
        var triageDirectory = Path.GetDirectoryName(triagePath);

        if (string.IsNullOrWhiteSpace(triageDirectory))
        {
            throw new InvalidOperationException("Unable to resolve triage state directory.");
        }

        Directory.CreateDirectory(triageDirectory);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await File.WriteAllTextAsync(triagePath, json, cancellationToken);
    }

    private string GetTriageFilePath()
    {
        return Path.Combine(_workspaceRoot, ".sarif", "triage.json");
    }

    private string ResolveFindingPath(TriageFindingEnvelope finding, PhysicalLocation.PhysicalLocationArtifactLocation? artifactLocation)
    {
        var resolved = FileHelper.ResolveArtifactPath(artifactLocation, finding.Run);
        if (string.IsNullOrWhiteSpace(resolved))
        {
            resolved = artifactLocation?.Uri ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            return string.Empty;
        }

        if (Path.IsPathRooted(resolved))
        {
            return resolved;
        }

        return Path.Combine(_workspaceRoot, resolved.Replace('/', Path.DirectorySeparatorChar));
    }

    private async Task<string> ExtractSnippetAsync(string sourcePath, Region? region, CancellationToken cancellationToken)
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

        var startLine = Math.Max(0, (region?.StartLine ?? 1) - 1);
        var endLine = Math.Max(startLine, (region?.EndLine ?? startLine + 1) - 1);
        var language = GetLanguageFromExtension(Path.GetExtension(sourcePath));

        cancellationToken.ThrowIfCancellationRequested();

        var extractedMethod = await _treeSitterEngine.ExtractMethodAsync(sourceCode, language, startLine, endLine);
        if (!string.IsNullOrWhiteSpace(extractedMethod) && !extractedMethod.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
        {
            return extractedMethod.Trim();
        }

        var lines = sourceCode.Split('\n');
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        var safeStart = Math.Min(startLine, lines.Length - 1);
        var safeEnd = Math.Min(endLine, lines.Length - 1);
        return string.Join("\n", lines.Skip(safeStart).Take((safeEnd - safeStart) + 1)).Trim();
    }

    private static string GetLanguageFromExtension(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".cs" => "csharp",
            ".js" => "javascript",
            ".ts" => "typescript",
            ".py" => "python",
            ".java" => "java",
            ".cpp" => "cpp",
            ".c" => "c",
            ".go" => "go",
            ".rs" => "rust",
            ".rb" => "ruby",
            ".php" => "php",
            ".html" => "html",
            ".css" => "css",
            ".json" => "json",
            ".xml" => "xml",
            ".yaml" => "yaml",
            ".yml" => "yaml",
            ".md" => "markdown",
            ".sh" => "bash",
            ".ps1" => "powershell",
            ".sql" => "sql",
            _ => "csharp"
        };
    }

    private sealed record TriageFindingEnvelope(
        string FindingId,
        Result Result,
        Run Run,
        string SarifPath,
        string Severity,
        TriageFindingState State,
        string RuleName,
        string FilePath,
        int? LineNumber,
        double PriorityScore,
        string RuleCategory,
        string RuleDescription,
        string Remediation);
}
