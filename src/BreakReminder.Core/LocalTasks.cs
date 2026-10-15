using System.Text.Json;

namespace BreakReminder.Core;

/// <summary>Своя задача (хранится локально, tasks.json).</summary>
public sealed class LocalTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Date { get; set; }          // день задачи
    public TimeSpan? Time { get; set; }         // время, если задача «ко времени»
    public string Title { get; set; } = "";
    public string Note { get; set; } = "";
    public bool Done { get; set; }

    /// <summary>Момент задачи, если у неё есть время.</summary>
    public DateTime? At => Time is { } t ? Date.Date + t : null;

    /// <summary>Ключ для «уже напоминали» — меняется, если задачу перенесли.</summary>
    public string AlertKey => $"{Id}|{At:O}";
}

/// <summary>Свои задачи: загрузка, сохранение и выборка «на сегодня».</summary>
public sealed class TaskStore
{
    readonly string file;
    readonly List<LocalTask> items;

    public TaskStore() : this(Path.Combine(Paths.DataDir, "tasks.json")) { }

    public TaskStore(string file)
    {
        this.file = file;
        items = Load(file);
    }

    public IReadOnlyList<LocalTask> All => items;

    // русские буквы — как есть, а не У…: файл можно читать и править руками
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
    };

    /// <summary>Изменились задачи (добавили/отметили/удалили) — для обновления окон.</summary>
    public event Action? Changed;

    static List<LocalTask> Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return new();
            var list = JsonSerializer.Deserialize<List<LocalTask>>(File.ReadAllText(file)) ?? new();
            // старые выполненные (больше месяца) не храним
            var cutoff = DateTime.Today.AddDays(-30);
            return list.Where(t => !(t.Done && t.Date < cutoff)).ToList();
        }
        catch { return new(); }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(items, JsonOptions));
        }
        catch { }
        Changed?.Invoke();
    }

    public LocalTask Add(DateTime date, string title, TimeSpan? time, string note)
    {
        var t = new LocalTask { Date = date.Date, Title = title.Trim(), Time = time, Note = (note ?? "").Trim() };
        items.Add(t);
        Save();
        return t;
    }

    public void SetDone(string id, bool done)
    {
        var t = items.FirstOrDefault(x => x.Id == id);
        if (t == null || t.Done == done) return;
        t.Done = done;
        Save();
    }

    public void Remove(string id)
    {
        if (items.RemoveAll(x => x.Id == id) > 0) Save();
    }

    /// <summary>
    /// Задачи на день: этого дня + невыполненные без времени с прошлых дней (за последние 2 недели) —
    /// чтобы недоделанное не терялось.
    /// </summary>
    public List<LocalTask> ForDay(DateTime day)
    {
        day = day.Date;
        return items
            .Where(t => t.Date == day || (!t.Done && t.Time == null && t.Date < day && t.Date >= day.AddDays(-14)))
            .OrderBy(t => t.Done)
            .ThenBy(t => t.At ?? DateTime.MaxValue)
            .ThenBy(t => t.Date)
            .ToList();
    }
}
