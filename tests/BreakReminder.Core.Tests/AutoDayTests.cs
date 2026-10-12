using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class AutoDayTests
{
    static readonly DateTime Day = new(2026, 10, 12);

    static Rig AutoRig(DateTime start, int startMin = 600, int endMin = 1320) =>
        new(start, s => { s.AutoDay = true; s.AutoStartMin = startMin; s.AutoEndMin = endMin; });

    [Theory]
    [InlineData(9.99, false)]
    [InlineData(10.0, true)]
    [InlineData(21.98, true)]
    [InlineData(22.0, false)]
    public void Window_10_to_22(double hour, bool inside)
    {
        var r = AutoRig(Day);
        Assert.Equal(inside, r.Engine.CurrentAutoWindowStart(Day.AddHours(hour)) != null);
    }

    [Fact]
    public void Window_across_midnight()
    {
        var r = AutoRig(Day, 22 * 60, 6 * 60);
        Assert.Equal(Day.AddHours(22), r.Engine.CurrentAutoWindowStart(Day.AddHours(23)));
        Assert.Equal(Day.AddDays(-1).AddHours(22), r.Engine.CurrentAutoWindowStart(Day.AddHours(3)));
        Assert.Null(r.Engine.CurrentAutoWindowStart(Day.AddHours(12)));
        Assert.Equal(Day.AddDays(1).AddHours(6), r.Engine.AutoEndFor(Day.AddHours(22.5)));
    }

    [Fact]
    public void End_time_follows_start()
    {
        var r = AutoRig(Day);
        Assert.Equal(Day.AddHours(22), r.Engine.AutoEndFor(Day.AddHours(9)));
        Assert.Equal(Day.AddDays(1).AddHours(22), r.Engine.AutoEndFor(Day.AddHours(23)));
        Assert.Equal(Day.AddHours(10), r.Engine.NextAutoStart(Day.AddHours(8)));
        Assert.Equal(Day.AddDays(1).AddHours(10), r.Engine.NextAutoStart(Day.AddHours(23)));
    }

    [Fact]
    public void Starts_at_ten_when_unlocked()
    {
        var r = AutoRig(Day.AddHours(9.9));
        r.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(WorkState.Idle, r.Engine.State);
        r.Advance(TimeSpan.FromMinutes(2)); // 10:01
        Assert.Equal(WorkState.Running, r.Engine.State);
        Assert.Single(r.Notifications);
    }

    [Fact]
    public void Waits_for_unlock()
    {
        var r = AutoRig(Day.AddHours(9.9));
        r.Probe.Locked = true;
        r.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(WorkState.Idle, r.Engine.State);
        Assert.Equal("Начну после разблокировки экрана", r.Engine.AutoStartHint(r.Now));
        r.Probe.Locked = false;
        r.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(WorkState.Running, r.Engine.State);
    }

    [Fact]
    public void Manual_start_before_ten_is_kept()
    {
        var r = AutoRig(Day.AddHours(9.5));
        r.Engine.StartDay(r.Now);
        r.Advance(TimeSpan.FromMinutes(40));
        Assert.Equal(Day.AddHours(9.5), r.Engine.DayStart);
        Assert.Empty(r.Notifications);
    }

    [Fact]
    public void Only_once_per_day_after_manual_end()
    {
        var r = AutoRig(Day.AddHours(10).AddSeconds(-1));
        r.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(WorkState.Running, r.Engine.State);
        r.Engine.EndDay(r.Now);
        r.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(WorkState.Idle, r.Engine.State);
        Assert.StartsWith("Автостарт завтра в", r.Engine.AutoStartHint(r.Now));
    }

    [Fact]
    public void History_from_earlier_today_blocks_autostart_after_restart()
    {
        var r = new Rig(Day.AddHours(15), s => s.AutoDay = true);
        // движок создан до того, как мы подложили историю — пересоздаём с ней
        r.History.Last = Day.AddHours(10).AddMinutes(2);
        var engine = new WorkDayEngine(r.Settings, r.Probe, r.History, r.Now);
        engine.Tick(r.Now.AddSeconds(1));
        Assert.Equal(WorkState.Idle, engine.State);
    }

    [Fact]
    public void Auto_end_at_22_and_sleep_through_end_counts_only_until_22()
    {
        var r = AutoRig(Day.AddHours(10).AddSeconds(-1));
        r.Advance(TimeSpan.FromSeconds(2));
        r.Advance(TimeSpan.FromHours(11));        // 21:00
        r.Jump(TimeSpan.FromHours(12));           // компьютер проспал до 09:00 следующего дня
        Assert.Equal(WorkState.Idle, r.Engine.State);
        var row = Assert.Single(r.History.Rows);
        Assert.Equal(Day.AddHours(22), row.End);
        Assert.InRange(row.Day.Minutes, 719, 720); // 10:00 → 22:00
        Assert.Equal(2, r.Notifications.Count);
    }

    [Fact]
    public void Disabled_does_nothing()
    {
        var r = new Rig(Day.AddHours(9.99));
        r.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(WorkState.Idle, r.Engine.State);
        Assert.Equal("", r.Engine.AutoStartHint(r.Now));
    }
}
