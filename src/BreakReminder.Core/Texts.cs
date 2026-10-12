namespace BreakReminder.Core;

/// <summary>Стандартные тексты уведомлений.</summary>
public static class Texts
{
    public const string TitleShort = "Время отдохнуть!";
    public const string TitleLong = "Большой перерыв!";
    public const string BtnDone = "✓  Сделал";
    public const string BtnSnooze = "Отложить {N} мин";
    public const string BtnSkip = "Пропустить";

    public static readonly string[] Tips =
    {
        "Встань и пройдись пару минут.",
        "Посмотри вдаль 20 секунд — глаза скажут спасибо.",
        "Разомни шею и плечи: медленные круги головой и плечами.",
        "Выпей стакан воды.",
        "Сделай 5 медленных глубоких вдохов.",
        "Потянись и выпрями спину.",
        "Закрой глаза на полминуты и поморгай.",
        "Подойди к окну, посмотри на улицу.",
        "Сделай 10 приседаний — кровь разгонится."
    };

    public static readonly string[] LongTips =
    {
        "Выйди на улицу или хотя бы пройдись по этажу.",
        "Перекуси и выпей воды — подальше от экрана.",
        "Сделай лёгкую зарядку: наклоны, приседания, растяжка.",
        "Отвлекись полностью: никаких чатов и почты 15 минут.",
        "Проветри комнату и посмотри в окно."
    };

    /// <summary>Текст -> непустые строки; если строк нет — стандартный список.</summary>
    public static string[] ParseLines(string? text, string[] fallback)
    {
        var list = (text ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        return list.Length > 0 ? list : fallback;
    }
}
