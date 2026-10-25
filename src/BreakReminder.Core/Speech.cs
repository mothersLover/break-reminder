using System.Text.RegularExpressions;

namespace BreakReminder.Core;

/// <summary>Озвучка: языки, выбор того, что произносить, паузы для повторения.</summary>
public static class SpeechText
{
    /// <summary>Языки озвучки: код ISO 639-1 и название.</summary>
    public static readonly (string Code, string Name)[] Languages =
    {
        ("fr", "Французский"), ("en", "Английский"), ("de", "Немецкий"), ("es", "Испанский"),
        ("it", "Итальянский"), ("pt", "Португальский"), ("ru", "Русский"), ("", "Без озвучки")
    };

    public static string Name(string code) => Languages.FirstOrDefault(l => l.Code == code).Name ?? code;

    /// <summary>Пример фразы для «Проверить голос».</summary>
    public static string Sample(string code) => code switch
    {
        "fr" => "Bonjour ! On répète ensemble ?",
        "en" => "Hello! Shall we practise together?",
        "de" => "Hallo! Wollen wir zusammen üben?",
        "es" => "¡Hola! ¿Practicamos juntos?",
        "it" => "Ciao! Ripetiamo insieme?",
        "pt" => "Olá! Vamos praticar juntos?",
        _ => "Привет! Повторим вместе?"
    };

    static readonly Regex Cyrillic = new(@"[А-Яа-яЁё]");
    static readonly Regex NonCyrillicRun = new(@"[^А-Яа-яЁё]+");
    static readonly Regex Latin = new(@"[A-Za-zÀ-ÖØ-öø-ÿŒœ]{2,}");

    /// <summary>
    /// Что озвучить из сообщения помощника: только фразы на изучаемом языке (латиница), без русских пояснений —
    /// русский текст чужим голосом звучит нелепо. Пусто — озвучивать нечего.
    /// </summary>
    public static List<string> Foreign(string text)
    {
        var parts = new List<string>();
        foreach (Match m in NonCyrillicRun.Matches(text ?? ""))
        {
            // внутри куска — по строкам: «1. Je suis allé…» и «2. Elle est…» — разные фразы
            foreach (var raw in m.Value.Split('\n'))
            {
                string s = raw.Replace("«", "").Replace("»", "").Trim();
                s = Regex.Replace(s, @"^([-*•—]|\d+[.)])\s*", "");          // номера и маркеры списка
                s = s.Trim(' ', '«', '»', '"', '(', ')', ':', ';', ',', '—', '-', '→', '*', '/');
                int words = Latin.Matches(s).Count;
                if (words == 0) continue;
                // одиночное слово посреди русского текста («с aller нужен être», «Unité 5») — не озвучиваем,
                // только если оно в кавычках: «allée»
                if (words == 1 && !Regex.IsMatch(raw, "[«\"“]")) continue;
                if (!parts.Contains(s)) parts.Add(s);
            }
        }
        return parts;
    }

    /// <summary>Хватит ли пауз, чтобы повторить вслух: ~1,5 с + 70 мс на символ, не больше 8 с.</summary>
    public static TimeSpan RepeatPause(string phrase) =>
        TimeSpan.FromMilliseconds(Math.Min(8000, 1500 + 70 * (phrase ?? "").Length));

    public static bool HasCyrillic(string s) => Cyrillic.IsMatch(s ?? "");

    /// <summary>Как поставить голос, если его нет (по названию платформы из IPlatform.Name).</summary>
    public static string InstallHint(string platform, string lang) => lang == "" ? "Для этого хобби озвучка выключена." : platform switch
    {
        "Windows" => $"В системе нет голоса для языка «{Name(lang)}». Поставь его: Параметры → Время и язык → Язык и регион → " +
                     $"добавить {Name(lang).ToLowerInvariant()} язык (с речью) или Параметры → Время и язык → Речь → «Добавить голоса». " +
                     "Потом перезапусти программу.",
        "macOS" => $"Нет голоса для языка «{Name(lang)}». Системные настройки → Универсальный доступ → Устный контент → " +
                   "Системный голос → «Управление голосами» — добавь голос этого языка.",
        _ => "Не найдена программа озвучки. Поставь eSpeak NG или speech-dispatcher, например: sudo apt install espeak-ng"
    };
}
