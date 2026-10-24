using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class LearningTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "br-learn-" + Guid.NewGuid().ToString("N"));
    string F(string n) => Path.Combine(dir, n);
    static readonly DateTime Sunday = new(2026, 10, 25, 19, 0, 0);   // воскресенье

    public void Dispose()
    {
        try { Directory.Delete(dir, true); } catch { }
    }

    [Fact]
    public void Goals_persist_and_go_to_assistant_with_days_left()
    {
        var g = new GoalStore(F("goals.json"));
        int changes = 0;
        g.Changed += () => changes++;
        var unit6 = g.Add("Закончить Unité 6", new DateTime(2026, 11, 15), Sunday)!;
        g.Add("Пересказать выходные без ошибок", new DateTime(2026, 10, 25), Sunday);
        Assert.Null(g.Add("   ", Sunday, Sunday));
        Assert.Equal(2, changes);

        var again = new GoalStore(F("goals.json"));
        Assert.Equal(new[] { "Пересказать выходные без ошибок", "Закончить Unité 6" }, again.Active.Select(x => x.Title));   // по сроку
        string lines = again.PromptLines(Sunday);
        Assert.Contains("Закончить Unité 6 — до 15.11.2026 (осталось 21 дн.)", lines);
        Assert.Contains("(срок сегодня)", lines);

        again.SetDone(unit6.Id, true);
        Assert.Single(new GoalStore(F("goals.json")).Active);

        var memory = new TutorMemory(F("mem")) { Goals = again };
        Assert.Contains("<цели человека", memory.SystemPrompt(new AppSettings(), Sunday));
        Assert.Contains("Пересказать выходные", memory.SystemPrompt(new AppSettings(), Sunday));
    }

    [Fact]
    public void Stats_cover_minutes_streaks_cards_and_mistakes()
    {
        var log = new PracticeLog(F("p.csv"));
        var d = Sunday.Date;
        foreach (int back in new[] { 20, 19, 18, 17 }) log.Done(d.AddDays(-back).AddHours(20), PracticeKind.Evening, 10);   // серия из 4 дней
        log.Done(d.AddDays(-9).AddHours(20), PracticeKind.Evening, 15);                                                     // прошлая неделя
        log.Done(d.AddDays(-1).AddHours(11), PracticeKind.Morning, 5);
        log.Done(d.AddHours(20), PracticeKind.Homework, 30);
        log.Skip(d.AddHours(12), PracticeKind.Day);

        var cards = new CardStore(F("c.json"));
        cards.Add(new[] { ("забытое", "oublié", ""), ("выученное", "appris", ""), ("новое", "nouveau", "") }, d.AddDays(-60));
        var forgotten = cards.All[0];
        cards.Review(forgotten, Grade.Again, d.AddDays(-2));
        cards.Review(forgotten, Grade.Again, d.AddDays(-2));
        cards.All[1].IntervalDays = 30;
        cards.All[1].LastReview = d.AddDays(-1);
        cards.All[1].Due = d.AddDays(29);

        var s = LearningStats.Build(log, cards, Sunday);
        Assert.Equal(LearningStats.Days, s.MinutesByDay.Count);
        Assert.Equal(30, s.MinutesByDay[d]);
        Assert.Equal(35, s.WeekMinutes);
        Assert.Equal(15, s.PrevWeekMinutes);
        Assert.Equal(2, s.Streak);
        Assert.Equal(4, s.BestStreak);
        Assert.Equal(3, s.CardsTotal);
        Assert.Equal(1, s.CardsLearned);
        Assert.Equal(1, s.CardsNew);
        Assert.Same(forgotten, s.TopMistakes.Single());
        Assert.Equal(1, s.WeekSessions[PracticeKind.Homework]);
        Assert.False(s.WeekSessions.ContainsKey(PracticeKind.Day));                // пропуск — не занятие
        Assert.Contains("забытое → oublié (2 раз)", s.Summary());
    }

    [Fact]
    public void Weekly_review_is_due_sunday_evening_or_catch_up_until_tuesday()
    {
        var store = new WeeklyReviewStore(F("weekly.json"));
        var week = new DateTime(2026, 10, 19);                                     // понедельник этой недели
        Assert.Null(store.DueWeek(Sunday.Date.AddHours(17)));                      // рано
        Assert.Equal(week, store.DueWeek(Sunday));
        Assert.Equal(week, store.DueWeek(Sunday.AddDays(1)));                      // понедельник — догоняем прошлую неделю
        Assert.Equal(week, store.DueWeek(Sunday.AddDays(2)));                      // вторник
        Assert.Null(store.DueWeek(Sunday.AddDays(3)));                             // среда — уже нет
        Assert.Null(store.DueWeek(Sunday.AddDays(-2)));                            // пятница

        store.Add(WeeklyReviewStore.Parse("Отличная неделя!\n<план>\n- Пн, Ср — Unité 6\n</план>", week, Sunday));
        Assert.Null(store.DueWeek(Sunday));
        Assert.Null(new WeeklyReviewStore(F("weekly.json")).DueWeek(Sunday.AddDays(1)));
        Assert.Equal("- Пн, Ср — Unité 6", new WeeklyReviewStore(F("weekly.json")).Latest!.Plan);
    }

    [Fact]
    public void Weekly_request_and_parsing()
    {
        var memory = new TutorMemory(F("mem"));
        memory.Profile = "Édito A2, Unité 6";
        var goals = new GoalStore(F("goals.json"));
        goals.Add("Закончить Unité 6", new DateTime(2026, 11, 15), Sunday);
        var stats = LearningStats.Build(new PracticeLog(F("p.csv")), new CardStore(F("c.json")), Sunday);
        var (system, req) = WeeklyReviewStore.BuildRequest(memory, stats, goals, new DateTime(2026, 10, 19), Sunday);
        Assert.Contains("<план>", system);
        Assert.Contains("19.10 — 25.10.2026", req.Text);
        Assert.Contains("Закончить Unité 6 — до 15.11.2026", req.Text);
        Assert.Contains("Édito A2, Unité 6", req.Text);
        Assert.Contains("Занятия за 7 дней: 0 мин", req.Text);

        var r = WeeklyReviewStore.Parse("Разбор недели.\n\n\n\nСовет.\n<план>Пн — 15 мин", new DateTime(2026, 10, 19), Sunday);
        Assert.Equal("Разбор недели.\n\nСовет.", r.Text);
        Assert.Equal("Пн — 15 мин", r.Plan);                                     // модель не закрыла блок — всё равно план
        Assert.Equal("", WeeklyReviewStore.Parse("Без плана", Sunday, Sunday).Plan);
    }
}
