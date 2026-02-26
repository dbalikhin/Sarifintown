using Sarifintown.Core;
using Sarifintown.Models;
using System.Collections.Generic;
using System.Linq;

namespace Sarifintown.Helpers
{
    public static class AnalysisHelper
    {

        public static async Task<CodeSnippet> ExtractSnippetAsync(
        string relativePath,
        IFileReader fileReader)
        {
            // The helper just calls the abstract method. 
            // Blazor or Console handles the actual hunting.
            string sourceCode = await fileReader.ReadFileAsync(relativePath);

            // ... do your Tree-sitter or SARIF processing here
            return new CodeSnippet { };
        }

        public static bool FilterBySeverityAndRule(Result result, IReadOnlyCollection<string> selectedSeverity, IEnumerable<RuleWithCount> selectedRules)
        {
            if (result == null) return false;

            // filter by severity
            bool rowVisible = selectedSeverity != null && selectedSeverity.Contains(result.Level);

            // filter by only selected rule (empty selection is treated as All rules selected)
            if (selectedRules != null && selectedRules.Any())
            {
                rowVisible &= selectedRules.Select(r => r.Rule.Id).Contains(result.RuleId);
            }

            return rowVisible;
        }

        public static string GetMultiSelectionText(List<string> selectedValues)
        {
            if (selectedValues == null || selectedValues.Count == 0)
            {
                return "0 SARIF files have been selected";
            }
            return $"{selectedValues.Count} SARIF file{(selectedValues.Count > 1 ? "s have" : " has")} been selected";
        }

        public static string GetMultiSelectionRuleText(List<string> selectedValues)
        {
            if (selectedValues == null || selectedValues.Count == 0)
            {
                return "All rules selected";
            }
            else
            {
                return $"{selectedValues.Count} Rule{(selectedValues.Count > 1 ? "s" : "")} selected";
            }
        }
    }
}