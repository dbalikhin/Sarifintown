namespace Sarifintown.Models
{
    public class DirectoryPickerResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int DirectoryId { get; set; }
        public string Name { get; set; }
        public List<string> Subdirectories { get; set; } = new List<string>();
    }
}
