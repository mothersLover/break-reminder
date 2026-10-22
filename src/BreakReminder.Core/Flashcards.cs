using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BreakReminder.Core;

/// <summary>Карточка: вопрос → ответ. Интервал и «лёгкость» — для расписания повторений.</summary>
public sealed class Flashcard
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Front { get; set; } = "";       // что спросить (обычно по-русски)
    public string Back { get; set; } = "";        // правильный ответ (на изучаемом языке)
    public string Note { get; set; } = "";        // пояснение: правило, откуда ошибка
    public DateTime Created { get; set; }
    public DateTime Due { get; set; }             // когда показать
    public double IntervalDays { get; set; }      // текущий интервал
    public double Ease { get; set; } = 2.5;       // во сколько раз растёт интервал
    public int Reps { get; set; }                 // успешных повторений подряд
    public int Lapses { get; set; }               // сколько раз забывал
    public DateTime? LastReview { get; set; }
    public DateTime? FirstReview { get; set; }    // когда карточку впервые показали (для лимита новых в день)

    public bool IsNew => LastReview == null;
}

/// <summary>Оценка ответа: как в Anki.</summary>
public enum Grade { Again, Hard, Good, Easy }

/// <summary>
/// Расписание повторений (упрощённый SM-2). «Не помню» — снова через 10 минут, лёгкость падает;
/// «Помню» — 1 день, 3 дня, дальше интервал × лёгкость; «Легко» — длиннее; «Трудно» — чуть длиннее прежнего.
/// </summary>
public static class Scheduler
{
    public static readonly TimeSpan AgainDelay = TimeSpan.FromMinutes(10);
    public const double MinEase = 1.3, MaxIntervalDays = 365;

    /// <summary>Записать ответ: новые интервал, лёгкость и срок.</summary>
    public static void Apply(Flashcard c, Grade g, DateTime now)
    {
        double next = NextInterval(c, g);
        switch (g)
        {
            case Grade.Again:
                c.Lapses++;
                c.Reps = 0;
                c.Ease = Math.Max(MinEase, c.Ease - 0.2);
                c.IntervalDays = 0;
                c.Due = now + AgainDelay;
                break;
            case Grade.Hard:
                c.Ease = Math.Max(MinEase, c.Ease - 0.15);
                break;
            case Grade.Easy:
                c.Ease += 0.15;
                break;
        }
        if (g != Grade.Again)
        {
            c.Reps++;
            c.IntervalDays = next;
            c.Due = now.Date.AddDays(next);
        }
        c.FirstReview ??= now;
        c.LastReview = now;
    }

    /// <summary>Через сколько дней покажем, если ответить так (0 — сегодня же, через 10 минут).</summary>
    public static double NextInterval(Flashcard c, Grade g)
    {
        double i = c.IntervalDays;
        double days = g switch
        {
            Grade.Again => 0,
            Grade.Hard => c.Reps == 0 ? 1 : Math.Max(1, Math.Round(i * 1.2)),
            Grade.Good => c.Reps == 0 ? 1 : c.Reps == 1 ? 3 : Math.Round(i * c.Ease),
            _ => c.Reps == 0 ? 3 : Math.Round(Math.Max(i * c.Ease, 3) * 1.3)
        };
        // «Помню» не короче «Трудно», «Легко» не короче «Помню»
        if (g == Grade.Good) days = Math.Max(days, NextInterval(c, Grade.Hard) + (c.Reps >= 2 ? 1 : 0));
        if (g == Grade.Easy) days = Math.Max(days, NextInterval(c, Grade.Good) + 1);
        return Math.Min(days, MaxIntervalDays);
    }

    /// <summary>Подпись на кнопке: «10 мин», «1 дн», «2 нед», «3 мес».</summary>
    public static string Label(double days) =>
        days <= 0 ? $"{(int)AgainDelay.TotalMinutes} мин"
        : days < 14 ? $"{days:0} дн"
        : days < 60 ? $"{Math.Round(days / 7):0} нед"
        : $"{Math.Round(days / 30):0} мес";
}

/// <summary>Сравнение ответа с правильным: совпало, совпало без учёта диакритики (accents), не совпало.</summary>
public enum AnswerMatch { Exact, AlmostAccents, Different }

/// <summary>Карточки (assistant/cards.json): добавление без дублей, очередь на повторение, карточки из ответов помощника.</summary>
public sealed class CardStore
{
    public const int NewPerDay = 10;   // новых карточек в день — не больше

    readonly string file;
    readonly List<Flashcard> cards;

    public CardStore() : this(Path.Combine(Paths.DataDir, "assistant", "cards.json")) { }

    public CardStore(string file)
    {
        this.file = file;
        cards = Load(file);
    }

    public IReadOnlyList<Flashcard> All => cards;

    /// <summary>Карточки изменились — для обновления окон и счётчиков.</summary>
    public event Action? Changed;

    /// <summary>Добавить карточки; дубли (тот же вопрос) пропускаются. Возвращает, сколько добавлено.</summary>
    public int Add(IEnumerable<(string Front, string Back, string Note)> items, DateTime now)
    {
        var known = cards.Select(c => Key(c.Front)).ToHashSet();
        int added = 0;
        foreach (var (front, back, note) in items)
        {
            string f = front.Trim(), b = back.Trim();
            if (f.Length == 0 || b.Length == 0 || !known.Add(Key(f))) continue;
            cards.Add(new Flashcard { Front = f, Back = b, Note = (note ?? "").Trim(), Created = now, Due = now });
            added++;
        }
        if (added > 0) Save();
        return added;
    }

