namespace Sarifintown.Services
{
    public class SettingsService
    {
        public ResultViewMode ResultViewMode { get; set; } = ResultViewMode.SplitMode;
    }

    public enum ResultViewMode
    {
        SingleWindow,
        SplitMode
    }
}
