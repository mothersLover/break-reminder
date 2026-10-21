using System.Text;
using System.Text.Json;

namespace BreakReminder.Core;

/// <summary>
/// Память ИИ-помощника (папка assistant/ рядом с настройками):
/// profile.txt — что человек написал о себе сам; notes.txt — заметки, которые помощник ведёт после разговоров;
/// chat.json — текущий разговор. Модель сама ничего не помнит: всё это отправляется ей в начале каждого запроса.
/// </summary>
public sealed class TutorMemory
{
    public const int MaxNotesChars = 6000;

    readonly string dir;
    string profile, notes;
    readonly List<ChatMessage> chat;
    int summarized;   // сколько первых сообщений разговора уже учтено в заметках

    public TutorMemory() : this(Path.Combine(Paths.DataDir, "assistant")) { }

    public TutorMemory(string dir)
    {
        this.dir = dir;
        profile = Read("profile.txt");
        notes = Read("notes.txt");
        (chat, summarized) = LoadChat();
    }

    string FilePath(string name) => Path.Combine(dir, name);

    public string Profile
    {
        get => profile;
        set { profile = (value ?? "").Trim(); Write("profile.txt", profile); }
    }

    public string Notes
    {
        get => notes;
        set { notes = Clip((value ?? "").Trim()); Write("notes.txt", notes); }
    }

    public IReadOnlyList<ChatMessage> Chat => chat;

    /// <summary>Есть сообщения, ещё не учтённые в заметках (и хотя бы один ответ помощника).</summary>
    public bool NeedsSummary => chat.Skip(summarized).Any(m => !m.Local && !m.IsUser);

    public void Add(ChatMessage m)
    {
        chat.Add(m);
        SaveChat();
    }

    /// <summary>Заметки обновлены по разговору до count-го сообщения.</summary>
    public void MarkSummarized(int count)
    {
        summarized = Math.Clamp(count, 0, chat.Count);
        SaveChat();
    }

    /// <summary>Начать разговор заново (заметки остаются).</summary>
    public void ClearChat()
    {
        chat.Clear();
        summarized = 0;
        SaveChat();
    }

    /// <summary>«Забыть всё»: профиль, заметки, разговор.</summary>
    public void ForgetAll()
    {
        Profile = "";
        Notes = "";
        ClearChat();
    }

    // ---------- запросы к модели ----------

