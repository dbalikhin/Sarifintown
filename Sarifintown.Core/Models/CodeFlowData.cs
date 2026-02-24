using System.Text.Json.Serialization;

namespace Sarifintown.Models
{
    public class CodeFlowData
    {
        public int Id { get; set; }
        public string Filename { get; set; }
        public string FilenameExt { get; set; }
        public Region Region { get; set; }
        public ExtractedCodeSnippet ExtractedSnippet { get; set; } = new ExtractedCodeSnippet();
    }

    public class MethodExtractionResult
    {
        [JsonPropertyName("methodCode")]
        public string MethodCode { get; set; }

        [JsonPropertyName("sarifRegion")]
        public Region SarifRegion { get; set; }

        [JsonPropertyName("isFound")]
        public bool IsFound { get; set; }
    }

    public class MethodHighlightResult
    {
        public int Id { get; set; }
        public string Filename { get; set; }
        public string FilenameExt { get; set; }
        public ExtractedCodeSnippet ExtractedSnippet { get; set; } = new ExtractedCodeSnippet();
        public Region SarifRegion { get; set; }
        public List<int> CodeFlowDataIds { get; set; } = new List<int>();
        public List<int> HighlightLines { get; set; } = new List<int>();
    }
}
