using System.Globalization;
using System.Text;

namespace BreakReminder.Core;

/// <summary>Вид занятия с помощником. Homework — «Сел за домашку» (по кнопке, не по расписанию).</summary>
public enum PracticeKind { Morning, Day, Evening, Homework }

public static class PracticeText
{
    public static string Title(PracticeKind k) => k switch
    {
        PracticeKind.Morning => "Утренняя разминка",
        PracticeKind.Day => "Мини-тренировка",
        PracticeKind.Homework => "Домашнее задание",
        _ => "Вечернее занятие"
    };

    public static int Minutes(PracticeKind k, AppSettings s) => k switch
    {
        PracticeKind.Morning => s.PracticeMorningMin,
        PracticeKind.Day => s.PracticeDayMin,
        PracticeKind.Homework => 0,   // без ограничения по времени
        _ => s.PracticeEveningMin
    };

    const string HomeworkInstructions =
        "Сейчас человек делает домашнее задание. Если задание ещё не прислано — попроси прислать его: фото страницы " +
        "учебника или тетради либо текст, и свои ответы. Разбирай по одному упражнению: найди ошибки и проверь по правилам выше " +
        "(сначала подсказка, если так настроено), задавай встречные вопросы, чтобы проверить понимание. Не решай задание " +
        "за человека целиком — помогай дойти самому. Если на фото плохо видно — так и скажи и попроси переснять. " +
        "Когда человек попросит подвести итог — 2–4 строки: что сделано, где ошибался, что повторить.";

    /// <summary>Что попросить у помощника на этом занятии (добавляется к инструкциям).</summary>
    public static string Instructions(PracticeKind k, int minutes) => k == PracticeKind.Homework ? HomeworkInstructions :
        (k switch
        {
            PracticeKind.Morning =>
                $"Сейчас утренняя разминка на {minutes} мин в начале рабочего дня. Поздоровайся одной фразой на изучаемом языке " +
                "и дай 3–4 лёгких задания: повторение прошлого занятия и типичных ошибок из заметок.",
            PracticeKind.Day =>
                $"Сейчас мини-тренировка на {minutes} мин посреди рабочего дня. Без вступлений: 2–3 быстрых задания " +
                "на то, что человек путает (из заметок) или недавно прошёл.",
            _ =>
                $"Сейчас вечернее занятие на {minutes} мин. Коротко спроси, как дела с текущим юнитом или домашним заданием; " +
                "дальше — немного нового по текущей теме и повторение ошибок из заметок."
        }) +
        "\nДавай задания по одному и жди ответ, проверяй по правилам выше. Чередуй виды: перевод фраз, вставить пропущенное, " +
        "спряжение, короткий ответ на вопрос (для другого хобби — подходящие ему вопросы и мини-задачи). " +
        "Если из профиля и заметок непонятно, чем человек занимается, — сначала спроси. " +
        "Когда человек попросит подвести итог — 2–3 строки: что получилось, что повторить в следующий раз.";
}

/// <summary>
/// Журнал занятий (assistant/practice.csv): дата и время, вид, минуты, сделано или пропущено.
/// По нему — что уже было сегодня, минуты за день и серия дней подряд.
/// </summary>
public sealed class PracticeLog
{
    public sealed record Entry(DateTime At, PracticeKind Kind, int Minutes, bool Skipped);

    readonly string file;
    readonly List<Entry> entries;

    public PracticeLog() : this(Path.Combine(Paths.DataDir, "assistant", "practice.csv")) { }

    public PracticeLog(string file)
    {
        this.file = file;
        entries = Load(file);
    }

    public IReadOnlyList<Entry> Entries => entries;

    public void Done(DateTime at, PracticeKind kind, int minutes) => Append(new Entry(at, kind, Math.Max(1, minutes), false));

    public void Skip(DateTime at, PracticeKind kind) => Append(new Entry(at, kind, 0, true));

    public IEnumerable<Entry> On(DateTime day) => entries.Where(e => e.At.Date == day.Date);

    public int MinutesOn(DateTime day) => On(day).Sum(e => e.Minutes);

    /// <summary>Сколько дней подряд были занятия — по сегодня (или по вчера, если сегодня ещё не занимался).</summary>
    public int Streak(DateTime today)
    {
        var days = entries.Where(e => !e.Skipped).Select(e => e.At.Date).ToHashSet();
        var d = days.Contains(today.Date) ? today.Date : today.Date.AddDays(-1);
        int n = 0;
        while (days.Contains(d)) { n++; d = d.AddDays(-1); }
        return n;
    }

