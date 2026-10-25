using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class HabitTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), "br-habits-" + Guid.NewGuid().ToString("N") + ".json");
    static readonly DateTime Day = new(2026, 10, 26, 10, 0, 0);

    public void Dispose()
    {
        try { File.Delete(file); } catch { }
    }

    [Fact]
    public void Marks_progress_skip_and_persist()
    {
        var s = new HabitStore(file);
        int changes = 0;
        s.Changed += () => changes++;
        var water = s.Add(new Habit { Emoji = "💧", Title = " Вода ", Target = 8 });
        var walk = s.Add(new Habit { Emoji = "🚶", Title = "Прогулка" });
        Assert.Equal("Вода", water.Title);

        for (int i = 0; i < 3; i++) s.Mark(water.Id, Day);
        Assert.Equal(2, s.Mark(water.Id, Day, -1));
        Assert.Equal(0, s.Mark(walk.Id, Day, -1));                  // ниже нуля — нет
        s.Skip(walk.Id, Day);
        Assert.True(s.IsSkipped(walk.Id, Day));
        s.Mark(walk.Id, Day);
        Assert.False(s.IsSkipped(walk.Id, Day));                    // сделал — уже не «не сегодня»
        Assert.Equal((1, 2), s.Progress(Day));

        var again = new HabitStore(file);
        Assert.Equal(2, again.Count(water.Id, Day));
        Assert.True(again.IsDone(again.All[1], Day));
        Assert.Contains("💧 Вода ×8", again.PlanLine());

        again.Remove(water.Id);
        Assert.Single(new HabitStore(file).All);
        Assert.True(changes >= 6);
    }

    [Fact]
    public void Streak_counts_days_target_met()
    {
        var s = new HabitStore(file);
        var water = s.Add(new Habit { Title = "Вода", Target = 2 });
        foreach (int back in new[] { 3, 2, 1 }) { s.Mark(water.Id, Day.AddDays(-back)); s.Mark(water.Id, Day.AddDays(-back)); }
        s.Mark(water.Id, Day.AddDays(-4));                           // 1 из 2 — не считается
        Assert.Equal(3, s.Streak(water, Day));                       // сегодня ещё не выполнено — серия по вчера
        s.Mark(water.Id, Day);
        s.Mark(water.Id, Day);
        Assert.Equal(4, s.Streak(water, Day));
    }

    sealed class HabitRig
    {
        public readonly Rig R;
        public readonly HabitStore Store;
        public readonly HabitCoach Coach;
        public readonly List<(string Title, DateTime At)> Due = new();

        public HabitRig(DateTime start, string file)
        {
            R = new Rig(start, s => s.IntervalMin = 240 * 2);   // перерывы не мешают (поверх напоминания о перерыве привычки молчат)
            Store = new HabitStore(file);
            Coach = new HabitCoach(Store, R.Engine, R.Probe);
            Coach.Due += h => Due.Add((h.Title, R.Now));
        }

        public void Minutes(int n)
        {
            for (int i = 0; i < n; i++)
            {
                R.Advance(TimeSpan.FromMinutes(1));
                Coach.Tick(R.Now);
            }
        }
    }

    [Fact]
    public void Every_n_minutes_only_during_work_day_from_last_mark()
    {
        var c = new HabitRig(Day, file);
        var water = c.Store.Add(new Habit { Title = "Вода", Target = 8, Remind = HabitRemind.Every, EveryMin = 90 });
        c.Minutes(120);
        Assert.Empty(c.Due);                                         // рабочий день не начат
        c.R.Engine.StartDay(c.R.Now);
        c.Minutes(89);
        Assert.Empty(c.Due);
        c.Minutes(1);
        Assert.Single(c.Due);
        c.Store.Mark(water.Id, c.R.Now);                             // «Сделал»
        c.Coach.Marked(water.Id, c.R.Now);
        c.Coach.Answered();
        c.Minutes(60);
        Assert.Single(c.Due);
        c.Store.Mark(water.Id, c.R.Now);                             // выпил сам, без напоминания
        c.Coach.Marked(water.Id, c.R.Now);
        c.Minutes(89);
        Assert.Single(c.Due);                                        // отсчёт от последней отметки
        c.Minutes(1);
        Assert.Equal(2, c.Due.Count);
    }

    [Fact]
    public void At_time_once_with_snooze_and_skip()
    {
        var c = new HabitRig(Day.Date.AddHours(12).AddMinutes(50), file);
        var walk = c.Store.Add(new Habit { Title = "Прогулка", Remind = HabitRemind.AtTime, RemindAt = 13 * 60 });
        c.Minutes(9);
        Assert.Empty(c.Due);
        c.Minutes(1);
        Assert.Single(c.Due);
        c.Coach.Snooze(walk.Id, c.R.Now);
        c.Minutes(29);
        Assert.Single(c.Due);
        c.Minutes(1);
        Assert.Equal(2, c.Due.Count);                                // «позже» — через 30 минут
        c.Coach.Answered();
        c.Minutes(60);
        Assert.Equal(2, c.Due.Count);                                // без «позже» — больше не напоминает
        c.Store.Skip(walk.Id, c.R.Now);
        c.Coach.Snooze(walk.Id, c.R.Now);
        c.Minutes(60);
        Assert.Equal(2, c.Due.Count);                                // «не сегодня»
    }

    [Fact]
    public void Not_during_call_or_when_away_but_fine_on_break()
    {
        var c = new HabitRig(Day.Date.AddHours(12).AddMinutes(59), file);
        c.Store.Add(new Habit { Title = "Прогулка", Remind = HabitRemind.AtTime, RemindAt = 13 * 60 });
        c.R.Probe.Call = true;
        c.Minutes(10);
        Assert.Empty(c.Due);
        c.R.Probe.Call = false;
        c.R.Probe.Locked = true;
        c.Minutes(5);
        Assert.Empty(c.Due);
        c.R.Probe.Locked = false;
        c.Minutes(1);
        Assert.Single(c.Due);
    }

    [Fact]
    public void Done_habit_is_not_reminded()
    {
        var c = new HabitRig(Day.Date.AddHours(12).AddMinutes(59), file);
        var walk = c.Store.Add(new Habit { Title = "Прогулка", Remind = HabitRemind.AtTime, RemindAt = 13 * 60 });
        c.Store.Mark(walk.Id, c.R.Now);
        c.Minutes(30);
        Assert.Empty(c.Due);
    }
}
