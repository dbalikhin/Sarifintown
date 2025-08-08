using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace Sarifintown.Models
{
    public class DisplayMessage
    {
        public AuthorRole Role { get; init; }
        public StringBuilder DisplayContent { get; set; } = new();
        public StringBuilder ThinkingLog { get; set; } = new();
        public bool HasThinking => ThinkingLog.Length > 0;
        public string FinalContent => DisplayContent.ToString();
        public string FinalThinkingLog => ThinkingLog.ToString();
    }
}