    /// <summary>Самая длинная серия дней подряд за всё время.</summary>
    public int BestStreak()
    {
        var days = entries.Where(e => !e.Skipped).Select(e => e.At.Date).Distinct().OrderBy(d => d).ToList();
        int best = 0, run = 0;
        for (int i = 0; i < days.Count; i++)
        {
            run = i > 0 && days[i] == days[i - 1].AddDays(1) ? run + 1 : 1;
            best = Math.Max(best, run);
        }
        return best;
    }

    void Append(Entry e)
    {
        entries.Add(e);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.AppendAllText(file,
                $"{e.At.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)},{e.Kind},{e.Minutes},{(e.Skipped ? "skipped" : "done")}\n",
                Encoding.UTF8);
        }
        catch { }
    }

    static List<Entry> Load(string file)
    {
        var list = new List<Entry>();
        try
        {
            if (!File.Exists(file)) return list;
            foreach (var line in File.ReadAllLines(file, Encoding.UTF8))
            {
                var p = line.Split(',');
                if (p.Length < 4) continue;
                if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
                if (!Enum.TryParse(p[1], out PracticeKind kind) || !int.TryParse(p[2], out int min)) continue;
                list.Add(new Entry(at, kind, min, p[3].Trim() == "skipped"));
            }
        }
        catch { }
        return list;
    }
}

/// <summary>
/// Когда предложить занятие. Разминка — через 10 мин после начала рабочего дня; мини-тренировки — не раньше
/// чем через 1,5 ч после начала дня и с промежутком от 2 ч; вечернее — в заданное время (и вне рабочего дня).
/// Только в удобный момент: человек за компьютером, нет встречи, созвона или полноэкранной программы,
/// не на паузе и не перед самым перерывом. «Позже» — через 30 мин, «Не сегодня» — до завтра.
/// </summary>
public sealed class PracticeCoach
{
    public static readonly TimeSpan MorningDelay = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MorningWindow = TimeSpan.FromHours(3);
    public static readonly TimeSpan DayFirst = TimeSpan.FromMinutes(90);
    public static readonly TimeSpan DayGap = TimeSpan.FromHours(2);
    public static readonly TimeSpan EveningWindow = TimeSpan.FromHours(3);
    public static readonly TimeSpan SnoozeTime = TimeSpan.FromMinutes(30);

    readonly WorkDayEngine engine;
    readonly IEnvironmentProbe probe;
    readonly PracticeLog log;
    readonly Dictionary<PracticeKind, DateTime> snoozed = new();
    readonly HashSet<PracticeKind> startedToday = new();   // начатые занятия (журнал пишется по окончании)
    DateTime lastDayStartedAt = DateTime.MinValue;
    int dayStarted;           // начато мини-тренировок сегодня (в этом запуске)
    DateTime today;

    public PracticeCoach(AppSettings settings, WorkDayEngine engine, IEnvironmentProbe probe, PracticeLog log)
    {
        Settings = settings;
        this.engine = engine;
        this.probe = probe;
        this.log = log;
    }

    public AppSettings Settings { get; set; }

    /// <summary>Сейчас открыто предложение или идёт занятие — новых не предлагаем (задаёт приложение).</summary>
    public bool Busy { get; set; }

    /// <summary>Пора предложить занятие.</summary>
    public event Action<PracticeKind>? Due;

    bool Enabled => Settings.AiOn && Settings.PracticeOn;

    public void Tick(DateTime now)
    {
        if (now.Date != today)
        {
            today = now.Date;
            snoozed.Clear();
            startedToday.Clear();
            dayStarted = 0;
        }
        if (!Enabled || Busy) return;
        if (Next(now) is { } kind && GoodMoment(now, kind))
        {
            Busy = true;
            Due?.Invoke(kind);
        }
    }

