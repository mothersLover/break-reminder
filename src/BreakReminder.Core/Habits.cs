using System.Globalization;

namespace BreakReminder.Core;

/// <summary>Когда напоминать о привычке.</summary>
public enum HabitRemind { None, AtTime, Every }

/// <summary>Привычка: «💧 Вода — 8 раз в день, напоминать каждые 90 минут в рабочий день».</summary>
public sealed class Habit
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Emoji { get; set; } = "✅";
    public string Title { get; set; } = "";
    public int Target { get; set; } = 1;              // сколько раз в день (1 — просто «сделал»)
    public HabitRemind Remind { get; set; }
    public int RemindAt { get; set; } = 13 * 60;      // AtTime: минуты от полуночи
    public int EveryMin { get; set; } = 90;           // Every: интервал (только пока идёт рабочий день)
    public DateTime Created { get; set; }

    public string Label => $"{Emoji} {Title}".Trim();
}

/// <summary>
/// Привычки и их отметки (habits.json рядом с настройками): сколько раз сделано за день, «не сегодня», серии.
/// </summary>
public sealed class HabitStore
{
    sealed class FileModel
    {
        public List<Habit> Habits { get; set; } = new();
        /// <summary>«yyyy-MM-dd» → id → сколько раз сделано.</summary>
        public Dictionary<string, Dictionary<string, int>> Log { get; set; } = new();
        /// <summary>«yyyy-MM-dd|id» — «не сегодня».</summary>
        public HashSet<string> Skipped { get; set; } = new();
    }

    readonly string file;
    readonly FileModel data;

    public HabitStore() : this(Path.Combine(Paths.DataDir, "habits.json")) { }

    public HabitStore(string file)
    {
        this.file = file;
        data = Json.Load<FileModel>(file) ?? new FileModel();
        // старые отметки (больше года) не храним
        var cutoff = DateTime.Today.AddDays(-400);
        foreach (var day in data.Log.Keys.Where(k => Day(k) < cutoff).ToList()) data.Log.Remove(day);
    }

    public IReadOnlyList<Habit> All => data.Habits;

    /// <summary>Привычки или отметки изменились — для обновления окон.</summary>
    public event Action? Changed;

    static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static DateTime Day(string key) => DateTime.TryParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.MinValue;

    public Habit Add(Habit h)
    {
        h.Title = h.Title.Trim();
        h.Target = Math.Clamp(h.Target, 1, 50);
        data.Habits.Add(h);
        Save();
        return h;
    }

    /// <summary>Сохранить изменённую привычку (поля уже поменяны снаружи).</summary>
    public void Update(Habit h)
    {
        h.Target = Math.Clamp(h.Target, 1, 50);
        Save();
    }

    public void Remove(string id)
    {
        if (data.Habits.RemoveAll(h => h.Id == id) == 0) return;
        foreach (var day in data.Log.Values) day.Remove(id);
        Save();
    }

    public int Count(string id, DateTime day) =>
        data.Log.TryGetValue(Key(day), out var d) && d.TryGetValue(id, out var n) ? n : 0;

    public bool IsDone(Habit h, DateTime day) => Count(h.Id, day) >= h.Target;

    public bool IsSkipped(string id, DateTime day) => data.Skipped.Contains(Key(day) + "|" + id);

    /// <summary>+1 (или −1): «выпил стакан». Возвращает новое значение.</summary>
    public int Mark(string id, DateTime day, int delta = 1)
    {
        var key = Key(day);
        if (!data.Log.TryGetValue(key, out var d)) data.Log[key] = d = new();
        int n = Math.Max(0, (d.TryGetValue(id, out var cur) ? cur : 0) + delta);
        if (n == 0) d.Remove(id); else d[id] = n;
        if (delta > 0) data.Skipped.Remove(key + "|" + id);   // сделал — значит уже не «не сегодня»
        Save();
        return n;
    }

    public void Skip(string id, DateTime day)
    {
        if (data.Skipped.Add(Key(day) + "|" + id)) Save();
    }

    /// <summary>Сколько дней подряд цель выполнена — по сегодня, а если сегодня ещё нет — по вчера.</summary>
    public int Streak(Habit h, DateTime today)
    {
        var d = IsDone(h, today) ? today.Date : today.Date.AddDays(-1);
        int n = 0;
        while (IsDone(h, d)) { n++; d = d.AddDays(-1); }
        return n;
    }

    /// <summary>Сколько привычек выполнено за день и сколько всего.</summary>
    public (int Done, int Total) Progress(DateTime day) => (data.Habits.Count(h => IsDone(h, day)), data.Habits.Count);

    /// <summary>Для утреннего приветствия: «💧 Вода ×8, 🚶 Прогулка в 13:00».</summary>
    public string? PlanLine()
    {
        if (data.Habits.Count == 0) return null;
        return "Привычки: " + string.Join(", ", data.Habits.Select(h =>
            h.Label + (h.Target > 1 ? $" ×{h.Target}" : "") + (h.Remind == HabitRemind.AtTime ? $" в {TimeSpan.FromMinutes(h.RemindAt):hh\\:mm}" : "")));
    }

