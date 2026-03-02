using Sarifintown.Models;
using Sarifintown.Helpers;

namespace Sarifintown.Services
{
    public class SarifFileService 
    {
        private List<SarifFile> _sarifFiles = new();

        private Dictionary<string, Rule> _ruleDictionary = new Dictionary<string, Rule>();

        public bool AddSarifFile(SarifFile sarifFile, int jsDirectoryId = 0)
        {
            if (!_sarifFiles.Contains(sarifFile))
            {
                foreach (var run in sarifFile.SarifLog.Runs ?? Enumerable.Empty<Run>())
                {
                    run.JSDirectoryId = jsDirectoryId;
                }

                AddFilenamePathAndExt(sarifFile.SarifLog);
                UseRuleDictionary(sarifFile.SarifLog);
                CalculateLevelStats(sarifFile.SarifLog);
                CalculateUniqueRulesInResults(sarifFile.SarifLog);
                BuildRunIndexes(sarifFile.SarifLog);

                _sarifFiles.Add(sarifFile);

                return true;
            }
            return false;
        }

        private void CalculateUniqueRulesInResults(SarifLog sarifLog)
        {
            foreach (var run in sarifLog.Runs)
            {
                var results = run.Results;

                var uniqueRulesWithCounts = results
                    .Where(result => result.Rule != null)
                    .GroupBy(result => result.RuleId)
                    .Select(group => new RuleWithCount
                    {
                        Rule = group.First().Rule,
                        Count = group.Count()
                    })
                    .ToList();

                run.UsedRules = uniqueRulesWithCounts;
            }
        }

        private void UseRuleDictionary(SarifLog sarifLog)
        {
            foreach (var run in sarifLog.Runs)
            {
                var rules = run.Tool?.Driver?.Rule ?? new List<Rule>();
                var results = run.Results ?? new List<Result>();

                for (int i = 0; i < rules.Count; i++)
                {
                    var rule = rules[i];
                    string ruleKey = !string.IsNullOrEmpty(rule.Id) ? rule.Id : i.ToString();
                    _ruleDictionary[ruleKey] = rule;
                }

                foreach (var result in results)
                {
                    // Assign the corresponding rule to the result
                    if (!string.IsNullOrEmpty(result.RuleId) && _ruleDictionary.TryGetValue(result.RuleId, out var rule))
                    {
                        result.Rule = rule;
                    }
                    else if (result.RuleIndex >= 0 && result.RuleIndex < rules.Count)
                    {
                        result.Rule = rules[result.RuleIndex];
                    }
                }
            }
        }

        private void AddFilenamePathAndExt(SarifLog sarifLog)
        {
            foreach (var run in sarifLog.Runs ?? Enumerable.Empty<Run>())
            {
                foreach (var result in run.Results?.Where(r => r.Locations?.Any() == true) ?? Enumerable.Empty<Result>())
                {
                    result.ParentRun = run;

                    var firstLocation = result.Locations[0]?.PhysicalLocation?.ArtifactLocation;
                    result.OriginalFilenamePath = firstLocation?.Uri;

                    var resolvedPath = FileHelper.ResolveArtifactPath(firstLocation, run);
                    result.FilenamePath = string.IsNullOrWhiteSpace(resolvedPath)
                        ? firstLocation?.Uri
                        : resolvedPath;

                    result.ResultIdentity = SarifTriageIdentityHelper.BuildIdentity(result);
                    result.IsSnippetLoaded = result.Locations[0]?.PhysicalLocation?.ExtractedCodeSnippet != null;

                    result.FilenameExt = Path.GetExtension(result.FilenamePath)?.TrimStart('.');
                }
            }
        }

        /// <summary>
        /// Finds a result by its stable identity across all loaded SARIF runs.
        /// </summary>
        public (Run Run, Result Result)? FindResultByIdentity(string resultIdentity)
        {
            if (string.IsNullOrWhiteSpace(resultIdentity))
            {
                return null;
            }

            foreach (var run in _sarifFiles.SelectMany(file => file.SarifLog.Runs ?? Enumerable.Empty<Run>()))
            {
                var match = (run.Results ?? new List<Result>())
                    .FirstOrDefault(result => string.Equals(result.ResultIdentity, resultIdentity, StringComparison.Ordinal));

                if (match != null)
                {
                    return (run, match);
                }
            }

            return null;
        }

