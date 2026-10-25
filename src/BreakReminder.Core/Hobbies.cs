namespace BreakReminder.Core;

/// <summary>Хобби: «📘 Французский» (язык озвучки fr) или «🎸 Гитара» (без озвучки).</summary>
public sealed class Hobby
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    public string Emoji { get; set; } = "📘";
    /// <summary>Язык озвучки (код ISO), "" — без озвучки.</summary>
    public string Lang { get; set; } = "";

    public string Label => $"{Emoji} {Name}".Trim();
}

/// <summary>
/// Хобби и какое из них выбрано (assistant/hobbies.json). Первое хобби («main») живёт в прежней папке assistant/ —
/// данные, накопленные до появления хобби, никуда не переезжают; остальные — в assistant/hobbies/&lt;id&gt;/.
/// </summary>
public sealed class HobbyStore
{
    public const string MainId = "main";

    sealed class FileModel
    {
        public List<Hobby> Hobbies { get; set; } = new();
        public string Active { get; set; } = MainId;
    }

    readonly string baseDir;
    readonly FileModel data;

    public HobbyStore(string defaultLang) : this(Path.Combine(Paths.DataDir, "assistant"), defaultLang) { }

    /// <param name="baseDir">Папка помощника (assistant/).</param>
    /// <param name="defaultLang">Язык озвучки для первого хобби, если файла ещё нет.</param>
    public HobbyStore(string baseDir, string defaultLang)
    {
        this.baseDir = baseDir;
        data = Json.Load<FileModel>(FilePath) ?? new FileModel();
        if (data.Hobbies.Count == 0)
        {
            // первое хобби — то, чем человек уже занимается; название по языку озвучки
            string name = string.IsNullOrEmpty(defaultLang) ? "Учёба" : SpeechText.Name(defaultLang);
            data.Hobbies.Add(new Hobby { Id = MainId, Name = name, Emoji = "📘", Lang = defaultLang ?? "" });
            data.Active = MainId;
        }
        if (data.Hobbies.All(h => h.Id != data.Active)) data.Active = data.Hobbies[0].Id;
    }

    string FilePath => Path.Combine(baseDir, "hobbies.json");

    public IReadOnlyList<Hobby> All => data.Hobbies;
    public Hobby Active => data.Hobbies.First(h => h.Id == data.Active);

    /// <summary>Папка с памятью, карточками, целями и итогами этого хобби.</summary>
    public string DirFor(Hobby h) => h.Id == MainId ? baseDir : Path.Combine(baseDir, "hobbies", h.Id);

    public Hobby Add(string name, string emoji, string lang)
    {
        var h = new Hobby { Name = name.Trim(), Emoji = string.IsNullOrWhiteSpace(emoji) ? "📘" : emoji.Trim(), Lang = lang ?? "" };
        data.Hobbies.Add(h);
        Save();
        return h;
    }

    public void Update(Hobby h) => Save();

    public void SetActive(string id)
    {
        if (data.Hobbies.Any(h => h.Id == id) && data.Active != id)
        {
            data.Active = id;
            Save();
        }
    }

    /// <summary>Удалить хобби вместе с его папкой. Последнее и первое («main», старые данные) не удаляются.</summary>
    public bool Remove(string id)
    {
        if (id == MainId || data.Hobbies.Count <= 1) return false;
        var h = data.Hobbies.FirstOrDefault(x => x.Id == id);
        if (h == null) return false;
        data.Hobbies.Remove(h);
        if (data.Active == id) data.Active = data.Hobbies[0].Id;
        Save();
        try { Directory.Delete(DirFor(h), true); } catch { }
        return true;
    }

    void Save() => Json.Save(FilePath, data);
}

/// <summary>Всё, что относится к одному хобби: память помощника, карточки, цели, итоги недели.</summary>
public sealed class HobbySpace
{
    public Hobby Hobby { get; }
    public TutorMemory Memory { get; }
    public CardStore Cards { get; }
    public GoalStore Goals { get; }
    public WeeklyReviewStore Reviews { get; }

    public HobbySpace(Hobby hobby, string dir)
    {
        Hobby = hobby;
        Memory = new TutorMemory(dir);
        Cards = new CardStore(Path.Combine(dir, "cards.json"));
        Goals = new GoalStore(Path.Combine(dir, "goals.json"));
        Reviews = new WeeklyReviewStore(Path.Combine(dir, "weekly.json"));
        Memory.Goals = Goals;           // помощник видит цели этого хобби
        Memory.Hobby = hobby.Name;
    }

    public static HobbySpace Open(HobbyStore store, Hobby h) => new(h, store.DirFor(h));
}
