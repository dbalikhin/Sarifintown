using Sarifintown.Models;

namespace Sarifintown.Helpers
{
    public class SnippetHelper
    {
        public static ExtractedCodeSnippet ExtractCodeSnippet(string fileContent, Region region)
        {
            return ExtractCodeSnippet(fileContent, region.StartLine, region.StartColumn, region.EndLine, region.EndColumn);
        }

        public static ExtractedCodeSnippet ExtractCodeSnippet(string fileContent, int startLine, int startColumn, int endLine, int endColumn)
        {
            // Split the file content into lines
            var allLines = fileContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // Determine the total number of lines
            int totalLines = allLines.Length;

            // Validate startLine and endLine
            if (startLine < 1 || startLine > totalLines || endLine < 1 || endLine > totalLines)
            {
                throw new ArgumentOutOfRangeException("StartLine or EndLine is out of range.");
            }

            // Calculate the visible range of lines to include (with boundary checks)
            int visibleStartLine = Math.Max(1, startLine - 3);
            int visibleEndLine = Math.Min(totalLines, endLine + 3);

            // Extract ContextSnippet: the lines from visibleStartLine to visibleEndLine
            var contextLines = new List<string>();
            for (int lineNumber = visibleStartLine; lineNumber <= visibleEndLine; lineNumber++)
            {
                int lineIndex = lineNumber - 1; // zero-based index
                string line = allLines[lineIndex];
                contextLines.Add(line);
            }
            string contextSnippet = string.Join(Environment.NewLine, contextLines);

            // Extract Snippet: the exact text between startLine/startColumn and endLine/endColumn
            string snippet = ExtractSnippetFromLines(allLines, startLine, startColumn, endLine, endColumn);

            // Return the result
            return new ExtractedCodeSnippet
            {
                Snippet = snippet,
                ContextSnippet = contextSnippet,
                LineSnippet = allLines[startLine - 1],
                StartLine = startLine,
                EndLine = endLine,
                VisibleStartLine = visibleStartLine,
                VisibleEndLine = visibleEndLine
            };
        }

        private static string ExtractSnippetFromLines(string[] allLines, int startLine, int startColumn, int endLine, int endColumn)
        {
            if (startLine < 1 || startLine > allLines.Length || endLine < 1 || endLine > allLines.Length)
            {
                throw new ArgumentOutOfRangeException("StartLine or EndLine is out of range.");
            }

            int startLineIndex = startLine - 1;
            int endLineIndex = endLine - 1;

            var snippetLines = new List<string>();

            if (startLineIndex == endLineIndex)
            {
                // Single-line snippet
                string line = allLines[startLineIndex];
                int startIndex = Math.Max(0, startColumn - 1);  // already 0-based
                int length = endColumn - startColumn + 1; // make endColumn inclusive
                if (startIndex >= line.Length)
                {
                    snippetLines.Add(string.Empty);
                }
                else
                {
                    length = Math.Min(line.Length - startIndex, length);
                    snippetLines.Add(line.Substring(startIndex, length));  // was: startIndex - 1 (double subtraction bug)
                }
            }
            else
            {
                // Multi-line snippet
                string firstLine = allLines[startLineIndex];
                int startIndex = Math.Max(0, startColumn - 1);
                if (startIndex >= firstLine.Length)
                {
                    snippetLines.Add(string.Empty);
                }
                else
                {
                    snippetLines.Add(firstLine.Substring(startIndex));
                }

                for (int i = startLineIndex + 1; i < endLineIndex; i++)
                {
                    snippetLines.Add(allLines[i]);
                }

                string lastLine = allLines[endLineIndex];
                int endIndex = Math.Min(lastLine.Length, endColumn); // make endColumn inclusive
                if (endIndex > 0)
                {
                    snippetLines.Add(lastLine.Substring(0, endIndex));
                }
                else
                {
                    snippetLines.Add(string.Empty);
                }
            }

            return string.Join(Environment.NewLine, snippetLines);
        }
    }
}
