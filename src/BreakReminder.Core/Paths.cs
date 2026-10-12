namespace BreakReminder.Core;

public static class Paths
{
    /// <summary>
    /// Windows: %APPDATA%\BreakReminder (как у старой версии — настройки и история подхватываются),
    /// Linux: ~/.config/BreakReminder, macOS: ~/Library/Application Support/BreakReminder.
    /// </summary>
    public static string DataDir
    {
        get
        {
            // для разработки и тестов: отдельная папка, чтобы не трогать настоящие данные
            if (Environment.GetEnvironmentVariable("BREAKREMINDER_DATA_DIR") is { Length: > 0 } custom)
                return custom;
            if (OperatingSystem.IsMacOS())
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "BreakReminder");
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BreakReminder");
        }
    }

    public static string SettingsFile => Path.Combine(DataDir, "settings.ini");
    public static string TipsFile => Path.Combine(DataDir, "tips.txt");
    public static string LongTipsFile => Path.Combine(DataDir, "tips_long.txt");
    public static string HistoryFile => Path.Combine(DataDir, "history.csv");
}