    /// <summary>Какое занятие сейчас по расписанию (без проверки, удобный ли момент).</summary>
    public PracticeKind? Next(DateTime now)
    {
        bool running = engine.State == WorkState.Running;
        var s = Settings;

        if (s.PracticeEvening && !Handled(PracticeKind.Evening, now))
        {
            var at = now.Date + TimeSpan.FromMinutes(s.PracticeEveningAt);
            if (now >= at && now < at + EveningWindow && Ready(PracticeKind.Evening, now)) return PracticeKind.Evening;
        }
        if (!running) return null;

        var start = engine.DayStart;
        if (s.PracticeMorning && !Handled(PracticeKind.Morning, now)
            && now >= start + MorningDelay && now < start + MorningWindow && Ready(PracticeKind.Morning, now))
            return PracticeKind.Morning;

        bool daySkipped = log.On(now).Any(e => e.Kind == PracticeKind.Day && e.Skipped);   // «не сегодня»
        if (s.PracticeDayCount > 0 && !daySkipped && DayCount(now) < s.PracticeDayCount && Ready(PracticeKind.Day, now))
        {
            var last = new[] { start + DayFirst - DayGap, lastDayStartedAt, LastLogged(PracticeKind.Day, now) ?? DateTime.MinValue }.Max();
            bool beforeEvening = !s.PracticeEvening || now < now.Date + TimeSpan.FromMinutes(s.PracticeEveningAt) - TimeSpan.FromHours(1);
            if (now >= start + DayFirst && now >= last + DayGap && beforeEvening) return PracticeKind.Day;
        }
        return null;
    }

    /// <summary>Удобный момент: человек за компьютером и ничем срочным не занят.</summary>
    public bool GoodMoment(DateTime now, PracticeKind kind)
    {
        if (probe.IsScreenLocked() || probe.IdleTime() >= TimeSpan.FromMinutes(2)) return false;
        if (engine.State == WorkState.Paused) return false;
        if (engine.IsBusy(now)) return false;   // встреча, созвон, полноэкранная программа
        if (engine.State == WorkState.Running)
        {
            if (engine.ReminderOpen || engine.OnBreak || engine.Away) return false;
            // не перед самым перерывом: занятие должно успеть закончиться
            if (engine.Remaining < TimeSpan.FromMinutes(PracticeText.Minutes(kind, Settings) + 5)) return false;
        }
        return true;
    }

    /// <summary>Человек начал занятие.</summary>
    public void Started(PracticeKind kind, DateTime now)
    {
        startedToday.Add(kind);
        if (kind == PracticeKind.Day)
        {
            lastDayStartedAt = now;
            dayStarted++;
        }
        snoozed.Remove(kind);
    }

    /// <summary>Занятие закончилось (минуты — сколько длилось на самом деле).</summary>
    public void Finished(PracticeKind kind, DateTime now, int minutes)
    {
        log.Done(now, kind, minutes);
        Busy = false;
    }

    /// <summary>Начал, но так и не ответил ни на одно задание — не засчитываем, предложим позже.</summary>
    public void Abandoned(PracticeKind kind, DateTime now)
    {
        startedToday.Remove(kind);
        if (kind == PracticeKind.Day && dayStarted > 0) dayStarted--;
        Snooze(kind, now);
    }

    /// <summary>«Позже» — предложить снова через 30 минут.</summary>
    public void Snooze(PracticeKind kind, DateTime now)
    {
        snoozed[kind] = now + SnoozeTime;
        Busy = false;
    }

    /// <summary>«Не сегодня».</summary>
    public void Skip(PracticeKind kind, DateTime now)
    {
        log.Skip(now, kind);
        Busy = false;
    }

    /// <summary>План на день для утреннего приветствия.</summary>
    public string? PlanLine(DateTime dayStart)
    {
        if (!Enabled) return null;
        var parts = new List<string>();
        if (Settings.PracticeMorning) parts.Add($"разминка около {dayStart + MorningDelay:HH:mm}");
        if (Settings.PracticeDayCount > 0) parts.Add(Settings.PracticeDayCount == 1 ? "мини-тренировка днём" : $"{Settings.PracticeDayCount} мини-тренировки днём");
        if (Settings.PracticeEvening) parts.Add($"вечером в {dayStart.Date + TimeSpan.FromMinutes(Settings.PracticeEveningAt):HH:mm}");
        if (parts.Count == 0) return null;
        int streak = log.Streak(dayStart);
        return "Занятия: " + string.Join(", ", parts) + (streak > 0 ? $" · серия {streak} дн." : "");
    }

    bool Handled(PracticeKind kind, DateTime now) => startedToday.Contains(kind) || log.On(now).Any(e => e.Kind == kind);

    bool Ready(PracticeKind kind, DateTime now) => !snoozed.TryGetValue(kind, out var until) || now >= until;

    /// <summary>Сколько мини-тренировок уже было сегодня (журнал переживает перезапуск, счётчик — идущее занятие).</summary>
    int DayCount(DateTime now) => Math.Max(log.On(now).Count(e => e.Kind == PracticeKind.Day && !e.Skipped), dayStarted);

    DateTime? LastLogged(PracticeKind kind, DateTime now) =>
        log.On(now).Where(e => e.Kind == kind).Select(e => (DateTime?)e.At).DefaultIfEmpty(null).Max();
}