    /// <summary>Инструкции для помощника: роль, стиль, профиль и заметки.</summary>
    public string SystemPrompt(AppSettings s, DateTime now)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Ты — личный помощник в программе «Отдыхалка». Помогаешь человеку с хобби, привычками и учёбой, " +
                      "особенно с иностранными языками: объясняешь, проверяешь задания, тренируешь, напоминаешь пройденное, " +
                      "помогаешь не бросать.");
        sb.AppendLine();
        sb.AppendLine("Как отвечать:");
        sb.AppendLine("- По-русски, если человек не просит иначе; примеры, упражнения и разборы — на изучаемом языке с переводом, где нужно.");
        sb.AppendLine("- Коротко и по делу: окно чата небольшое. Без таблиц и заголовков; простые списки можно. Без markdown-разметки вроде **жирного**.");
        sb.AppendLine(s.AiHintsFirst
            ? "- Когда человек делает упражнение или домашнее задание: не давай готовый ответ сразу. Укажи, где ошибка, дай подсказку " +
              "и попроси попробовать ещё раз. Правильный вариант — после второй попытки или если человек попросит."
            : "- Когда человек делает упражнение или домашнее задание: проверяй и сразу показывай правильный вариант с коротким объяснением.");
        sb.AppendLine("- Иногда задавай встречный вопрос, чтобы проверить понимание («почему здесь passé composé?»).");
        sb.AppendLine("- Опирайся на профиль и заметки ниже. Не выдумывай содержание конкретного учебника или юнита: если не знаешь, что там, — спроси.");
        sb.AppendLine();
        sb.Append("Сегодня ").Append(now.ToString("dddd, d MMMM yyyy, HH:mm", Ru)).AppendLine(".");
        if (s.UserName.Length > 0) sb.Append("Человека зовут ").Append(s.UserName).AppendLine(".");

        if (profile.Length == 0 && notes.Length == 0)
        {
            sb.AppendLine();
            sb.AppendLine("Ты пока ничего не знаешь о человеке. В начале разговора коротко спроси, чем он занимается " +
                          "(хобби, язык, учебник, где остановился) и чего хочет добиться.");
        }
        if (profile.Length > 0)
            sb.AppendLine().AppendLine("<профиль — человек написал сам>").AppendLine(profile).AppendLine("</профиль>");
        if (notes.Length > 0)
            sb.AppendLine().AppendLine("<заметки о прошлых занятиях — ты вёл их сам>").AppendLine(notes).AppendLine("</заметки>");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Запрос «обнови заметки по разговору». Возвращает инструкции, одно сообщение и число учтённых сообщений
    /// (передать в MarkSummarized после успеха). null — обновлять нечего.
    /// </summary>
    public (string System, ChatMessage Request, int Count)? SummaryRequest(DateTime now)
    {
        if (!NeedsSummary) return null;
        int count = chat.Count;
        var fresh = chat.Skip(summarized).Where(m => !m.Local).ToList();

        string system =
            "Ты ведёшь заметки для ИИ-помощника, который помогает человеку с хобби, привычками и учёбой. " +
            "Помощник видит только эти заметки — по ним он должен вспомнить человека в следующий раз.\n" +
            "Обнови заметки по новому разговору. Сохрани важное из старых, убери устаревшее.\n" +
            "Что записывать: чем человек занимается (хобби, язык, учебник, текущий юнит/уровень), цели, договорённости " +
            "(«каждый день по 15 минут»), что уже разобрано, типичные ошибки и что повторить, что получается хорошо.\n" +
            "Формат: короткие пункты обычным текстом, группами «О человеке», «Пройдено», «Ошибки и что повторить», «Договорённости». " +
            $"Не больше 40 строк. Даты пиши числами (сегодня {now:dd.MM.yyyy}). Верни только заметки, без вступлений.";

        var sb = new StringBuilder();
        sb.AppendLine("<текущие заметки>").AppendLine(notes.Length > 0 ? notes : "(пока пусто)").AppendLine("</текущие заметки>").AppendLine();
        if (profile.Length > 0) sb.AppendLine("<профиль — человек написал сам>").AppendLine(profile).AppendLine("</профиль>").AppendLine();
        sb.AppendLine("<новый разговор>");
        foreach (var m in fresh) sb.Append(m.IsUser ? "Человек: " : "Помощник: ").AppendLine(m.Text.Trim()).AppendLine();
        sb.AppendLine("</новый разговор>");

        return (system, new ChatMessage(true, sb.ToString(), now), count);
    }

    // ---------- файлы ----------

    static readonly System.Globalization.CultureInfo Ru = new("ru-RU");

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
    };

    sealed class ChatFile
    {
        public int Summarized { get; set; }
        public List<ChatMessage> Messages { get; set; } = new();
    }

    static string Clip(string s) => s.Length > MaxNotesChars ? s[..MaxNotesChars] : s;

    string Read(string name)
    {
        try { return File.Exists(FilePath(name)) ? File.ReadAllText(FilePath(name), Encoding.UTF8).Trim() : ""; }
        catch { return ""; }
    }

    void Write(string name, string text)
    {
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath(name), text, Encoding.UTF8);
        }
        catch { }
    }

    (List<ChatMessage>, int) LoadChat()
    {
        try
        {
            if (!File.Exists(FilePath("chat.json"))) return (new(), 0);
            var f = JsonSerializer.Deserialize<ChatFile>(File.ReadAllText(FilePath("chat.json"))) ?? new ChatFile();
            var list = f.Messages.Where(m => m is { Text: not null }).ToList();
            return (list, Math.Clamp(f.Summarized, 0, list.Count));
        }
        catch { return (new(), 0); }
    }

    void SaveChat()
    {
        // служебные сообщения (ошибки) не храним; держим последние 200
        var keep = chat.Where(m => !m.Local).ToList();
        int drop = Math.Max(0, keep.Count - 200);
        var file = new ChatFile { Summarized = Math.Max(0, chat.Take(summarized).Count(m => !m.Local) - drop), Messages = keep.Skip(drop).ToList() };
        Write("chat.json", JsonSerializer.Serialize(file, JsonOptions));
    }
}