        /// <summary>
        /// Returns a set of result identities matching severity and rule filters using precomputed run indexes.
        /// </summary>
        public HashSet<string> GetFilteredResultIdentities(Run run, IReadOnlyCollection<string>? selectedSeverity, IEnumerable<RuleWithCount>? selectedRules)
        {
            ArgumentNullException.ThrowIfNull(run);

            var filtered = new HashSet<string>(run.AllResultIdentities, StringComparer.Ordinal);

            if (selectedSeverity != null && selectedSeverity.Count > 0)
            {
                var allowedBySeverity = new HashSet<string>(StringComparer.Ordinal);
                foreach (var severity in selectedSeverity)
                {
                    if (!string.IsNullOrWhiteSpace(severity) && run.ResultIdentityBySeverity.TryGetValue(severity, out var identities))
                    {
                        allowedBySeverity.UnionWith(identities);
                    }
                }

                filtered.IntersectWith(allowedBySeverity);
            }

            if (selectedRules != null)
            {
                var selectedRuleIds = selectedRules
                    .Where(rule => !string.IsNullOrWhiteSpace(rule?.Rule?.Id))
                    .Select(rule => rule.Rule.Id)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (selectedRuleIds.Count > 0)
                {
                    var allowedByRule = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var ruleId in selectedRuleIds)
                    {
                        if (run.ResultIdentityByRuleId.TryGetValue(ruleId, out var identities))
                        {
                            allowedByRule.UnionWith(identities);
                        }
                    }

                    filtered.IntersectWith(allowedByRule);
                }
            }

            return filtered;
        }

        private static void BuildRunIndexes(SarifLog sarifLog)
        {
            foreach (var run in sarifLog.Runs ?? Enumerable.Empty<Run>())
            {
                run.ResultIdentityBySeverity = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                run.ResultIdentityByRuleId = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
                run.AllResultIdentities = new HashSet<string>(StringComparer.Ordinal);

                foreach (var result in run.Results ?? Enumerable.Empty<Result>())
                {
                    if (string.IsNullOrWhiteSpace(result.ResultIdentity))
                    {
                        result.ResultIdentity = SarifTriageIdentityHelper.BuildIdentity(result);
                    }

                    if (string.IsNullOrWhiteSpace(result.ResultIdentity))
                    {
                        continue;
                    }

                    run.AllResultIdentities.Add(result.ResultIdentity);
                    AddToIndex(run.ResultIdentityBySeverity, result.Level, result.ResultIdentity);
                    AddToIndex(run.ResultIdentityByRuleId, result.RuleId, result.ResultIdentity);
                }
            }
        }

        private static void AddToIndex(Dictionary<string, HashSet<string>> index, string? key, string resultIdentity)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(resultIdentity))
            {
                return;
            }

            if (!index.TryGetValue(key, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                index[key] = set;
            }

            set.Add(resultIdentity);
        }

        private void CalculateLevelStats(SarifLog sarifLog)
        {
            sarifLog.Runs
                .ForEach(run =>
                {
                    int errorCount = 0;
                    int warningCount = 0;
                    int noteCount = 0;

                    // Dictionary to look up default level for each rule
                    var ruleDefaultLevels = (run.Tool?.Driver?.Rule ?? new List<Rule>())
                        .ToDictionary(rule => rule.Id, rule => rule.DefaultConfiguration?.Level ?? "warning");

                    (run.Results ?? new List<Result>()).ForEach(result =>
                    {
                        if (string.IsNullOrEmpty(result.Level))
                        {
                            // Fallback to rule's default level
                            result.Level = ruleDefaultLevels.ContainsKey(result.RuleId)
                                ? ruleDefaultLevels[result.RuleId]
                                : "warning"; // default value
                        }

                        switch (result.Level.ToLower())
                        {
                            case "error":
                                result.Severity = Result.ResultSeverity.High;
                                errorCount++;
                                break;
                            case "warning":
                                result.Severity = Result.ResultSeverity.Medium;
                                warningCount++;
                                break;
                            case "note":
                                result.Severity = Result.ResultSeverity.Medium;
                                noteCount++;
                                break;
                        }
                    });

                    run.Levels.Error = errorCount;
                    run.Levels.Warning = warningCount;
                    run.Levels.Note = noteCount;
                });
        }

        public void RemoveAllSarifFiles()
        {
            _sarifFiles.Clear();
        }

        public List<SarifFile> GetFiles(IEnumerable<SarifFile> sarifFiles)
        {
            return _sarifFiles.Where(f => f.Equals(sarifFiles)).ToList();
        }

        public IEnumerable<SarifFile> AllFiles
        {
            get { return _sarifFiles; }          
        }

        public int Count { get { return _sarifFiles.Count; } }

    }
            
}
