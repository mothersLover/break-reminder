using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class FlashcardTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), "br-cards-" + Guid.NewGuid().ToString("N") + ".json");
    static readonly DateTime Now = new(2026, 10, 21, 12, 0, 0);

    public void Dispose()
    {
        try { File.Delete(file); } catch { }
    }

    static Flashcard New() => new() { Front = "я пошёл", Back = "je suis allé", Created = Now, Due = Now };

    [Fact]
    public void Good_answers_grow_the_interval()
    {
        var c = New();
        var t = Now;
        var days = new List<double>();
        for (int i = 0; i < 5; i++)
        {
            Scheduler.Apply(c, Grade.Good, t);
            days.Add(c.IntervalDays);
            t = c.Due.AddHours(9);
        }
        Assert.Equal(new double[] { 1, 3, 8, 20, 50 }, days);
        Assert.Equal(Now.Date.AddDays(1), new Flashcard { Created = Now }.Let(x => Scheduler.Apply(x, Grade.Good, Now)).Due);
    }

    [Fact]
    public void Again_brings_card_back_in_ten_minutes_and_lowers_ease()
    {
        var c = New();
        Scheduler.Apply(c, Grade.Good, Now);
        Scheduler.Apply(c, Grade.Good, Now.AddDays(1));
        Scheduler.Apply(c, Grade.Again, Now.AddDays(4));
        Assert.Equal(Now.AddDays(4) + Scheduler.AgainDelay, c.Due);
        Assert.Equal(0, c.Reps);
        Assert.Equal(1, c.Lapses);
        Assert.Equal(2.3, c.Ease, 3);
        Scheduler.Apply(c, Grade.Good, Now.AddDays(4).AddMinutes(10));
        Assert.Equal(1, c.IntervalDays);                          // после забывания — заново с 1 дня
    }

    [Fact]
    public void Buttons_are_ordered_again_hard_good_easy()
    {
        foreach (var reps in new[] { 0, 1, 2, 5 })
        {
            var c = New();
            for (int i = 0; i < reps; i++) Scheduler.Apply(c, Grade.Good, Now.AddDays(i * 10));
            double again = Scheduler.NextInterval(c, Grade.Again), hard = Scheduler.NextInterval(c, Grade.Hard),
                good = Scheduler.NextInterval(c, Grade.Good), easy = Scheduler.NextInterval(c, Grade.Easy);
            Assert.True(again < hard && hard <= good && good < easy, $"reps={reps}: {again} {hard} {good} {easy}");
        }
        Assert.Equal("10 мин", Scheduler.Label(0));
        Assert.Equal("3 дн", Scheduler.Label(3));
        Assert.Equal("3 нед", Scheduler.Label(20));
        Assert.Equal("7 нед", Scheduler.Label(50));
        Assert.Equal("2 мес", Scheduler.Label(70));
    }

    [Fact]
    public void Store_dedupes_persists_and_queues()
    {
        var s = new CardStore(file);
        int changes = 0;
        s.Changed += () => changes++;
        Assert.Equal(2, s.Add(new[] { ("Я пошёл в кино", "Je suis allé au cinéma", ""), ("она пришла", "elle est venue", "être + согласование") }, Now));
        Assert.Equal(0, s.Add(new[] { ("я пошёл в кино!", "другое", "") }, Now));   // тот же вопрос
        Assert.Equal(1, changes);

        var again = new CardStore(file);
        Assert.Equal(2, again.All.Count);
        Assert.Equal("être + согласование", again.All[1].Note);
        Assert.Equal(2, again.DueCount(Now));
        again.Review(again.Queue(Now)[0], Grade.Good, Now);
        Assert.Equal(1, new CardStore(file).DueCount(Now));
        Assert.Equal(2, new CardStore(file).DueCount(Now.AddDays(1)));
    }

    [Fact]
    public void Queue_puts_reviews_first_and_limits_new_cards_per_day()
    {
        var s = new CardStore(file);
        s.Add(Enumerable.Range(1, 15).Select(i => ($"вопрос {i}", $"ответ {i}", "")), Now.AddDays(-2));
        var old = s.All[0];
        s.Review(old, Grade.Good, Now.AddDays(-2));                 // повторение — на вчера
        var q = s.Queue(Now);
        Assert.Same(old, q[0]);
        Assert.Equal(1 + CardStore.NewPerDay, q.Count);

        foreach (var c in q.Skip(1).Take(4)) s.Review(c, Grade.Good, Now);   // 4 новых сегодня уже показаны
        Assert.Equal(1 + CardStore.NewPerDay - 4, s.Queue(Now).Count);
    }

    [Fact]
    public void Extract_cards_from_assistant_reply()
    {
        string reply = "Вот карточки по Unité 5.\n<карточки>\n- я пошёл в кино | je suis allé au cinéma | aller → être\n2) она пришла | elle est venue\nмусор без разделителя\n | пустой вопрос\n</карточки>\nУдачи!";
        var (text, found) = CardStore.Extract(reply);
        Assert.Equal("Вот карточки по Unité 5.\n\nУдачи!", text);
        Assert.Equal(2, found.Count);
        Assert.Equal(("я пошёл в кино", "je suis allé au cinéma", "aller → être"), found[0]);
        Assert.Equal(("она пришла", "elle est venue", ""), found[1]);

        var (plain, none) = CardStore.Extract("Просто ответ");
        Assert.Equal("Просто ответ", plain);
        Assert.Empty(none);

        var (cut, one) = CardStore.Extract("Заметки\n<карточки>\nа | б");   // модель не закрыла блок
        Assert.Equal("Заметки", cut);
        Assert.Single(one);
    }

    [Fact]
    public void Compare_answers()
    {
        Assert.Equal(AnswerMatch.Exact, CardStore.Compare("  Je suis allé au cinéma! ", "je suis allé au cinéma"));
        Assert.Equal(AnswerMatch.Exact, CardStore.Compare("c’est", "c'est"));
        Assert.Equal(AnswerMatch.AlmostAccents, CardStore.Compare("je suis alle au cinema", "Je suis allé au cinéma"));
        Assert.Equal(AnswerMatch.Different, CardStore.Compare("j'ai allé au cinéma", "je suis allé au cinéma"));
        Assert.Equal(AnswerMatch.Different, CardStore.Compare("", "je"));
    }

    [Fact]
    public void Word_of_day_prefers_forgotten_cards()
    {
        var s = new CardStore(file);
        Assert.Null(s.WordOfDay(Now));
        s.Add(new[] { ("лёгкое", "facile", ""), ("трудное", "difficile", "") }, Now);
        Assert.NotNull(s.WordOfDay(Now));                           // пока только новые
        var hard = s.All[1];
        s.Review(hard, Grade.Again, Now);
        Assert.Same(hard, s.WordOfDay(Now.Date.AddDays(1)));
    }

    [Fact]
    public void Summary_request_asks_for_cards_and_lists_existing()
    {
        string dir = Path.Combine(Path.GetTempPath(), "br-mem-" + Guid.NewGuid().ToString("N"));
        try
        {
            var m = new TutorMemory(dir);
            m.Add(new ChatMessage(true, "j'ai allé", Now));
            m.Add(new ChatMessage(false, "je suis allé", Now));
            var req = m.SummaryRequest(Now, new[] { "я пошёл в кино" })!.Value;
            Assert.Contains("<карточки>", req.System);
            Assert.Contains("<уже есть карточки>", req.Request.Text);
            Assert.Contains("я пошёл в кино", req.Request.Text);
            Assert.Contains("<карточки>", m.SystemPrompt(new AppSettings(), Now));   // и в чате — по просьбе
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

static class LetExt
{
    public static T Let<T>(this T x, Action<T> a) { a(x); return x; }
}
