using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace Sarifintown.Models
{
    public class DisplayMessage
    {
        public AuthorRole Role { get; set; }
        public string Content { get; set; }
        public bool IsReasoning { get; set; } // Flag to identify "thinking" messages

        public DisplayMessage(AuthorRole role, string content, bool isReasoning = false)
        {
            Role = role;
            Content = content;
            IsReasoning = isReasoning;
        }
    }
}
