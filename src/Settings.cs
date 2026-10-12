using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BreakReminder
{
    public enum PopupMode { Corner, Center, Fullscreen }

    public class AppSettings
    {
        public int IntervalMin = 50;
        public int BreakMin = 5;
        public int SnoozeMin = 5;
        public bool IdleDetect = true;

        public bool LongBreaks = true;
        public int LongEvery = 4;
        public int LongBreakMin = 15;

        public bool DndCalls = true;
        public bool DndFullscreen = true;
        public bool PauseHotkey = true;

        public bool AutoDay = false;
        public int AutoStartMin = 10 * 60;  // 10:00
        public int AutoEndMin = 22 * 60;    // 22:00

        public PopupMode Popup = PopupMode.Corner;
        public bool Blink = true;

        public bool SoundOn = true;
        public string Sound = "chime";
        public string CustomSoundPath = "";
        public int Volume = 70;
        public bool RepeatSound = true;
        public int RepeatSec = 60;

        public bool CloseToTray = true;

        // Тексты уведомлений (советы хранятся в отдельных файлах tips.txt / tips_long.txt)
        public string TitleShort = Texts.TitleShort;
        public string TitleLong = Texts.TitleLong;
        public string[] TipsShort = Texts.Tips;
        public string[] TipsLong = Texts.LongTips;
        public string BtnDone = Texts.BtnDone;
        public string BtnSnooze = Texts.BtnSnooze;
        public string BtnSkip = Texts.BtnSkip;

        public TimeSpan Interval { get { return TimeSpan.FromMinutes(IntervalMin); } }
        public TimeSpan BreakSpan { get { return TimeSpan.FromMinutes(BreakMin); } }
        public TimeSpan Snooze { get { return TimeSpan.FromMinutes(SnoozeMin); } }
        public TimeSpan LongBreakSpan { get { return TimeSpan.FromMinutes(LongBreakMin); } }

        public static string DataDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BreakReminder");
            }
        }

        static string FilePath { get { return Path.Combine(DataDir, "settings.ini"); } }
        static string TipsPath { get { return Path.Combine(DataDir, "tips.txt"); } }
        static string LongTipsPath { get { return Path.Combine(DataDir, "tips_long.txt"); } }

        public AppSettings Clone()
        {
            return (AppSettings)MemberwiseClone();
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            s.ReloadTips();
            try
            {
                if (!File.Exists(FilePath)) return s;
                var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
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
                s.PauseHotkey = Bool(d, "PauseHotkey", s.PauseHotkey);
                s.AutoDay = Bool(d, "AutoDay", s.AutoDay);
                s.AutoStartMin = Int(d, "AutoStartMin", s.AutoStartMin, 0, 24 * 60 - 1);
                s.AutoEndMin = Int(d, "AutoEndMin", s.AutoEndMin, 0, 24 * 60 - 1);
                s.Blink = Bool(d, "Blink", s.Blink);
                s.SoundOn = Bool(d, "SoundOn", s.SoundOn);
                s.Volume = Int(d, "Volume", s.Volume, 0, 100);
                s.RepeatSound = Bool(d, "RepeatSound", s.RepeatSound);
                s.RepeatSec = Int(d, "RepeatSec", s.RepeatSec, 10, 600);
                s.CloseToTray = Bool(d, "CloseToTray", s.CloseToTray);
                string v;
                if (d.TryGetValue("Sound", out v) && Array.IndexOf(Sounds.Keys, v) >= 0) s.Sound = v;
                if (d.TryGetValue("CustomSoundPath", out v)) s.CustomSoundPath = v;
                s.TitleShort = Str(d, "TitleShort", s.TitleShort);
                s.TitleLong = Str(d, "TitleLong", s.TitleLong);
                s.BtnDone = Str(d, "BtnDone", s.BtnDone);
                s.BtnSnooze = Str(d, "BtnSnooze", s.BtnSnooze);
                s.BtnSkip = Str(d, "BtnSkip", s.BtnSkip);
                if (d.TryGetValue("Popup", out v))
                {
                    try { s.Popup = (PopupMode)Enum.Parse(typeof(PopupMode), v, true); } catch { }
                }
            }
            catch { }
            return s;
        }

        /// <summary>Перечитать tips.txt / tips_long.txt — их можно править и вручную, пока программа работает.</summary>
        public void ReloadTips()
        {
            try
            {
                if (File.Exists(TipsPath)) TipsShort = Texts.ParseLines(File.ReadAllText(TipsPath, Encoding.UTF8), Texts.Tips);
                if (File.Exists(LongTipsPath)) TipsLong = Texts.ParseLines(File.ReadAllText(LongTipsPath, Encoding.UTF8), Texts.LongTips);
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var sb = new StringBuilder();
                sb.AppendLine("IntervalMin=" + IntervalMin);
                sb.AppendLine("BreakMin=" + BreakMin);
                sb.AppendLine("SnoozeMin=" + SnoozeMin);
                sb.AppendLine("IdleDetect=" + (IdleDetect ? 1 : 0));
                sb.AppendLine("LongBreaks=" + (LongBreaks ? 1 : 0));
                sb.AppendLine("LongEvery=" + LongEvery);
                sb.AppendLine("LongBreakMin=" + LongBreakMin);
                sb.AppendLine("DndCalls=" + (DndCalls ? 1 : 0));
                sb.AppendLine("DndFullscreen=" + (DndFullscreen ? 1 : 0));
                sb.AppendLine("PauseHotkey=" + (PauseHotkey ? 1 : 0));
                sb.AppendLine("AutoDay=" + (AutoDay ? 1 : 0));
                sb.AppendLine("AutoStartMin=" + AutoStartMin);
                sb.AppendLine("AutoEndMin=" + AutoEndMin);
                sb.AppendLine("Popup=" + Popup);
                sb.AppendLine("Blink=" + (Blink ? 1 : 0));
                sb.AppendLine("SoundOn=" + (SoundOn ? 1 : 0));
                sb.AppendLine("Sound=" + Sound);
                sb.AppendLine("CustomSoundPath=" + CustomSoundPath);
                sb.AppendLine("Volume=" + Volume);
                sb.AppendLine("RepeatSound=" + (RepeatSound ? 1 : 0));
                sb.AppendLine("RepeatSec=" + RepeatSec);
                sb.AppendLine("CloseToTray=" + (CloseToTray ? 1 : 0));
                sb.AppendLine("TitleShort=" + OneLine(TitleShort));
                sb.AppendLine("TitleLong=" + OneLine(TitleLong));
                sb.AppendLine("BtnDone=" + OneLine(BtnDone));
                sb.AppendLine("BtnSnooze=" + OneLine(BtnSnooze));
                sb.AppendLine("BtnSkip=" + OneLine(BtnSkip));
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
                File.WriteAllText(TipsPath, string.Join(Environment.NewLine, TipsShort) + Environment.NewLine, Encoding.UTF8);
                File.WriteAllText(LongTipsPath, string.Join(Environment.NewLine, TipsLong) + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        static int Int(Dictionary<string, string> d, string key, int def, int min, int max)
        {
            string v;
            int r;
            if (d.TryGetValue(key, out v) && int.TryParse(v, out r)) return Math.Max(min, Math.Min(max, r));
            return def;
        }

        static string Str(Dictionary<string, string> d, string key, string def)
        {
            string v;
            return d.TryGetValue(key, out v) && v.Length > 0 ? v : def;
        }

        static string OneLine(string v)
        {
            return (v ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        }

        static bool Bool(Dictionary<string, string> d, string key, bool def)
        {
            string v;
            if (!d.TryGetValue(key, out v)) return def;
            return v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Стандартные тексты уведомлений.</summary>
    static class Texts
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
        public static string[] ParseLines(string text, string[] fallback)
        {
            var list = new List<string>();
            foreach (string line in (text ?? "").Split('\n'))
            {
                string t = line.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list.Count > 0 ? list.ToArray() : fallback;
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "BreakReminder";

        public static bool Get()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(Name) != null;
            }
            catch { return false; }
        }

        public static void Set(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\" --tray");
                    else k.DeleteValue(Name, false);
                }
            }
            catch { }
        }
    }
}