    public void Remove(string id)
    {
        if (cards.RemoveAll(c => c.Id == id) > 0) Save();
    }

    /// <summary>Ответ на карточку.</summary>
    public void Review(Flashcard c, Grade g, DateTime now)
    {
        Scheduler.Apply(c, g, now);
        Save();
    }

    /// <summary>Очередь на сейчас: сначала просроченные повторения, потом новые (не больше NewPerDay за день).</summary>
    public List<Flashcard> Queue(DateTime now)
    {
        int newToday = cards.Count(c => c.FirstReview?.Date == now.Date);
        var review = cards.Where(c => !c.IsNew && c.Due <= now).OrderBy(c => c.Due);
        var fresh = cards.Where(c => c.IsNew && c.Due <= now).OrderBy(c => c.Created).Take(Math.Max(0, NewPerDay - newToday));
        return review.Concat(fresh).ToList();
    }

    public int DueCount(DateTime now) => Queue(now).Count;

    /// <summary>Слово дня: недавно выученная или забываемая карточка — чтобы освежить.</summary>
    public Flashcard? WordOfDay(DateTime today)
    {
        if (cards.Count == 0) return null;
        var pool = cards.Where(c => !c.IsNew).OrderByDescending(c => c.Lapses).ThenByDescending(c => c.LastReview).Take(10).ToList();
        if (pool.Count == 0) pool = cards.OrderByDescending(c => c.Created).Take(10).ToList();
        return pool[(today.DayOfYear + today.Year) % pool.Count];
    }

    /// <summary>
    /// Что послушать: сначала то, что пора повторить, потом недавно выученное и забываемое, потом новое.
    /// Только карточки, где ответ — иностранная фраза (без кириллицы).
    /// </summary>
    public List<Flashcard> ForListening(DateTime now, int count)
    {
        var speakable = cards.Where(c => !SpeechText.HasCyrillic(c.Back)).ToList();
        var due = speakable.Where(c => !c.IsNew && c.Due <= now).OrderBy(c => c.Due);
        var recent = speakable.Where(c => !c.IsNew && c.Due > now).OrderByDescending(c => c.Lapses).ThenByDescending(c => c.LastReview);
        var fresh = speakable.Where(c => c.IsNew).OrderByDescending(c => c.Created);
        return due.Concat(recent).Concat(fresh).Distinct().Take(count).ToList();
    }

    /// <summary>Вопросы уже существующих карточек — чтобы помощник не повторялся.</summary>
    public IEnumerable<string> RecentFronts(int max) => cards.OrderByDescending(c => c.Created).Take(max).Select(c => c.Front);

    // ---------- карточки из текста помощника ----------

    static readonly Regex Block = new(@"<карточки>(.*?)(</карточки>|$)", RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Вынуть блок &lt;карточки&gt;…&lt;/карточки&gt; из ответа помощника. Строки «вопрос | ответ | пояснение»
    /// (пояснение необязательно). Возвращает текст без блока и найденные карточки.
    /// </summary>
    public static (string Text, List<(string Front, string Back, string Note)> Cards) Extract(string reply)
    {
        var list = new List<(string, string, string)>();
        var m = Block.Match(reply);
        if (!m.Success) return (reply.Trim(), list);
        foreach (var raw in m.Groups[1].Value.Split('\n'))
        {
            var line = Regex.Replace(raw.Trim(), @"^([-*•]|\d+[.)])\s*", "");
            var parts = line.Split('|').Select(p => p.Trim()).ToArray();
            if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0) continue;
            list.Add((parts[0], parts[1], parts.Length > 2 ? string.Join(" | ", parts.Skip(2)) : ""));
        }
        string text = (reply[..m.Index] + reply[(m.Index + m.Length)..]).Trim();
        return (Regex.Replace(text, @"\n{3,}", "\n\n"), list);
    }

    /// <summary>Сравнить ответ: без учёта регистра, пробелов и знаков препинания; отдельно — «верно, но без диакритики».</summary>
    public static AnswerMatch Compare(string answer, string correct)
    {
        string a = Norm(answer), c = Norm(correct);
        if (a.Length == 0) return AnswerMatch.Different;
        if (a == c) return AnswerMatch.Exact;
        return StripAccents(a) == StripAccents(c) ? AnswerMatch.AlmostAccents : AnswerMatch.Different;
    }

    static string Norm(string s)
    {
        s = s.ToLowerInvariant().Replace('’', '\'').Replace('`', '\'');
        s = Regex.Replace(s, @"[.,!?;:«»""()\-–—]", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    static string StripAccents(string s)
    {
        var sb = new StringBuilder();
        foreach (char ch in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    static string Key(string front) => Norm(front);

    // ---------- файл ----------

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
    };

    static List<Flashcard> Load(string file)
    {
        try
        {
            if (!File.Exists(file)) return new();
            return (JsonSerializer.Deserialize<List<Flashcard>>(File.ReadAllText(file)) ?? new())
                .Where(c => c.Front.Length > 0 && c.Back.Length > 0).ToList();
        }
        catch { return new(); }
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(cards, JsonOptions));
        }
        catch { }
        Changed?.Invoke();
    }
}
