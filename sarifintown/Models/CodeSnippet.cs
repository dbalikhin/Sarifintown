namespace Sarifintown.Models
{
    public class CodeSnippet
    {
        public string Language { get; set; }
        public string CodeContent { get; set; }
        public string HighlightedLines { get; set; }
        public int StartLineNumber { get; set; } = 1;
    }
}
