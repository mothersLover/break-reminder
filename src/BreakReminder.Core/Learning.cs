using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BreakReminder.Core;

/// <summary>Цель со сроком: «закончить Unité 6 к 15 ноября».</summary>
public sealed class Goal
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public DateTime Deadline { get; set; }
    public bool Done { get; set; }
    public DateTime Created { get; set; }

    public int DaysLeft(DateTime today) => (Deadline.Date - today.Date).Days;
}

/// <summary>Цели (assistant/goals.json). Помощник видит активные цели и сколько дней осталось.</summary>
public sealed class GoalStore
{
    readonly string file;
    readonly List<Goal> goals;

    public GoalStore() : this(Path.Combine(Paths.DataDir, "assistant", "goals.json")) { }

    public GoalStore(string file)
    {
        this.file = file;
        goals = Json.Load<List<Goal>>(file) ?? new();
    }

    public IReadOnlyList<Goal> All => goals;

    /// <summary>Невыполненные — по сроку.</summary>
    public IEnumerable<Goal> Active => goals.Where(g => !g.Done).OrderBy(g => g.Deadline);

    public event Action? Changed;

    public Goal? Add(string title, DateTime deadline, DateTime now)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0) return null;
        var g = new Goal { Title = title, Deadline = deadline.Date, Created = now };
        goals.Add(g);
        Save();
        return g;
    }

    public void SetDone(string id, bool done)
    {
        var g = goals.FirstOrDefault(x => x.Id == id);
        if (g == null || g.Done == done) return;
        g.Done = done;
        Save();
    }

    public void Remove(string id)
    {
        if (goals.RemoveAll(g => g.Id == id) > 0) Save();
    }

    /// <summary>Для инструкций помощника: «- Закончить Unité 6 — до 15.11 (осталось 22 дн.)».</summary>
    public string PromptLines(DateTime today) => string.Join("\n", Active.Select(g =>
    {
        int left = g.DaysLeft(today);
        string when = left > 0 ? $"осталось {left} дн." : left == 0 ? "срок сегодня" : $"срок прошёл {-left} дн. назад";
        return $"- {g.Title} — до {g.Deadline:dd.MM.yyyy} ({when})";
    }));

    void Save()
    {
        Json.Save(file, goals);
        Changed?.Invoke();
    }
}

/// <summary>Сводка по учёбе для вкладки «Учёба» и итогов недели.</summary>
public sealed record LearningStats(
    IReadOnlyDictionary<DateTime, int> MinutesByDay,   // за последние Days дней (включая сегодня)
    int WeekMinutes, int PrevWeekMinutes,
    int Streak, int BestStreak,
    int CardsTotal, int CardsLearned, int CardsNew, int CardsDue,
    IReadOnlyList<Flashcard> TopMistakes,
    IReadOnlyDictionary<PracticeKind, int> WeekSessions)
{
    public const int Days = 28;
    /// <summary>Карточка «выучена», если интервал повторения — от трёх недель.</summary>
    public const double LearnedIntervalDays = 21;

    public static LearningStats Build(PracticeLog log, CardStore cards, DateTime now)
    {
        var today = now.Date;
        var byDay = new Dictionary<DateTime, int>();
        for (int i = Days - 1; i >= 0; i--) byDay[today.AddDays(-i)] = log.MinutesOn(today.AddDays(-i));
        int Sum(DateTime from, DateTime to) => log.Entries.Where(e => e.At.Date >= from && e.At.Date <= to).Sum(e => e.Minutes);
        var weekFrom = today.AddDays(-6);
        var sessions = log.Entries.Where(e => !e.Skipped && e.At.Date >= weekFrom)
            .GroupBy(e => e.Kind).ToDictionary(g => g.Key, g => g.Count());
        return new LearningStats(
            byDay, Sum(weekFrom, today), Sum(today.AddDays(-13), today.AddDays(-7)),
            log.Streak(today), log.BestStreak(),
            cards.All.Count, cards.All.Count(c => c.IntervalDays >= LearnedIntervalDays), cards.All.Count(c => c.IsNew), cards.DueCount(now),
            cards.All.Where(c => c.Lapses > 0).OrderByDescending(c => c.Lapses).ThenByDescending(c => c.LastReview).Take(5).ToList(),
            sessions);
    }

    /// <summary>Коротко текстом — для итогов недели.</summary>
    public string Summary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Занятия за 7 дней: {WeekMinutes} мин (неделей раньше — {PrevWeekMinutes} мин).");
        if (WeekSessions.Count > 0)
            sb.AppendLine("Из них: " + string.Join(", ", WeekSessions.Select(kv => $"{PracticeText.Title(kv.Key).ToLowerInvariant()} — {kv.Value}")) + ".");
        sb.AppendLine($"Серия дней подряд: {Streak} (лучшая — {BestStreak}).");
        sb.AppendLine($"Карточки: всего {CardsTotal}, выучено {CardsLearned}, новых {CardsNew}, к повторению сейчас {CardsDue}.");
        if (TopMistakes.Count > 0)
            sb.AppendLine("Чаще всего забывает: " + string.Join("; ", TopMistakes.Select(c => $"{c.Front} → {c.Back} ({c.Lapses} раз)")) + ".");
        return sb.ToString().TrimEnd();
    }
}

