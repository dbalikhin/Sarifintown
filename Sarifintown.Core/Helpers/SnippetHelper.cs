using Sarifintown.Models;

namespace Sarifintown.Helpers
{
    public class SnippetHelper
    {
        private const int DefaultContextRadius = 3;

        public static ExtractedCodeSnippet ExtractCodeSnippet(string fileContent, Region region, int surroundingLines = DefaultContextRadius)
        {
            return ExtractCodeSnippet(fileContent, region.StartLine, region.StartColumn, region.EndLine, region.EndColumn, surroundingLines);
        }

        public static string ExtractLineWindow(string fileContent, int startLine, int endLine, int radius = DefaultContextRadius)
        {
            if (string.IsNullOrEmpty(fileContent))
            {
                return string.Empty;
            }

            var allLines = fileContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (allLines.Length == 0)
            {
                return string.Empty;
            }

            var normalizedStart = Math.Max(1, startLine);
            var normalizedEnd = Math.Max(normalizedStart, endLine);

            var visibleStartLine = Math.Max(1, normalizedStart - Math.Max(0, radius));
            var visibleEndLine = Math.Min(allLines.Length, normalizedEnd + Math.Max(0, radius));

            return string.Join(Environment.NewLine, allLines.Skip(visibleStartLine - 1).Take((visibleEndLine - visibleStartLine) + 1)).TrimEnd();
        }

        public static string ExtractLineRange(string fileContent, int startLine, int endLine)
        {
            if (string.IsNullOrEmpty(fileContent))
            {
                return string.Empty;
            }

            var allLines = fileContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (allLines.Length == 0)
            {
                return string.Empty;
            }

            var normalizedStart = Math.Max(1, startLine);
            var normalizedEnd = Math.Max(normalizedStart, endLine);

            var safeStart = Math.Min(normalizedStart, allLines.Length);
            var safeEnd = Math.Min(normalizedEnd, allLines.Length);

            return string.Join(Environment.NewLine, allLines.Skip(safeStart - 1).Take((safeEnd - safeStart) + 1)).TrimEnd();
        }

        public static ExtractedCodeSnippet ExtractCodeSnippet(
            string fileContent,
            int startLine,
            int startColumn,
            int endLine,
            int endColumn,
            int surroundingLines = DefaultContextRadius)
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
            var contextRadius = Math.Max(1, surroundingLines);
            int visibleStartLine = Math.Max(1, startLine - contextRadius);
            int visibleEndLine = Math.Min(totalLines, endLine + contextRadius);

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
            public static string HighlightSnippet(string fileContent, Region region)
            {
                var result = new System.Text.StringBuilder();
                int currentLine = 1;
                int currentColumn = 1;
                bool withinRegion = false;

                for (int i = 0; i < fileContent.Length; i++)
                {
                    char currentChar = fileContent[i];

                    if (currentLine == region.StartLine && currentColumn == region.StartColumn && !withinRegion)
                    {
                        // Start highlighting
                        result.Append("<mark>");
                        withinRegion = true;
                    }

                    if (currentLine == region.EndLine && currentColumn == region.EndColumn && withinRegion)
                    {
                        // End highlighting
                        result.Append("</mark>");
                        withinRegion = false;
                    }

                    result.Append(currentChar);

                    // Handle both Windows and Linux line endings
                    if (currentChar == '\r' && i + 1 < fileContent.Length && fileContent[i + 1] == '\n')
                    {
                        // Windows newline
                        currentLine++;
                        currentColumn = 1;
                        i++; // Skip the '\n' part of the Windows newline
                        result.Append('\n'); // Normalize to '\n' in the result
                    }
                    else if (currentChar == '\n')
                    {
                        // Linux newline
                        currentLine++;
                        currentColumn = 1;
                    }
                    else
                    {
                        currentColumn++;
                    }
                }

                // If we are still within the region at the end of the loop, close the mark tag
                if (withinRegion)
                {
                    result.Append("</mark>");
                }

                return result.ToString();
            }
        }
    }
