using NUnit.Framework;
using Sarifintown.Helpers;
using Sarifintown.Models;
using System.Collections.Generic;

namespace Sarifintown.Core.Tests
{
    [TestFixture]
    public class AnalysisHelperTests
    {
        [Test]
        public void FilterBySeverityAndRule_WithNullResult_ReturnsFalse()
        {
            var result = AnalysisHelper.FilterBySeverityAndRule(null!, new List<string>(), new List<RuleWithCount>());
            Assert.That(result, Is.False);
        }

        [Test]
        public void FilterBySeverityAndRule_WithMatchingSeverityAndNoRules_ReturnsTrue()
        {
            var result = new Result { Level = "error", RuleId = "RULE1" };
            var selectedSeverity = new List<string> { "error", "warning" };
            var selectedRules = new List<RuleWithCount>();

            var isVisible = AnalysisHelper.FilterBySeverityAndRule(result, selectedSeverity, selectedRules);

            Assert.That(isVisible, Is.True);
        }

        [Test]
        public void FilterBySeverityAndRule_WithNonMatchingSeverity_ReturnsFalse()
        {
            var result = new Result { Level = "note", RuleId = "RULE1" };
            var selectedSeverity = new List<string> { "error", "warning" };
            var selectedRules = new List<RuleWithCount>();

            var isVisible = AnalysisHelper.FilterBySeverityAndRule(result, selectedSeverity, selectedRules);

            Assert.That(isVisible, Is.False);
        }

        [Test]
        public void FilterBySeverityAndRule_WithMatchingSeverityAndMatchingRule_ReturnsTrue()
        {
            var result = new Result { Level = "error", RuleId = "RULE1" };
            var selectedSeverity = new List<string> { "error" };
            var selectedRules = new List<RuleWithCount>
            {
                new RuleWithCount { Rule = new Rule { Id = "RULE1" } }
            };

            var isVisible = AnalysisHelper.FilterBySeverityAndRule(result, selectedSeverity, selectedRules);

            Assert.That(isVisible, Is.True);
        }

        [Test]
        public void FilterBySeverityAndRule_WithMatchingSeverityAndNonMatchingRule_ReturnsFalse()
        {
            var result = new Result { Level = "error", RuleId = "RULE2" };
            var selectedSeverity = new List<string> { "error" };
            var selectedRules = new List<RuleWithCount>
            {
                new RuleWithCount { Rule = new Rule { Id = "RULE1" } }
            };

            var isVisible = AnalysisHelper.FilterBySeverityAndRule(result, selectedSeverity, selectedRules);

            Assert.That(isVisible, Is.False);
        }

        [Test]
        public void GetMultiSelectionText_WithEmptyList_ReturnsZeroFiles()
        {
            var result = AnalysisHelper.GetMultiSelectionText(new List<string>());
            Assert.That(result, Is.EqualTo("0 SARIF files have been selected"));
        }

        [Test]
        public void GetMultiSelectionText_WithOneFile_ReturnsOneFile()
        {
            var result = AnalysisHelper.GetMultiSelectionText(new List<string> { "file1" });
            Assert.That(result, Is.EqualTo("1 SARIF file has been selected"));
        }

        [Test]
        public void GetMultiSelectionText_WithMultipleFiles_ReturnsMultipleFiles()
        {
            var result = AnalysisHelper.GetMultiSelectionText(new List<string> { "file1", "file2" });
            Assert.That(result, Is.EqualTo("2 SARIF files have been selected"));
        }

        [Test]
        public void GetMultiSelectionRuleText_WithEmptyList_ReturnsAllRules()
        {
            var result = AnalysisHelper.GetMultiSelectionRuleText(new List<string>());
            Assert.That(result, Is.EqualTo("All rules selected"));
        }

        [Test]
        public void GetMultiSelectionRuleText_WithOneRule_ReturnsOneRule()
        {
            var result = AnalysisHelper.GetMultiSelectionRuleText(new List<string> { "rule1" });
            Assert.That(result, Is.EqualTo("1 Rule selected"));
        }

        [Test]
        public void GetMultiSelectionRuleText_WithMultipleRules_ReturnsMultipleRules()
        {
            var result = AnalysisHelper.GetMultiSelectionRuleText(new List<string> { "rule1", "rule2" });
            Assert.That(result, Is.EqualTo("2 Rules selected"));
        }
    }
}
