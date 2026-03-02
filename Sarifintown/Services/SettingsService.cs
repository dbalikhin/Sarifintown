namespace Sarifintown.Services
{
    public class SettingsService
    {
        public const int MinSurroundingLines = 1;
        public const int MaxSurroundingLines = 10;
        public const int DefaultSurroundingLines = 3;

        private int _surroundingLines = DefaultSurroundingLines;

        public ResultViewMode ResultViewMode { get; set; } = ResultViewMode.SplitMode;

        public int SurroundingLines
        {
            get => _surroundingLines;
            set => _surroundingLines = Math.Clamp(value, MinSurroundingLines, MaxSurroundingLines);
        }
    }

    public enum ResultViewMode
    {
        SingleWindow,
        SplitMode
    }
}
