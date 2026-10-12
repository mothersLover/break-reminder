using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class IcsParserTests
{
    // Время в UTC (суффикс Z) — чтобы тест не зависел от часового пояса машины.
    const string Ics = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//test//EN
        BEGIN:VEVENT
        UID:standup
        DTSTART:20261012T070000Z
        DTEND:20261012T071500Z
        RRULE:FREQ=DAILY;COUNT=5
        SUMMARY:Стендап
        END:VEVENT
        BEGIN:VEVENT
        UID:cancelled
        DTSTART:20261012T090000Z
        DTEND:20261012T100000Z
        STATUS:CANCELLED
        SUMMARY:Отменили
        END:VEVENT
        BEGIN:VEVENT
        UID:free
        DTSTART:20261012T110000Z
        DTEND:20261012T120000Z
        TRANSP:TRANSPARENT
        SUMMARY:Фокус-время
        END:VEVENT
        BEGIN:VEVENT
        UID:allday
        DTSTART;VALUE=DATE:20261012
        DTEND;VALUE=DATE:20261013
        SUMMARY:День рождения
        END:VEVENT
        BEGIN:VEVENT
        UID:review
        DTSTART:20261012T130000Z
        DTEND:20261012T140000Z
        SUMMARY:Ревью
        END:VEVENT
        END:VCALENDAR
        """;

    static DateTime Utc(int d, int h, int m = 0) => new DateTime(2026, 10, d, h, m, 0, DateTimeKind.Utc).ToLocalTime();

    [Fact]
    public void Parses_busy_meetings_and_expands_recurrence()
    {
        // 12-е и 13-е: стендап каждый день + ревью 12-го; отменённая, «свободная» и на весь день — отброшены
        var list = IcsParser.Parse(Ics, Utc(12, 0), Utc(14, 0));
        Assert.Equal(new[] { "Стендап", "Ревью", "Стендап" }, list.Select(m => m.Title));
        var standup = list[0];
        Assert.Equal(Utc(12, 7), standup.Start);
        Assert.Equal(Utc(12, 7, 15), standup.End);
    }

    [Fact]
    public void Webcal_is_normalized()
    {
        Assert.Equal("https://calendar.example/x.ics", CalendarFeed.NormalizeUrl("  webcal://calendar.example/x.ics "));
    }
}

public class CalendarEngineTests
{
    static readonly DateTime Monday9 = new(2026, 10, 12, 9, 0, 0);
    static TimeSpan Min(double m) => TimeSpan.FromMinutes(m);

    static Rig CalRig(params Meeting[] meetings) => CalRig(null, meetings);

    static Rig CalRig(Action<AppSettings>? configure, params Meeting[] meetings)
    {
        var r = new Rig(Monday9, s => { s.CalendarOn = true; configure?.Invoke(s); });
        r.Engine.Meetings = meetings;
        return r;
    }

    static Meeting M(int startMin, int durMin, string title = "Планёрка") =>
        new(Monday9.AddMinutes(startMin), Monday9.AddMinutes(startMin + durMin), title);

    [Fact]
    public void Reminder_waits_for_meeting_end()
    {
        var r = CalRig(s => s.CalBreakBefore = false, M(40, 30));   // 09:40–10:10
        r.Engine.StartDay(r.Now);
        r.Advance(Min(55));                              // перерыв должен был быть в 09:50
        Assert.Equal(0, r.RemindersShown);
        Assert.Equal("встреча «Планёрка» до 10:10", r.Engine.DndReason);
        r.Advance(Min(16));                              // 10:11
        Assert.Equal(1, r.RemindersShown);
    }

    [Fact]
    public void Meeting_is_not_counted_as_idle_rest()
    {
        var r = CalRig(M(20, 30));
        r.Engine.StartDay(r.Now);
        r.Advance(Min(25));
        r.Probe.Idle = Min(10);                          // на встрече не трогаем мышь
        r.Advance(Min(1));
        Assert.False(r.Engine.Away);
    }

    [Fact]
    public void Break_offered_before_meeting_if_it_would_fall_into_it()
    {
        var r = CalRig(M(52, 60));                       // перерыв в 09:50 (5 мин) не успевает до встречи в 09:52
        r.Engine.StartDay(r.Now);
        r.Advance(Min(41.9));
        Assert.Equal(0, r.RemindersShown);
        r.Advance(Min(0.2));                             // за 10 мин до встречи
        Assert.Equal(1, r.RemindersShown);
        Assert.StartsWith("Через 10 мин встреча «Планёрка»", r.Engine.ReminderNote);
        r.Engine.Answer(ReminderResult.Done, r.Now);
        Assert.Null(r.Engine.ReminderNote);
    }

    [Fact]
    public void No_early_break_if_it_fits_before_meeting_or_was_recent()
    {
        // перерыв в 09:50 попадает на встречу 09:30–10:00: ждать её конца (10 мин) лучше, чем отдыхать на 20+ мин раньше
        var r = CalRig(M(30, 30), M(150, 30, "Поздно"));
        r.Engine.StartDay(r.Now);
        r.Advance(Min(29.9));
        Assert.Equal(0, r.RemindersShown);
        r.Advance(Min(30.2));                            // 10:00 — встреча кончилась, обычный перерыв (ждал её конца)
        Assert.Equal(1, r.RemindersShown);
        Assert.Null(r.Engine.ReminderNote);
    }

    [Fact]
    public void Meeting_alert_fires_once_n_minutes_before()
    {
        var r = CalRig(M(30, 30));
        Meeting? alerted = null;
        int count = 0;
        r.Engine.MeetingSoon += m => { alerted = m; count++; };
        r.Engine.StartDay(r.Now);
        r.Advance(Min(24.9));
        Assert.Equal(0, count);
        r.Advance(Min(1));                               // 09:25:54 — за 5 мин
        Assert.Equal(1, count);
        Assert.Equal("Планёрка", alerted!.Title);
        r.Advance(Min(10));
        Assert.Equal(1, count);
    }

    [Fact]
    public void Long_meeting_chain_triggers_long_break_right_after()
    {
        // 09:10–10:00, 10:05–11:00, 11:00–11:20: 2 ч 10 мин подряд
        var r = CalRig(M(10, 50, "А"), M(65, 55, "Б"), M(120, 20, "В"));
        // короткие перерывы (например, в окне 10:00–10:05) сразу отмечаем «Сделал»
        r.Engine.ReminderDue += isLong => { if (!isLong) r.Engine.Answer(ReminderResult.Done, r.Now); };
        r.Engine.StartDay(r.Now);
        r.Advance(Min(141));                             // 11:21
        Assert.True(r.Engine.ReminderOpen);
        Assert.True(r.LastWasLong);
        Assert.StartsWith("После 2 ч 10 мин встреч подряд", r.Engine.ReminderNote);
        r.Engine.Answer(ReminderResult.Done, r.Now);
        Assert.False(r.Engine.NextIsLong);
    }

    [Fact]
    public void Short_chain_does_not_force_long_break()
    {
        var r = CalRig(M(5, 30), M(40, 30));             // 1 ч 05 мин
        r.Engine.StartDay(r.Now);
        r.Advance(Min(71));
        Assert.False(r.Engine.NextIsLong);
    }

    [Fact]
    public void Calendar_off_ignores_meetings()
    {
        var r = new Rig(Monday9);
        r.Engine.Meetings = new[] { M(40, 30) };
        r.Engine.StartDay(r.Now);
        r.Advance(Min(50.1));
        Assert.Equal(1, r.RemindersShown);
        Assert.Null(r.Engine.CurrentMeeting(r.Now));
    }
}