    void Save()
    {
        // «не сегодня» дольше недели не нужно
        var weekAgo = DateTime.Today.AddDays(-7);
        data.Skipped.RemoveWhere(k => Day(k.Split('|')[0]) < weekAgo);
        Json.Save(file, data);
        Changed?.Invoke();
    }

    /// <summary>Готовые заготовки для окна настройки.</summary>
    public static IReadOnlyList<Habit> Presets() => new[]
    {
        new Habit { Emoji = "💧", Title = "Вода", Target = 8, Remind = HabitRemind.Every, EveryMin = 90 },
        new Habit { Emoji = "🚶", Title = "Прогулка", Target = 1, Remind = HabitRemind.AtTime, RemindAt = 13 * 60 },
        new Habit { Emoji = "🤸", Title = "Зарядка", Target = 1, Remind = HabitRemind.AtTime, RemindAt = 10 * 60 + 30 },
        new Habit { Emoji = "📖", Title = "Чтение", Target = 1, Remind = HabitRemind.AtTime, RemindAt = 21 * 60 },
        new Habit { Emoji = "🧘", Title = "Растяжка", Target = 1, Remind = HabitRemind.AtTime, RemindAt = 18 * 60 }
    };
}

/// <summary>
/// Напоминания о привычках в удобный момент: человек за компьютером, нет встречи/созвона/полноэкранной программы,
/// не открыто напоминание о перерыве (а сам перерыв — как раз хорошее время выпить воды).
/// «В заданное время» — в течение 3 часов после него; «каждые N минут» — только пока идёт рабочий день,
/// отсчёт от начала дня или от последней отметки. «Позже» — через 30 минут.
/// </summary>
public sealed class HabitCoach
{
    public static readonly TimeSpan AtTimeWindow = TimeSpan.FromHours(3);
    public static readonly TimeSpan SnoozeTime = TimeSpan.FromMinutes(30);

    readonly HabitStore store;
    readonly WorkDayEngine engine;
    readonly IEnvironmentProbe probe;
    readonly Dictionary<string, DateTime> last = new();      // последнее напоминание или отметка
    readonly Dictionary<string, DateTime> snoozed = new();
    readonly HashSet<string> remindedToday = new();          // «в заданное время» — один раз в день
    DateTime today;

    public HabitCoach(HabitStore store, WorkDayEngine engine, IEnvironmentProbe probe)
    {
        this.store = store;
        this.engine = engine;
        this.probe = probe;
    }

    public bool Enabled { get; set; } = true;

    /// <summary>Сейчас открыто напоминание — следующих не показываем (задаёт приложение).</summary>
    public bool Busy { get; set; }

    public event Action<Habit>? Due;

    /// <summary>Отметили привычку (из любого окна) — отсчёт «каждые N минут» начинается заново.</summary>
    public void Marked(string id, DateTime now) => last[id] = now;

    public void Snooze(string id, DateTime now)
    {
        snoozed[id] = now + SnoozeTime;
        Busy = false;
    }

    public void Answered() => Busy = false;

    public void Tick(DateTime now)
    {
        if (now.Date != today)
        {
            today = now.Date;
            remindedToday.Clear();
            snoozed.Clear();
        }
        if (!Enabled || Busy || !GoodMoment(now)) return;
        foreach (var h in store.All)
        {
            if (h.Remind == HabitRemind.None || store.IsDone(h, now) || store.IsSkipped(h.Id, now)) continue;
            if (snoozed.TryGetValue(h.Id, out var until) && now < until) continue;
            if (!IsTime(h, now)) continue;
            last[h.Id] = now;
            snoozed.Remove(h.Id);   // «позже» отработало
            if (h.Remind == HabitRemind.AtTime) remindedToday.Add(h.Id);
            Busy = true;
            Due?.Invoke(h);
            return;
        }
    }

    bool IsTime(Habit h, DateTime now)
    {
        bool wasSnoozed = snoozed.ContainsKey(h.Id);
        if (h.Remind == HabitRemind.AtTime)
        {
            var at = now.Date + TimeSpan.FromMinutes(h.RemindAt);
            return now >= at && now < at + AtTimeWindow && (!remindedToday.Contains(h.Id) || wasSnoozed);
        }
        // каждые N минут — пока идёт рабочий день
        if (engine.State != WorkState.Running) return false;
        var from = engine.DayStart;
        if (last.TryGetValue(h.Id, out var l) && l > from) from = l;
        return now - from >= TimeSpan.FromMinutes(Math.Max(10, h.EveryMin)) || (wasSnoozed && now >= snoozed[h.Id]);
    }

    bool GoodMoment(DateTime now)
    {
        if (probe.IsScreenLocked() || probe.IdleTime() >= TimeSpan.FromMinutes(2)) return false;
        if (engine.IsBusy(now) || engine.ReminderOpen && !engine.OnBreak) return false;
        return true;
    }
}
