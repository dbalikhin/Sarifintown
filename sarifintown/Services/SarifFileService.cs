using Sarifintown.Models;

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
                AddFilenamePathAndExt(sarifFile.SarifLog);
                UseRuleDictionary(sarifFile.SarifLog);
                CalculateLevelStats(sarifFile.SarifLog);
                CalculateUniqueRulesInResults(sarifFile.SarifLog);

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
                var rules = run.Tool.Driver.Rule;
                var results = run.Results;

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
            sarifLog.Runs
                .SelectMany(run => run.Results)
                .Where(result => result.Locations.Any())
                .ToList()
                .ForEach(result =>
                {
                    result.FilenamePath = result.Locations[0].PhysicalLocation.ArtifactLocation.Uri;
                    result.FilenameExt = Path.GetExtension(result.FilenamePath)?.TrimStart('.');
                });
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
                    var ruleDefaultLevels = run.Tool.Driver.Rule
                        .ToDictionary(rule => rule.Id, rule => rule.DefaultConfiguration?.Level ?? "warning");

                    run.Results.ForEach(result =>
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
