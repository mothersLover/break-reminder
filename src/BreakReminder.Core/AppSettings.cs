using System.Text;

namespace BreakReminder.Core;

public enum PopupMode { Corner, Center, Fullscreen }

/// <summary>Шифрование секретов в settings.ini (ключи API, ссылка на календарь). Реализация — по ОС.</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    /// <summary>null — расшифровать не удалось (например, файл перенесли с другого компьютера).</summary>
    string? Unprotect(string stored);
}

/// <summary>Настройки. Формат settings.ini совместим со старой (legacy) версией.</summary>
public sealed class AppSettings
{
    // ---- напоминания ----
    public int IntervalMin = 50;
    public int BreakMin = 5;
    public int SnoozeMin = 5;
    public bool IdleDetect = true;

    // ---- большие перерывы ----
    public bool LongBreaks = true;
    public int LongEvery = 4;
    public int LongBreakMin = 15;

    // ---- не беспокоить ----
    public bool DndCalls = true;
    public bool DndFullscreen = true;

    // ---- уведомление ----
    public PopupMode Popup = PopupMode.Corner;
    public bool Blink = true;

    // ---- звук ----
    public bool SoundOn = true;
    public string Sound = "chime";
    public string CustomSoundPath = "";
    public int Volume = 70;
    public bool RepeatSound = true;
    public int RepeatSec = 60;

    // ---- рабочий день и запуск ----
    public bool AutoDay = false;
    public int AutoStartMin = 10 * 60;  // 10:00
    public int AutoEndMin = 22 * 60;    // 22:00
    public bool CloseToTray = true;
    public bool PauseHotkey = true;

    // ---- календарь (секретная iCal-ссылка) ----
    public bool CalendarOn = false;
    public string CalendarUrl = "";
    public bool CalWaitMeetings = true;      // во время встречи напоминание ждёт
    public bool CalBreakBefore = true;       // перерыв до встречи, если иначе он выпадет на неё
    public int CalBreakBeforeMin = 10;
    public bool CalMeetingAlert = true;      // напоминание о встрече
    public int CalMeetingAlertMin = 5;
    public bool CalChainLong = true;         // после длинной серии встреч — большой перерыв
    public int CalChainMin = 120;

    // ---- перерыв и глаза ----
    public bool GuidedBreak = true;          // «Начать перерыв»: таймер + упражнения
    public bool EyeRest = true;              // 20-20-20
    public int EyeEveryMin = 20;
    public int EyeSeconds = 20;

    // ---- утро и вечер ----
    public bool Greeting = true;             // приветствие в начале дня
    public string UserName = "";             // как обращаться
    public bool DayReport = true;            // отчёт в конце дня
    public bool Weather = false;
    public string WeatherCity = "";          // как показывать город
    public double WeatherLat = double.NaN;   // координаты найденного города
    public double WeatherLon = double.NaN;

    // ---- свои задачи ----
    public bool TaskAlerts = true;           // напоминать о задачах со временем
    public int TaskAlertMin = 5;             // за сколько минут (0 — ровно во время)
    public bool HabitReminders = true;       // напоминать о привычках (вода, прогулка…)

    public bool HasWeatherPlace => !double.IsNaN(WeatherLat) && !double.IsNaN(WeatherLon);

    // ---- обновления ----
    public bool UpdateOnStart = true;        // проверять при запуске
    public bool UpdateDaily = true;          // и раз в сутки, пока программа работает
    public string UpdateSkipVersion = "";    // «Пропустить эту версию»

    // ---- ИИ-помощник ----
    public bool AiOn = false;                // главный выключатель: пока выключен — никаких запросов
    public AiProvider AiProvider = AiProvider.ClaudeCode;
    public string AiClaudePath = "";         // пусто — найти claude автоматически
    public string AiClaudeModel = "";        // пусто — модель по умолчанию в Claude Code
    public string AiAnthropicKey = "";
    public string AiAnthropicModel = AiClients.DefaultAnthropicModel;
    public string AiOpenAiUrl = AiClients.DefaultOpenAiUrl;
    public string AiOpenAiKey = "";
    public string AiOpenAiModel = "";
    public string AiOllamaUrl = AiClients.DefaultOllamaUrl;
    public string AiOllamaModel = "";
    public bool AiHintsFirst = true;         // при проверке заданий — сначала подсказка
    public bool AiRequireWeather = true;     // запросы к ИИ — только если доступен прогноз погоды (проверка связи)

