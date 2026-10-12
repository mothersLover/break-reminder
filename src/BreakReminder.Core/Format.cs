namespace BreakReminder.Core;

public static class Format
{
    public static string Hms(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";
    }

    public static string Ms(TimeSpan t)
    {
        int sec = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds));
        return $"{sec / 60:00}:{sec % 60:00}";
    }

    public static string Minutes(TimeSpan t)
    {
        int m = (int)Math.Round(t.TotalMinutes);
        return m >= 60 ? $"{m / 60} ч {m % 60} мин" : m + " мин";
    }
}
