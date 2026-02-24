#nullable disable

using System.Text.Json.Serialization;

namespace Sarifintown.Models
{
    public class SarifLog
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("runs")]
        public List<Run> Runs { get; set; }
    }

    public class Run
    {
        [JsonPropertyName("tool")]
        public Tool Tool { get; set; }

        [JsonPropertyName("results")]
        public List<Result> Results { get; set; }

        [JsonIgnore]
        public ResultLevels Levels { get; set; } = new ResultLevels();

        public class ResultLevels
        {
            public int Error { get; set; }
            public int Warning { get; set; }
            public int Note { get; set; }
        }


        [JsonIgnore]
        public int JSDirectoryId { get; set; }

        [JsonIgnore]
        public List<RuleWithCount> UsedRules { get; set; }
    }

    public class RuleWithCount
    {
        public Rule Rule { get; set; }
        public int Count { get; set; }
    }

    public class Tool
    {
        [JsonPropertyName("driver")]
        public ToolDriver Driver { get; set; }

        public class ToolDriver
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("semanticVersion")]
            public string SemanticVersion { get; set; }

            [JsonPropertyName("version")]
            public string Version { get; set; }

            [JsonPropertyName("rules")]
            public List<Rule> Rule { get; set; }
        }
    }

    public class Rule
    {
        [JsonRequired]
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("shortDescription")]
        public RuleShortDescription ShortDescription { get; set; }

        public class RuleShortDescription
        {
            //[JsonRequired]
            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        [JsonPropertyName("defaultConfiguration")]
        public RuleDefaultConfiguration DefaultConfiguration { get; set; }

        public class RuleDefaultConfiguration
        {
            [JsonPropertyName("level")]
            public string Level { get; set; }
        }

        public List<RuleProperty> Properties { get; set; }
    }

    public class RuleProperty
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName ("name")]
        public string Name { get; set; }

        [JsonPropertyName("problem.severity")]
        public string SeverityProblem { get; set; }

        [JsonPropertyName("security-severity")]
        public string SeveritySecurity { get; set; }
    }


    public class Result
    {
        [JsonPropertyName("ruleId")]
        public string RuleId { get; set; }

        [JsonPropertyName("ruleIndex")]
        public int RuleIndex { get; set; }

        [JsonPropertyName("level")]
        public string Level { get; set; }

        [JsonIgnore]
        public ResultSeverity Severity { get; set; }

        public enum ResultSeverity
        {
            Low = 0,
            Medium = 1,
            High = 2,
        }

        [JsonPropertyName("message")]
        public ResultMessage Message { get; set; }

        public class ResultMessage
        {
            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        [JsonIgnore]
        public string FilenamePath { get; set; }

        [JsonIgnore]
        public string FilenameExt { get; set; }

        [JsonIgnore]
        public Rule Rule { get; set; }

        [JsonPropertyName("locations")]
        public List<ResultLocation> Locations { get; set; }

        [JsonPropertyName("codeFlows")]
        public List<CodeFlow> CodeFlows { get; set; }

        [JsonPropertyName("relatedLocations")]
        public List<RelatedLocation> RelatedLocations { get; set; }

        [JsonPropertyName("properties")]
        public Dictionary<string, object> Properties { get; set; }

        [JsonPropertyName("suppressions")]
        public List<Suppression> Suppressions { get; set; }
    }

    public class ResultLocation
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("physicalLocation")]
        public PhysicalLocation PhysicalLocation { get; set; }
    }

    public class CodeFlow
    {
        [JsonPropertyName("message")]
        public CodeFlowMessage Message { get; set; }

        public class CodeFlowMessage
        {
            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        [JsonPropertyName("threadFlows")]
        public List<ThreadFlow> ThreadFlows { get; set; }
    }

    public class ThreadFlow
    {
        [JsonPropertyName("locations")]
        public List<ThreadFlowLocation> Locations { get; set; }
        
    }

    public class ThreadFlowLocation
    {
        [JsonPropertyName("location")]
        public ResultLocation Location { get; set; }
    }

    public class RelatedLocation
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("message")]
        public RelatedLocationMessage Message { get; set; }

        public class RelatedLocationMessage
        {
            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        [JsonPropertyName("physicalLocation")]
        public PhysicalLocation PhysicalLocation { get; set; }
    }

    public class PhysicalLocation
    {
        [JsonPropertyName("artifactLocation")]
        public PhysicalLocationArtifactLocation ArtifactLocation { get; set; }

        public class PhysicalLocationArtifactLocation
        {
            [JsonPropertyName("uri")]
            public string Uri { get; set; }

            [JsonPropertyName("uriBaseId")]
            public string UriBaseId { get; set; }
        }

        [JsonPropertyName("region")]
        public Region Region { get; set; }

        [JsonIgnore]
        public string Language { get; set; }

        [JsonIgnore]
        public ExtractedCodeSnippet ExtractedCodeSnippet { get; set; }
    }

    public class ExtractedCodeSnippet
    {
        public string Snippet { get; set; }
        public string LineSnippet { get; set; }
        public string ContextSnippet { get; set; }
        public int StartLine { get; set; }
        public int EndLine { get; set; }
        public int VisibleStartLine { get; set; }
        public int VisibleEndLine { get; set; }
    }



    public class Region
    {
        [JsonPropertyName("startLine")]
        public int StartLine { get; set; }

        [JsonPropertyName("startColumn")]
        public int StartColumn { get; set; }

        [JsonPropertyName("endLine")]
        public int EndLine { get; set; }

        [JsonPropertyName("endColumn")]
        public int EndColumn { get; set; }

        [JsonPropertyName("snippet")]
        public RegionSnippet? Snippet { get; set; } = new RegionSnippet();

        public class RegionSnippet
        {
            [JsonPropertyName("text")]
            public string Text { get; set; }
        }

        public override bool Equals(object obj)
        {
            if (obj is Region other)
            {
                return StartLine == other.StartLine &&
                       StartColumn == other.StartColumn &&
                       EndLine == other.EndLine &&
                       EndColumn == other.EndColumn;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(StartLine, StartColumn, EndLine, EndColumn);
        }
    }



    public class Suppression
    {
        /// <summary>
        /// Specifies the type of suppression (required).
        /// Example values: "inSource", "external"
        /// </summary>
        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        /// <summary>
        /// Provides a human-readable explanation for why the result was suppressed (optional).
        /// </summary>
        [JsonPropertyName("justification")]
        public string Justification { get; set; }

        /// <summary>
        /// Indicates whether the suppression is currently active (optional).
        /// Example values: "accepted", "underReview", "rejected"
        /// </summary>
        [JsonPropertyName("status")]
        public string Status { get; set; }

        /// <summary>
        /// Describes the location where the suppression was applied (optional).
        /// </summary>
        [JsonPropertyName("location")]
        public SuppressionLocation Location { get; set; }

        public class SuppressionLocation
        {
            /// <summary>
            /// URI of the file where the suppression is applied (optional).
            /// </summary>
            [JsonPropertyName("uri")]
            public string Uri { get; set; }

            /// <summary>
            /// Describes the region within the file (optional).
            /// </summary>
            [JsonPropertyName("region")]
            public Region Region { get; set; }
        }
    }


}
