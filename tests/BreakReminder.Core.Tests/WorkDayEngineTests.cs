using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class WorkDayEngineTests
{
    static readonly DateTime Monday9 = new(2026, 10, 12, 9, 0, 0);
    static TimeSpan Min(double m) => TimeSpan.FromMinutes(m);

    [Fact]
    public void Reminder_fires_after_interval_and_done_resets()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(49.9));
        Assert.Equal(0, r.RemindersShown);
        r.Advance(Min(0.2));
        Assert.Equal(1, r.RemindersShown);
        Assert.True(r.Engine.ReminderOpen);

        // пока окно открыто, отсчёт стоит, а время работы идёт
        r.Advance(Min(3));
        Assert.Equal(1, r.RemindersShown);
        Assert.True(r.Engine.Worked >= Min(53));

        r.Engine.Answer(ReminderResult.Done, r.Now);
        Assert.Equal(1, r.Engine.Done);
        Assert.Equal(Min(50), r.Engine.Remaining);
        Assert.Equal(TimeSpan.Zero, r.Engine.SinceLastBreak);
    }

    [Fact]
    public void Snooze_and_skip()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(50.1));
        r.Engine.Answer(ReminderResult.Snooze, r.Now);
        Assert.Equal(Min(5), r.Engine.Remaining);
        r.Advance(Min(5.1));
        Assert.Equal(2, r.RemindersShown);
        r.Engine.Answer(ReminderResult.Skip, r.Now);
        Assert.Equal((0, 1, 1), (r.Engine.Done, r.Engine.Skipped, r.Engine.Snoozed));
        Assert.Equal(Min(50), r.Engine.Remaining);
        // пропуск не сбрасывает «без перерыва»
        Assert.True(r.Engine.SinceLastBreak > Min(55));
    }

    [Fact]
    public void Every_fourth_done_break_is_long()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        for (int i = 1; i <= 4; i++)
        {
            r.Advance(Min(50.1));
            Assert.Equal(i == 4, r.LastWasLong);
            r.Engine.Answer(ReminderResult.Done, r.Now);
        }
        Assert.Equal(0, r.Engine.Cycle);
        Assert.False(r.Engine.NextIsLong);
    }

    [Fact]
    public void Skipped_long_break_stays_long()
    {
        var r = new Rig(Monday9, s => s.LongEvery = 2);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(50.1));
        r.Engine.Answer(ReminderResult.Done, r.Now);
        r.Advance(Min(50.1));
        Assert.True(r.LastWasLong);
        r.Engine.Answer(ReminderResult.Skip, r.Now);
        r.Advance(Min(50.1));
        Assert.True(r.LastWasLong);
    }

    [Fact]
    public void Pause_stops_time_and_long_pause_counts_as_rest()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(30));
        r.Engine.TogglePause(r.Now);
        r.Advance(Min(3));
        Assert.Equal(Min(30), r.Engine.Worked);
        r.Engine.TogglePause(r.Now);        // короткая пауза — отсчёт продолжается
        Assert.Equal(Min(20), r.Engine.Remaining);

        r.Engine.TogglePause(r.Now);
        r.Advance(Min(6));                  // длиннее перерыва (5 мин)
        r.Engine.TogglePause(r.Now);
        Assert.Equal(Min(50), r.Engine.Remaining);
    }

    [Fact]
    public void Pause_closes_open_reminder_and_it_returns_after()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(50.1));
        r.Engine.TogglePause(r.Now);
        Assert.Equal(1, r.RemindersCancelled);
        r.Advance(Min(1));
        r.Engine.TogglePause(r.Now);
        r.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(2, r.RemindersShown);
    }

    [Fact]
    public void Sleep_longer_than_break_resets_countdown()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(40));
        r.Jump(Min(20));                    // компьютер спал 20 минут
        Assert.Equal(Min(50), r.Engine.Remaining);
        Assert.Equal(0, r.Engine.Cycle);    // 20 мин >= большого перерыва
        Assert.Equal(0, r.RemindersShown);
    }

    [Fact]
    public void Idle_counts_as_rest_but_not_during_a_call()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(40));

        r.Probe.Idle = Min(6);
        r.Probe.Call = true;                // сидим на созвоне без мыши — не отдых
        r.Advance(Min(1));
        Assert.False(r.Engine.Away);
        Assert.True(r.Engine.Remaining < Min(10));

        r.Probe.Call = false;
        r.Advance(Min(0.1));
        Assert.True(r.Engine.Away);
        Assert.Equal(Min(50), r.Engine.Remaining);

        r.Probe.Idle = TimeSpan.Zero;
        r.Advance(Min(0.1));
        Assert.False(r.Engine.Away);
    }

    [Fact]
    public void Do_not_disturb_postpones_until_call_ends()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Probe.Call = true;
        r.Advance(Min(55));
        Assert.Equal(0, r.RemindersShown);
        Assert.Equal("идёт созвон", r.Engine.DndReason);

        r.Probe.Call = false;
        r.Advance(Min(0.1));
        Assert.Equal(1, r.RemindersShown);
        Assert.Null(r.Engine.DndReason);
    }

    [Fact]
    public void Do_not_disturb_postpones_during_fullscreen_app()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Probe.Fullscreen = true;
        r.Advance(Min(52));
        Assert.Equal(0, r.RemindersShown);
        Assert.Equal("открыта полноэкранная программа", r.Engine.DndReason);
        r.Probe.Fullscreen = false;
        r.Advance(Min(0.1));
        Assert.Equal(1, r.RemindersShown);
    }

    [Fact]
    public void Dnd_off_shows_reminder_during_call()
    {
        var r = new Rig(Monday9, s => s.DndCalls = false);
        r.Engine.StartDay(r.Now);
        r.Probe.Call = true;
        r.Advance(Min(50.1));
        Assert.Equal(1, r.RemindersShown);
    }

    [Fact]
    public void EndDay_writes_history()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(50.1));
        r.Engine.Answer(ReminderResult.Done, r.Now);
        r.Advance(Min(10));
        r.Engine.EndDay(r.Now);
        var row = Assert.Single(r.History.Rows);
        Assert.Equal(Monday9, row.Start);
        Assert.Equal(60, row.Day.Minutes);
        Assert.Equal(1, row.Day.Done);
        Assert.Equal(WorkState.Idle, r.Engine.State);
    }

    [Fact]
    public void Settings_change_clamps_remaining()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        var s = r.Settings.Clone();
        s.IntervalMin = 20;
        r.Engine.Settings = s;
        Assert.Equal(Min(20), r.Engine.Remaining);
    }
}