/// <summary>Итоги недели от помощника.</summary>
public sealed class WeeklyReview
{
    public DateTime WeekStart { get; set; }       // понедельник недели, о которой итоги
    public DateTime CreatedAt { get; set; }
    public string Text { get; set; } = "";        // разбор (без блока плана)
    public string Plan { get; set; } = "";        // план на следующую неделю — можно закрепить
}

/// <summary>
/// Итоги недели (assistant/weekly.json): когда пора (воскресенье с 18:00, а если пропустил — до вторника включительно),
/// запрос к помощнику и разбор ответа.
/// </summary>
public sealed class WeeklyReviewStore
{
    public static readonly TimeSpan SundayFrom = TimeSpan.FromHours(18);

    readonly string file;
    readonly List<WeeklyReview> items;

    public WeeklyReviewStore() : this(Path.Combine(Paths.DataDir, "assistant", "weekly.json")) { }

    public WeeklyReviewStore(string file)
    {
        this.file = file;
        items = Json.Load<List<WeeklyReview>>(file) ?? new();
    }

    public IReadOnlyList<WeeklyReview> All => items;
    public WeeklyReview? Latest => items.OrderByDescending(r => r.CreatedAt).FirstOrDefault();

    public static DateTime MondayOf(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>О какой неделе пора подвести итоги (понедельник), или null — не пора / уже есть.</summary>
    public DateTime? DueWeek(DateTime now)
    {
        DateTime week;
        if (now.DayOfWeek == DayOfWeek.Sunday)
        {
            if (now.TimeOfDay < SundayFrom) return null;
            week = MondayOf(now);
        }
        else if (now.DayOfWeek is DayOfWeek.Monday or DayOfWeek.Tuesday)
            week = MondayOf(now).AddDays(-7);   // пропустил воскресенье — догоняем
        else return null;
        return items.Any(r => r.WeekStart == week) ? null : week;
    }

    public void Add(WeeklyReview r)
    {
        items.RemoveAll(x => x.WeekStart == r.WeekStart);
        items.Add(r);
        Json.Save(file, items.OrderBy(x => x.WeekStart).ToList());
    }

    /// <summary>Запрос «подведи итоги недели»: заметки, профиль, цели, статистика.</summary>
    public static (string System, ChatMessage Request) BuildRequest(TutorMemory memory, LearningStats stats, GoalStore goals, DateTime weekStart, DateTime now)
    {
        string system =
            "Ты — личный помощник по учёбе и хобби. Подведи итоги недели для человека: тепло, честно и коротко (до 15 строк), по-русски.\n" +
            "Что написать: что получилось и сколько занимался; где ошибается и что повторить; как идут цели (успевает ли к сроку — " +
            "посчитай по оставшимся дням); одна конкретная похвала и один совет.\n" +
            "В конце добавь блок <план> … </план> — план на следующую неделю: 3–5 коротких пунктов с днями или частотой " +
            "(например «Пн, Ср, Пт — по 15 минут Unité 6»). Без markdown-разметки.";
        var sb = new StringBuilder();
        sb.AppendLine($"Неделя: {weekStart:dd.MM} — {weekStart.AddDays(6):dd.MM.yyyy}. Сегодня {now.ToString("dddd, dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"))}.");
        sb.AppendLine().AppendLine("<статистика>").AppendLine(stats.Summary()).AppendLine("</статистика>");
        string g = goals.PromptLines(now);
        sb.AppendLine().AppendLine("<цели>").AppendLine(g.Length > 0 ? g : "(целей нет — предложи одну реалистичную на ближайший месяц)").AppendLine("</цели>");
        if (memory.Profile.Length > 0) sb.AppendLine().AppendLine("<профиль>").AppendLine(memory.Profile).AppendLine("</профиль>");
        if (memory.Notes.Length > 0) sb.AppendLine().AppendLine("<заметки помощника>").AppendLine(memory.Notes).AppendLine("</заметки помощника>");
        return (system, new ChatMessage(true, sb.ToString(), now));
    }

    static readonly Regex PlanBlock = new(@"<план>(.*?)(</план>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>Разобрать ответ: разбор и план отдельно.</summary>
    public static WeeklyReview Parse(string reply, DateTime weekStart, DateTime now)
    {
        var m = PlanBlock.Match(reply);
        string plan = m.Success ? m.Groups[1].Value.Trim() : "";
        string text = m.Success ? (reply[..m.Index] + reply[(m.Index + m.Length)..]).Trim() : reply.Trim();
        return new WeeklyReview { WeekStart = weekStart, CreatedAt = now, Text = Regex.Replace(text, @"\n{3,}", "\n\n"), Plan = plan };
    }
}

/// <summary>Небольшие JSON-файлы рядом с настройками: читаемые (кириллица как есть), ошибки — молча.</summary>
static class Json
{
    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
    };

    public static T? Load<T>(string file) where T : class
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<T>(File.ReadAllText(file)) : null; }
        catch { return null; }
    }

    public static void Save<T>(string file, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(value, Options));
        }
        catch { }
    }
}
