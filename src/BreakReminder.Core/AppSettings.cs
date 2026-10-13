using System.Text;

namespace BreakReminder.Core;

public enum PopupMode { Corner, Center, Fullscreen }

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

    // ---- обновления ----
    public bool UpdateOnStart = true;        // проверять при запуске
    public bool UpdateDaily = true;          // и раз в сутки, пока программа работает
    public string UpdateSkipVersion = "";    // «Пропустить эту версию»

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

            s.UpdateOnStart = Bool(d, "UpdateOnStart", s.UpdateOnStart);
            s.UpdateDaily = Bool(d, "UpdateDaily", s.UpdateDaily);
            s.UpdateSkipVersion = Str(d, "UpdateSkipVersion", s.UpdateSkipVersion);

            if (d.TryGetValue("CalendarUrl", out var v)) s.CalendarUrl = v;
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
            W("CalendarUrl", OneLine(CalendarUrl));
            B("CalWaitMeetings", CalWaitMeetings);
            B("CalBreakBefore", CalBreakBefore);
            W("CalBreakBeforeMin", CalBreakBeforeMin);
            B("CalMeetingAlert", CalMeetingAlert);
            W("CalMeetingAlertMin", CalMeetingAlertMin);
            B("CalChainLong", CalChainLong);
            W("CalChainMin", CalChainMin);
            B("UpdateOnStart", UpdateOnStart);
            B("UpdateDaily", UpdateDaily);
            W("UpdateSkipVersion", OneLine(UpdateSkipVersion));
            W("TitleShort", OneLine(TitleShort));
            W("TitleLong", OneLine(TitleLong));
            W("BtnDone", OneLine(BtnDone));
            W("BtnSnooze", OneLine(BtnSnooze));
            W("BtnSkip", OneLine(BtnSkip));

            File.WriteAllText(settingsFile, sb.ToString(), Encoding.UTF8);
            File.WriteAllText(tipsFile, string.Join(Environment.NewLine, TipsShort) + Environment.NewLine, Encoding.UTF8);
            File.WriteAllText(longTipsFile, string.Join(Environment.NewLine, TipsLong) + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    static int Int(Dictionary<string, string> d, string key, int def, int min, int max) =>
        d.TryGetValue(key, out var v) && int.TryParse(v, out int r) ? Math.Clamp(r, min, max) : def;

    static bool Bool(Dictionary<string, string> d, string key, bool def) =>
        d.TryGetValue(key, out var v) ? v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) : def;

    static string Str(Dictionary<string, string> d, string key, string def) =>
        d.TryGetValue(key, out var v) && v.Length > 0 ? v : def;

    static string OneLine(string v) => (v ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
}
