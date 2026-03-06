using Microsoft.Extensions.Options;
using Sarifintown.AgentEngine.Configuration;
using Sarifintown.Core;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.Globalization;
using System.Text.Json;

namespace Sarifintown.AgentEngine;

public sealed class SarifStateService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly IFileReader _fileReader;
    private readonly string _workspaceRoot;
    private readonly IReadOnlyList<string> _sarifFiles;
    private readonly SarifPreloadOptions _options;

    private List<TriageFindingEnvelope> _findings = new();
    private TriageStateDocument _triageDocument = new();
    private bool _isInitialized;

    internal SarifStateService(
        IFileReader fileReader,
        IOptions<SarifPreloadOptions> options,
        string workspaceRoot,
        IEnumerable<string> sarifFiles)
    {
        ArgumentNullException.ThrowIfNull(fileReader);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(sarifFiles);

        _fileReader = fileReader;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _sarifFiles = sarifFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _options = options.Value;
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            _triageDocument = await LoadTriageStateDocumentCoreAsync(cancellationToken).ConfigureAwait(false);
            var triageMap = BuildTriageMap(_triageDocument);

            var findings = new List<TriageFindingEnvelope>();
            var filesToLoad = await SelectFilesForInitialLoadAsync(cancellationToken).ConfigureAwait(false);
            foreach (var sarifPath in filesToLoad)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var parsed = await LoadFindingsFromSarifAsync(sarifPath, triageMap, cancellationToken).ConfigureAwait(false);
                findings.AddRange(parsed);
            }

            _findings = findings;
            _isInitialized = true;
        }
        finally
        {
            _stateLock.Release();
        }
    }

    internal async Task<IReadOnlyList<TriageFindingEnvelope>> GetFindingsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _findings.ToList();
        }
        finally
        {
            _stateLock.Release();
        }
    }

    internal async Task<TriageStateDocument> GetTriageStateDocumentAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return CloneDocument(_triageDocument);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    internal async Task SaveTriageStateDocumentAsync(TriageStateDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _triageDocument = CloneDocument(document);
            var triageMap = BuildTriageMap(_triageDocument);
            _findings = _findings
                .Select(finding => finding with { State = ResolveState(triageMap, finding.FindingId) })
                .ToList();

            await SaveTriageStateDocumentCoreAsync(_triageDocument, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _stateLock.Release();
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_isInitialized)
        {
            return;
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> SelectFilesForInitialLoadAsync(CancellationToken cancellationToken)
    {
        if (_sarifFiles.Count == 0 || _options.Strategy == PreloadStrategy.None)
        {
            return Array.Empty<string>();
        }

        var ordered = _sarifFiles
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToList();

        if (_options.Strategy == PreloadStrategy.All)
        {
            return ordered.Select(file => file.FullName).ToList();
        }

        var metadata = new List<SarifFileMetadata>(ordered.Count);
        foreach (var file in ordered)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var toolName = await ResolveToolNameAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            metadata.Add(new SarifFileMetadata(file.FullName, toolName, file.LastWriteTimeUtc));
        }

        return metadata
            .GroupBy(item => item.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(item => item.LastWriteTimeUtc)
                .ThenBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderByDescending(item => item.LastWriteTimeUtc)
            .Select(item => item.FilePath)
            .ToList();
    }

    private async Task<string> ResolveToolNameAsync(string sarifFile, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var content = await _fileReader.ReadFileAsync(sarifFile).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(content))
        {
            return "unknown-tool";
        }

        using var document = JsonDocument.Parse(content);
        if (!document.RootElement.TryGetProperty("runs", out var runsElement)
            || runsElement.ValueKind != JsonValueKind.Array)
        {
            return "unknown-tool";
        }

        foreach (var run in runsElement.EnumerateArray())
        {
            if (!run.TryGetProperty("tool", out var toolElement)
                || toolElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!toolElement.TryGetProperty("driver", out var driverElement)
                || driverElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!driverElement.TryGetProperty("name", out var nameElement)
                || nameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var toolName = nameElement.GetString();
            if (!string.IsNullOrWhiteSpace(toolName))
            {
                return toolName.Trim();
            }
        }

        return "unknown-tool";
    }

    private async Task<IReadOnlyList<TriageFindingEnvelope>> LoadFindingsFromSarifAsync(
        string sarifFile,
        IReadOnlyDictionary<string, TriageStateEntry> triageMap,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(sarifFile))
        {
            return Array.Empty<TriageFindingEnvelope>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        var content = await _fileReader.ReadFileAsync(sarifFile).ConfigureAwait(false);
        var sarifLog = JsonSerializer.Deserialize<SarifLog>(content, JsonOptions);
        if (sarifLog?.Runs == null)
        {
            return Array.Empty<TriageFindingEnvelope>();
        }

        var findings = new List<TriageFindingEnvelope>();

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
                    result.ResultIdentity = SarifTriageIdentityHelper.BuildIdentity(result, run.Tool?.Driver?.Name ?? string.Empty);
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

        return findings;
    }

    private async Task<TriageStateDocument> LoadTriageStateDocumentCoreAsync(CancellationToken cancellationToken)
    {
        var triagePath = GetTriageFilePath();

        if (!File.Exists(triagePath))
        {
            return new TriageStateDocument();
        }

        var json = await File.ReadAllTextAsync(triagePath, cancellationToken).ConfigureAwait(false);
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

    private async Task SaveTriageStateDocumentCoreAsync(TriageStateDocument document, CancellationToken cancellationToken)
    {
        var triagePath = GetTriageFilePath();
        var triageDirectory = Path.GetDirectoryName(triagePath);

        if (string.IsNullOrWhiteSpace(triageDirectory))
        {
            throw new InvalidOperationException("Unable to resolve triage state directory.");
        }

        Directory.CreateDirectory(triageDirectory);
        var json = JsonSerializer.Serialize(document, JsonOptions);
        await File.WriteAllTextAsync(triagePath, json, cancellationToken).ConfigureAwait(false);
    }

    private static TriageStateDocument CloneDocument(TriageStateDocument source)
    {
        return new TriageStateDocument
        {
            SchemaVersion = source.SchemaVersion,
            Entries = source.Entries
                .Select(entry => new TriageStateEntry
                {
                    FindingId = entry.FindingId,
                    State = entry.State,
                    Reason = entry.Reason,
                    Author = entry.Author,
                    UpdatedUtc = entry.UpdatedUtc
                })
                .ToList()
        };
    }

    private static IReadOnlyDictionary<string, TriageStateEntry> BuildTriageMap(TriageStateDocument triageState)
    {
        return triageState.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.FindingId))
            .GroupBy(entry => entry.FindingId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedUtc).First(), StringComparer.Ordinal);
    }

    private static Rule? ResolveRule(IReadOnlyDictionary<string, Rule> rules, Run run, Result result)
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

    private static TriageFindingState ResolveState(IReadOnlyDictionary<string, TriageStateEntry> triageMap, string findingId)
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

    private string GetTriageFilePath()
    {
        return Path.Combine(_workspaceRoot, ".sarif", "triage.json");
    }

    private sealed record SarifFileMetadata(string FilePath, string ToolName, DateTime LastWriteTimeUtc);
}
