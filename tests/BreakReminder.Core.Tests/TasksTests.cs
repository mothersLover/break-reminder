using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class TaskStoreTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), "br-tasks-" + Guid.NewGuid().ToString("N") + ".json");
    static readonly DateTime Today = DateTime.Today;

    public void Dispose()
    {
        try { File.Delete(file); } catch { }
    }

    [Fact]
    public void Add_done_remove_and_persist()
    {
        var s = new TaskStore(file);
        int changes = 0;
        s.Changed += () => changes++;
        var a = s.Add(Today, "  Позвонить в банк ", new TimeSpan(18, 30, 0), "по поводу карты");
        var b = s.Add(Today, "Написать отчёт", null, "");
        s.SetDone(b.Id, true);
        Assert.Equal(3, changes);

        var again = new TaskStore(file);
        var list = again.ForDay(Today);
        Assert.Equal(new[] { "Позвонить в банк", "Написать отчёт" }, list.Select(t => t.Title));  // невыполненные — сначала
        Assert.Equal(Today.AddHours(18.5), list[0].At);
        Assert.True(list[1].Done);

        again.Remove(a.Id);
        Assert.Single(new TaskStore(file).All);
    }

    [Fact]
    public void Undone_untimed_tasks_carry_over_to_today()
    {
        var s = new TaskStore(file);
        s.Add(Today.AddDays(-1), "Без времени со вчера", null, "");
        s.Add(Today.AddDays(-1), "Со временем вчера", new TimeSpan(10, 0, 0), "");
        var done = s.Add(Today.AddDays(-1), "Сделанное вчера", null, "");
        s.SetDone(done.Id, true);
        s.Add(Today.AddDays(-20), "Совсем старое", null, "");
        s.Add(Today.AddDays(1), "Завтра", null, "");

        Assert.Equal(new[] { "Без времени со вчера" }, s.ForDay(Today).Select(t => t.Title));
    }
}

public class TaskAlertTests
{
    static readonly DateTime Day = new(2026, 10, 15, 9, 0, 0);

    [Fact]
    public void Alert_n_minutes_before_once_even_outside_work_day()
    {
        var r = new Rig(Day);   // день не начат
        var fired = new List<string>();
        r.Engine.TaskDue += t => fired.Add(t.Title);
        r.Engine.Tasks = new[]
        {
            new LocalTask { Title = "Звонок", Date = Day.Date, Time = new TimeSpan(9, 30, 0) },
            new LocalTask { Title = "Готово уже", Date = Day.Date, Time = new TimeSpan(9, 30, 0), Done = true },
            new LocalTask { Title = "Без времени", Date = Day.Date }
        };
        r.Advance(TimeSpan.FromMinutes(24.9));
        Assert.Empty(fired);
        r.Advance(TimeSpan.FromMinutes(0.2));             // 09:25:06 — за 5 мин
        Assert.Equal(new[] { "Звонок" }, fired);
        r.Advance(TimeSpan.FromMinutes(10));
        Assert.Single(fired);
    }

    [Fact]
    public void Past_tasks_are_not_alerted_after_start_and_rescheduled_are()
    {
        var r = new Rig(Day.AddHours(1));                 // программа запущена в 10:00
        var fired = new List<string>();
        r.Engine.TaskDue += t => fired.Add(t.Title);
        var t = new LocalTask { Title = "Утренняя", Date = Day.Date, Time = new TimeSpan(9, 30, 0) };
        r.Engine.Tasks = new[] { t };
        r.Advance(TimeSpan.FromSeconds(5));
        Assert.Empty(fired);                              // уже прошла

        t.Time = new TimeSpan(10, 3, 0);                  // перенесли
        r.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "Утренняя" }, fired);
    }

    [Fact]
    public void Alert_exactly_on_time_when_lead_is_zero()
    {
        var r = new Rig(Day, s => s.TaskAlertMin = 0);
        var fired = 0;
        r.Engine.TaskDue += _ => fired++;
        r.Engine.Tasks = new[] { new LocalTask { Title = "x", Date = Day.Date, Time = new TimeSpan(9, 10, 0) } };
        r.Advance(TimeSpan.FromMinutes(9.9));
        Assert.Equal(0, fired);
        r.Advance(TimeSpan.FromMinutes(0.2));
        Assert.Equal(1, fired);
    }
}