    // ---- занятия по расписанию (нужен включённый помощник) ----
    public bool PracticeOn = true;
    public bool PracticeMorning = true;      // разминка после начала рабочего дня
    public int PracticeMorningMin = 5;
    public int PracticeDayCount = 2;         // мини-тренировок за рабочий день (0 — не надо)
    public int PracticeDayMin = 3;
    public bool PracticeCards = true;        // днём — повторение карточек, если накопились
    public bool WeeklyReview = true;         // итоги недели от помощника (воскресенье вечером)

    // ---- озвучка ----
    public string SpeechLang = "fr";         // на каком языке произносить карточки и фразы помощника
    public bool SpeechSlow = true;           // «Послушай и повтори»: первый раз — медленнее
    public bool SpeechAutoCards = true;      // в карточках — сразу произносить ответ
    public bool ListenInBreak = true;        // в перерыве — кнопка «Послушай и повтори»
    public int ListenCount = 5;              // фраз за раз
    public bool PracticeEvening = true;      // вечернее занятие
    public int PracticeEveningAt = 20 * 60;  // 20:00
    public int PracticeEveningMin = 15;

    // ---- тексты (советы хранятся в tips.txt / tips_long.txt) ----
    public string TitleShort = Texts.TitleShort;
    public string TitleLong = Texts.TitleLong;
    public string[] TipsShort = Texts.Tips;
    public string[] TipsLong = Texts.LongTips;
    public string BtnDone = Texts.BtnDone;
    public string BtnSnooze = Texts.BtnSnooze;
    public string BtnSkip = Texts.BtnSkip;

    public TimeSpan Interval => TimeSpan.FromMinutes(IntervalMin);
    public TimeSpan BreakSpan => TimeSpan.FromMinutes(BreakMin);
    public TimeSpan Snooze => TimeSpan.FromMinutes(SnoozeMin);
    public TimeSpan LongBreakSpan => TimeSpan.FromMinutes(LongBreakMin);
    public TimeSpan AutoStartTime => TimeSpan.FromMinutes(AutoStartMin);
    public TimeSpan AutoEndTime => TimeSpan.FromMinutes(AutoEndMin);

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>
    /// Чем шифровать секреты (задаёт приложение до Load; null — хранить как есть). Зашифрованное значение
    /// пишется как «enc:…»; старые незашифрованные читаются и шифруются при следующем сохранении.
    /// </summary>
    public static ISecretProtector? Protector { get; set; }

    const string EncPrefix = "enc:";

