namespace Sarifintown.Helpers
{
    public class TreeSitterLanguageHelper
    {
        public static string GetLanguageByExtension(string extension)
        {
            switch (extension)
            {
                case "c":
                    return "c";
                case "cs":
                    return "csharp";
                case "cpp":
                    return "cpp";
                case "go":
                    return "go";
                case "java":
                    return "java";                
                case "js":
                    return "javascript";
                case "kt":
                    return "kotlin";
                case "php":
                    return "php";
                case "py":
                    return "python";
                case "rb":
                    return "ruby";
                case "rs":
                    return "rust";
                case "ts":
                    return "typescript";
                default:
                    return "";
            }
        }
    }
}