    static string ReadSecret(Dictionary<string, string> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v.Length == 0) return "";
        if (!v.StartsWith(EncPrefix)) return v;                         // старый формат — открытым текстом
        try { return Protector?.Unprotect(v[EncPrefix.Length..]) ?? ""; }
        catch { return ""; }
    }

    static string WriteSecret(string value)
    {
        value = OneLine(value);
        if (value.Length == 0 || Protector == null) return value;
        try { return EncPrefix + Protector.Protect(value); }
        catch { return value; }
    }

    public static AppSettings Load() => Load(Paths.SettingsFile, Paths.TipsFile, Paths.LongTipsFile);

    public static AppSettings Load(string settingsFile, string tipsFile, string longTipsFile)
    {
        var s = new AppSettings();
        s.ReloadTips(tipsFile, longTipsFile);
        try
        {
            if (!File.Exists(settingsFile)) return s;
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(settingsFile, Encoding.UTF8))
            {
                int i = line.IndexOf('=');
                if (i > 0) d[line[..i].Trim()] = line[(i + 1)..].Trim();
            }

            s.IntervalMin = Int(d, "IntervalMin", s.IntervalMin, 1, 240);
            s.BreakMin = Int(d, "BreakMin", s.BreakMin, 1, 60);
            s.SnoozeMin = Int(d, "SnoozeMin", s.SnoozeMin, 1, 60);
            s.IdleDetect = Bool(d, "IdleDetect", s.IdleDetect);
            s.LongBreaks = Bool(d, "LongBreaks", s.LongBreaks);
            s.LongEvery = Int(d, "LongEvery", s.LongEvery, 2, 10);
            s.LongBreakMin = Int(d, "LongBreakMin", s.LongBreakMin, 5, 120);
            s.DndCalls = Bool(d, "DndCalls", s.DndCalls);
            s.DndFullscreen = Bool(d, "DndFullscreen", s.DndFullscreen);
            s.Blink = Bool(d, "Blink", s.Blink);
            s.SoundOn = Bool(d, "SoundOn", s.SoundOn);
            s.Volume = Int(d, "Volume", s.Volume, 0, 100);
            s.RepeatSound = Bool(d, "RepeatSound", s.RepeatSound);
            s.RepeatSec = Int(d, "RepeatSec", s.RepeatSec, 10, 600);
            s.AutoDay = Bool(d, "AutoDay", s.AutoDay);
            s.AutoStartMin = Int(d, "AutoStartMin", s.AutoStartMin, 0, 24 * 60 - 1);
            s.AutoEndMin = Int(d, "AutoEndMin", s.AutoEndMin, 0, 24 * 60 - 1);
            s.CloseToTray = Bool(d, "CloseToTray", s.CloseToTray);
            s.PauseHotkey = Bool(d, "PauseHotkey", s.PauseHotkey);

            s.CalendarOn = Bool(d, "CalendarOn", s.CalendarOn);
            s.CalWaitMeetings = Bool(d, "CalWaitMeetings", s.CalWaitMeetings);
            s.CalBreakBefore = Bool(d, "CalBreakBefore", s.CalBreakBefore);
            s.CalBreakBeforeMin = Int(d, "CalBreakBeforeMin", s.CalBreakBeforeMin, 1, 60);
            s.CalMeetingAlert = Bool(d, "CalMeetingAlert", s.CalMeetingAlert);
            s.CalMeetingAlertMin = Int(d, "CalMeetingAlertMin", s.CalMeetingAlertMin, 1, 60);
            s.CalChainLong = Bool(d, "CalChainLong", s.CalChainLong);
            s.CalChainMin = Int(d, "CalChainMin", s.CalChainMin, 30, 480);

            s.GuidedBreak = Bool(d, "GuidedBreak", s.GuidedBreak);
            s.EyeRest = Bool(d, "EyeRest", s.EyeRest);
            s.EyeEveryMin = Int(d, "EyeEveryMin", s.EyeEveryMin, 5, 120);
            s.EyeSeconds = Int(d, "EyeSeconds", s.EyeSeconds, 5, 120);
            s.Greeting = Bool(d, "Greeting", s.Greeting);
            s.UserName = Str(d, "UserName", s.UserName);
            s.DayReport = Bool(d, "DayReport", s.DayReport);
            s.Weather = Bool(d, "Weather", s.Weather);
            s.WeatherCity = Str(d, "WeatherCity", s.WeatherCity);
            s.WeatherLat = Dbl(d, "WeatherLat");
            s.WeatherLon = Dbl(d, "WeatherLon");

            s.TaskAlerts = Bool(d, "TaskAlerts", s.TaskAlerts);
            s.TaskAlertMin = Int(d, "TaskAlertMin", s.TaskAlertMin, 0, 120);
            s.HabitReminders = Bool(d, "HabitReminders", s.HabitReminders);

            s.UpdateOnStart = Bool(d, "UpdateOnStart", s.UpdateOnStart);
            s.UpdateDaily = Bool(d, "UpdateDaily", s.UpdateDaily);
            s.UpdateSkipVersion = Str(d, "UpdateSkipVersion", s.UpdateSkipVersion);

            s.AiOn = Bool(d, "AiOn", s.AiOn);
            if (d.TryGetValue("AiProvider", out var ap) && Enum.TryParse(ap, true, out AiProvider prov)) s.AiProvider = prov;
            s.AiClaudePath = Str(d, "AiClaudePath", s.AiClaudePath);
            s.AiClaudeModel = Str(d, "AiClaudeModel", s.AiClaudeModel);
            s.AiAnthropicKey = ReadSecret(d, "AiAnthropicKey");
            s.AiAnthropicModel = Str(d, "AiAnthropicModel", s.AiAnthropicModel);
            s.AiOpenAiUrl = Str(d, "AiOpenAiUrl", s.AiOpenAiUrl);
            s.AiOpenAiKey = ReadSecret(d, "AiOpenAiKey");
            s.AiOpenAiModel = Str(d, "AiOpenAiModel", s.AiOpenAiModel);
            s.AiOllamaUrl = Str(d, "AiOllamaUrl", s.AiOllamaUrl);
            s.AiOllamaModel = Str(d, "AiOllamaModel", s.AiOllamaModel);
            s.AiHintsFirst = Bool(d, "AiHintsFirst", s.AiHintsFirst);
            s.AiRequireWeather = Bool(d, "AiRequireWeather", s.AiRequireWeather);
            s.PracticeOn = Bool(d, "PracticeOn", s.PracticeOn);
            s.PracticeMorning = Bool(d, "PracticeMorning", s.PracticeMorning);
            s.PracticeMorningMin = Int(d, "PracticeMorningMin", s.PracticeMorningMin, 1, 60);
            s.PracticeDayCount = Int(d, "PracticeDayCount", s.PracticeDayCount, 0, 6);
            s.PracticeDayMin = Int(d, "PracticeDayMin", s.PracticeDayMin, 1, 30);
            s.PracticeCards = Bool(d, "PracticeCards", s.PracticeCards);
            s.WeeklyReview = Bool(d, "WeeklyReview", s.WeeklyReview);
            s.SpeechLang = Str(d, "SpeechLang", s.SpeechLang);
            s.SpeechSlow = Bool(d, "SpeechSlow", s.SpeechSlow);
            s.SpeechAutoCards = Bool(d, "SpeechAutoCards", s.SpeechAutoCards);
            s.ListenInBreak = Bool(d, "ListenInBreak", s.ListenInBreak);
            s.ListenCount = Int(d, "ListenCount", s.ListenCount, 1, 20);
            s.PracticeEvening = Bool(d, "PracticeEvening", s.PracticeEvening);
            s.PracticeEveningAt = Int(d, "PracticeEveningAt", s.PracticeEveningAt, 0, 24 * 60 - 1);
            s.PracticeEveningMin = Int(d, "PracticeEveningMin", s.PracticeEveningMin, 1, 120);

            s.CalendarUrl = ReadSecret(d, "CalendarUrl");
            string? v;
            if (d.TryGetValue("Sound", out v)) s.Sound = SoundCatalog.Normalize(v);
            if (d.TryGetValue("CustomSoundPath", out v)) s.CustomSoundPath = v;
            if (d.TryGetValue("Popup", out v) && Enum.TryParse(v, true, out PopupMode p)) s.Popup = p;

            s.TitleShort = Str(d, "TitleShort", s.TitleShort);
            s.TitleLong = Str(d, "TitleLong", s.TitleLong);
            s.BtnDone = Str(d, "BtnDone", s.BtnDone);
            s.BtnSnooze = Str(d, "BtnSnooze", s.BtnSnooze);
            s.BtnSkip = Str(d, "BtnSkip", s.BtnSkip);
        }
        catch { /* битый файл — остаёмся на значениях по умолчанию */ }
        return s;
    }

    /// <summary>Перечитать файлы советов — их можно править вручную, пока программа работает.</summary>
    public void ReloadTips() => ReloadTips(Paths.TipsFile, Paths.LongTipsFile);

    public void ReloadTips(string tipsFile, string longTipsFile)
    {
        try
        {
            if (File.Exists(tipsFile)) TipsShort = Texts.ParseLines(File.ReadAllText(tipsFile, Encoding.UTF8), Texts.Tips);
            if (File.Exists(longTipsFile)) TipsLong = Texts.ParseLines(File.ReadAllText(longTipsFile, Encoding.UTF8), Texts.LongTips);
        }
        catch { }
    }

    public void Save() => Save(Paths.DataDir, Paths.SettingsFile, Paths.TipsFile, Paths.LongTipsFile);

    public void Save(string dir, string settingsFile, string tipsFile, string longTipsFile)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            void W(string k, object val) => sb.AppendLine(k + "=" + val);
            void B(string k, bool val) => W(k, val ? 1 : 0);

            W("IntervalMin", IntervalMin);
            W("BreakMin", BreakMin);
            W("SnoozeMin", SnoozeMin);
            B("IdleDetect", IdleDetect);
            B("LongBreaks", LongBreaks);
            W("LongEvery", LongEvery);
            W("LongBreakMin", LongBreakMin);
            B("DndCalls", DndCalls);
            B("DndFullscreen", DndFullscreen);
            W("Popup", Popup);
            B("Blink", Blink);
            B("SoundOn", SoundOn);
            W("Sound", Sound);
            W("CustomSoundPath", CustomSoundPath);
            W("Volume", Volume);
            B("RepeatSound", RepeatSound);
            W("RepeatSec", RepeatSec);
            B("AutoDay", AutoDay);
            W("AutoStartMin", AutoStartMin);
            W("AutoEndMin", AutoEndMin);
            B("CloseToTray", CloseToTray);
            B("PauseHotkey", PauseHotkey);
            B("CalendarOn", CalendarOn);
            W("CalendarUrl", WriteSecret(CalendarUrl));
            B("CalWaitMeetings", CalWaitMeetings);
            B("CalBreakBefore", CalBreakBefore);
            W("CalBreakBeforeMin", CalBreakBeforeMin);
            B("CalMeetingAlert", CalMeetingAlert);
            W("CalMeetingAlertMin", CalMeetingAlertMin);
            B("CalChainLong", CalChainLong);
            W("CalChainMin", CalChainMin);
            B("GuidedBreak", GuidedBreak);
            B("EyeRest", EyeRest);
            W("EyeEveryMin", EyeEveryMin);
            W("EyeSeconds", EyeSeconds);
            B("Greeting", Greeting);
            W("UserName", OneLine(UserName));
            B("DayReport", DayReport);
            B("Weather", Weather);
            W("WeatherCity", OneLine(WeatherCity));
            W("WeatherLat", HasWeatherPlace ? WeatherLat.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            W("WeatherLon", HasWeatherPlace ? WeatherLon.ToString(System.Globalization.CultureInfo.InvariantCulture) : "");
            B("TaskAlerts", TaskAlerts);
            W("TaskAlertMin", TaskAlertMin);
            B("HabitReminders", HabitReminders);
            B("UpdateOnStart", UpdateOnStart);
            B("UpdateDaily", UpdateDaily);
            W("UpdateSkipVersion", OneLine(UpdateSkipVersion));
            B("AiOn", AiOn);
            W("AiProvider", AiProvider);
            W("AiClaudePath", OneLine(AiClaudePath));
            W("AiClaudeModel", OneLine(AiClaudeModel));
            W("AiAnthropicKey", WriteSecret(AiAnthropicKey));
            W("AiAnthropicModel", OneLine(AiAnthropicModel));
            W("AiOpenAiUrl", OneLine(AiOpenAiUrl));
            W("AiOpenAiKey", WriteSecret(AiOpenAiKey));
            W("AiOpenAiModel", OneLine(AiOpenAiModel));
            W("AiOllamaUrl", OneLine(AiOllamaUrl));
            W("AiOllamaModel", OneLine(AiOllamaModel));
            B("AiHintsFirst", AiHintsFirst);
            B("AiRequireWeather", AiRequireWeather);
            B("PracticeOn", PracticeOn);
            B("PracticeMorning", PracticeMorning);
            W("PracticeMorningMin", PracticeMorningMin);
            W("PracticeDayCount", PracticeDayCount);
            W("PracticeDayMin", PracticeDayMin);
            B("PracticeCards", PracticeCards);
            B("WeeklyReview", WeeklyReview);
            W("SpeechLang", OneLine(SpeechLang));
            B("SpeechSlow", SpeechSlow);
            B("SpeechAutoCards", SpeechAutoCards);
            B("ListenInBreak", ListenInBreak);
            W("ListenCount", ListenCount);
            B("PracticeEvening", PracticeEvening);
            W("PracticeEveningAt", PracticeEveningAt);
            W("PracticeEveningMin", PracticeEveningMin);
            W("TitleShort", OneLine(TitleShort));
            W("TitleLong", OneLine(TitleLong));
            W("BtnDone", OneLine(BtnDone));
            W("BtnSnooze", OneLine(BtnSnooze));
            W("BtnSkip", OneLine(BtnSkip));

            File.WriteAllText(settingsFile, sb.ToString(), Encoding.UTF8);
            // в файле секреты (ключи, ссылка на календарь): на Linux/macOS — читать может только владелец
            if (!OperatingSystem.IsWindows())
                try { File.SetUnixFileMode(settingsFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
            File.WriteAllText(tipsFile, string.Join(Environment.NewLine, TipsShort) + Environment.NewLine, Encoding.UTF8);
            File.WriteAllText(longTipsFile, string.Join(Environment.NewLine, TipsLong) + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    static int Int(Dictionary<string, string> d, string key, int def, int min, int max) =>
        d.TryGetValue(key, out var v) && int.TryParse(v, out int r) ? Math.Clamp(r, min, max) : def;

    static bool Bool(Dictionary<string, string> d, string key, bool def) =>
        d.TryGetValue(key, out var v) ? v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) : def;

    static double Dbl(Dictionary<string, string> d, string key) =>
        d.TryGetValue(key, out var v) && double.TryParse(v, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double r) ? r : double.NaN;

    static string Str(Dictionary<string, string> d, string key, string def) =>
        d.TryGetValue(key, out var v) && v.Length > 0 ? v : def;

    static string OneLine(string v) => (v ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
}
