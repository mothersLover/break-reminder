using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class PracticeTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), "br-practice-" + Guid.NewGuid().ToString("N") + ".csv");
    static readonly DateTime Morning = new(2026, 10, 21, 10, 0, 0);

    public void Dispose()
    {
        try { File.Delete(file); } catch { }
    }

    /// <summary>Движок + тренер; перерывы редкие (раз в 4 ч), чтобы не мешали.</summary>
    sealed class CoachRig
    {
        public readonly Rig R;
        public readonly PracticeCoach Coach;
        public readonly PracticeLog Log;
        public readonly List<(PracticeKind Kind, DateTime At)> Offers = new();

        public CoachRig(DateTime start, string file, Action<AppSettings>? configure = null)
        {
            R = new Rig(start, s =>
            {
                s.IntervalMin = 240;
                s.AiOn = true;
                configure?.Invoke(s);
            });
            Log = new PracticeLog(file);
            Coach = new PracticeCoach(R.Settings, R.Engine, R.Probe, Log);
            Coach.Due += k => Offers.Add((k, R.Now));
        }

        /// <summary>Идти по минуте: движок тикает как обычно, тренер — раз в минуту.</summary>
        public void Minutes(int n)
        {
            for (int i = 0; i < n; i++)
            {
                R.Advance(TimeSpan.FromMinutes(1));
                Coach.Tick(R.Now);
            }
        }

        /// <summary>Принять предложение и позаниматься min минут.</summary>
        public void Practice(int min)
        {
            var k = Offers[^1].Kind;
            Coach.Started(k, R.Now);
            Minutes(min);
            Coach.Finished(k, R.Now, min);
        }
    }

    [Fact]
    public void Morning_warmup_ten_minutes_after_start_once()
    {
        var c = new CoachRig(Morning, file, s => s.PracticeDayCount = 0);
        c.R.Engine.StartDay(c.R.Now);
        c.Minutes(9);
        Assert.Empty(c.Offers);
        c.Minutes(2);
        Assert.Single(c.Offers);
        Assert.Equal(PracticeKind.Morning, c.Offers[0].Kind);
        Assert.Equal(Morning.AddMinutes(10), c.Offers[0].At);

        c.Practice(5);
        c.Minutes(120);
        Assert.Single(c.Offers);                                   // второй раз не предлагает
        Assert.Equal(5, c.Log.MinutesOn(Morning));
        Assert.Equal(1, c.Log.Streak(Morning));
        Assert.Single(new PracticeLog(file).Entries);              // журнал сохранился
    }

    [Fact]
    public void Waits_for_a_good_moment()
    {
        var c = new CoachRig(Morning, file, s => s.PracticeDayCount = 0);
        c.R.Engine.StartDay(c.R.Now);
        c.R.Probe.Call = true;                                     // созвон
        c.Minutes(20);
        Assert.Empty(c.Offers);
        c.R.Probe.Call = false;
        c.R.Probe.Idle = TimeSpan.FromMinutes(3);                  // отошёл (но меньше перерыва — работа идёт)
        c.R.Settings.IdleDetect = false;
        c.Minutes(5);
        Assert.Empty(c.Offers);
        c.R.Probe.Idle = TimeSpan.Zero;
        c.R.Engine.TogglePause(c.R.Now);                           // пауза
        c.Minutes(5);
        Assert.Empty(c.Offers);
        c.R.Engine.TogglePause(c.R.Now);
        c.Minutes(1);
        Assert.Single(c.Offers);
    }

    [Fact]
    public void Not_right_before_a_break()
    {
        var c = new CoachRig(Morning, file, s => { s.IntervalMin = 18; s.PracticeDayCount = 0; });
        c.R.Engine.StartDay(c.R.Now);
        c.Minutes(11);                                             // до перерыва 7 мин < 5 мин занятия + 5
        Assert.Empty(c.Offers);
    }

    [Fact]
    public void Snooze_and_skip()
    {
        var c = new CoachRig(Morning, file);
        c.R.Engine.StartDay(c.R.Now);
        c.Minutes(10);
        Assert.Single(c.Offers);
        c.Coach.Snooze(PracticeKind.Morning, c.R.Now);
        c.Minutes(29);
        Assert.Single(c.Offers);
        c.Minutes(1);
        Assert.Equal(2, c.Offers.Count);
        Assert.Equal(PracticeKind.Morning, c.Offers[1].Kind);
        c.Coach.Skip(PracticeKind.Morning, c.R.Now);

        c.Minutes(60);                                             // 11:40 — первая мини-тренировка в 11:30
        Assert.Equal(PracticeKind.Day, c.Offers[^1].Kind);
        c.Coach.Skip(PracticeKind.Day, c.R.Now);                   // «не сегодня» — больше днём не предлагать
        c.Minutes(300);
        Assert.Equal(3, c.Offers.Count);
    }

    [Fact]
    public void Day_sessions_spread_and_limited()
    {
        var c = new CoachRig(Morning, file, s => { s.PracticeMorning = false; s.PracticeEvening = false; s.PracticeDayCount = 2; });
        c.R.Engine.StartDay(c.R.Now);
        c.Minutes(89);
        Assert.Empty(c.Offers);
        c.Minutes(1);
        Assert.Equal(Morning.AddMinutes(90), c.Offers[0].At);
        c.Practice(3);
        c.Minutes(100);
        Assert.Single(c.Offers);                                   // промежуток — 2 часа после прошлой тренировки
        c.Minutes(20);
        Assert.Equal(2, c.Offers.Count);
        Assert.Equal(Morning.AddMinutes(90 + 3 + 120), c.Offers[1].At);
        c.Practice(3);
        c.Minutes(400);
        Assert.Equal(2, c.Offers.Count);                           // не больше двух
    }

    [Fact]
    public void Evening_session_even_without_work_day()
    {
        var c = new CoachRig(Morning.Date.AddHours(19).AddMinutes(50), file);
        c.Minutes(9);
        Assert.Empty(c.Offers);
        c.Minutes(1);
        Assert.Single(c.Offers);
        Assert.Equal(PracticeKind.Evening, c.Offers[0].Kind);
        c.Practice(15);
        c.Minutes(60);
        Assert.Single(c.Offers);
    }

    [Fact]
    public void Missed_evening_is_not_offered_late_at_night()
    {
        var c = new CoachRig(Morning.Date.AddHours(23).AddMinutes(10), file);
        c.Minutes(30);
        Assert.Empty(c.Offers);
    }

    [Fact]
    public void Nothing_when_assistant_or_practice_is_off()
    {
        var c = new CoachRig(Morning.Date.AddHours(20), file, s => s.AiOn = false);
        c.Minutes(10);
        Assert.Empty(c.Offers);
        c.R.Settings.AiOn = true;
        c.R.Settings.PracticeOn = false;
        c.Minutes(10);
        Assert.Empty(c.Offers);
    }

    [Fact]
    public void Busy_flag_blocks_new_offers_until_answered()
    {
        var c = new CoachRig(Morning.Date.AddHours(20), file);
        c.Minutes(1);
        Assert.Single(c.Offers);
        Assert.True(c.Coach.Busy);                                 // предложение открыто
        c.Minutes(10);
        Assert.Single(c.Offers);
        c.Coach.Snooze(PracticeKind.Evening, c.R.Now);
        Assert.False(c.Coach.Busy);
    }

    [Fact]
    public void Abandoned_session_is_offered_again_later()
    {
        var c = new CoachRig(Morning.Date.AddHours(20), file);
        c.Minutes(1);
        c.Coach.Started(PracticeKind.Evening, c.R.Now);
        c.Coach.Abandoned(PracticeKind.Evening, c.R.Now);          // открыл и закрыл, не ответив
        Assert.Empty(c.Log.Entries);
        c.Minutes(29);
        Assert.Single(c.Offers);
        c.Minutes(1);
        Assert.Equal(2, c.Offers.Count);
    }

    [Fact]
    public void Streak_counts_consecutive_days()
    {
        var log = new PracticeLog(file);
        var d = Morning.Date;
        log.Done(d.AddDays(-3).AddHours(20), PracticeKind.Evening, 15);
        log.Done(d.AddDays(-2).AddHours(20), PracticeKind.Evening, 10);
        log.Done(d.AddDays(-1).AddHours(11), PracticeKind.Morning, 5);
        log.Skip(d.AddHours(10), PracticeKind.Morning);
        Assert.Equal(3, log.Streak(d));                            // сегодня ещё не занимался — серия по вчера
        log.Done(d.AddHours(20), PracticeKind.Evening, 15);
        Assert.Equal(4, new PracticeLog(file).Streak(d));
        Assert.Equal(15, log.MinutesOn(d));
    }

    [Fact]
    public void Plan_line_for_greeting()
    {
        var c = new CoachRig(Morning, file);
        Assert.Equal("Занятия: разминка около 10:10, 2 мини-тренировки днём, вечером в 20:00", c.Coach.PlanLine(Morning));
        c.R.Settings.PracticeOn = false;
        Assert.Null(c.Coach.PlanLine(Morning));
    }

    [Fact]
    public void Practice_settings_roundtrip()
    {
        string dir = Path.Combine(Path.GetTempPath(), "br-ps-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new AppSettings { PracticeOn = false, PracticeDayCount = 4, PracticeEveningAt = 21 * 60 + 30, PracticeEveningMin = 25 };
            string f(string n) => Path.Combine(dir, n);
            s.Save(dir, f("s.ini"), f("t.txt"), f("tl.txt"));
            var l = AppSettings.Load(f("s.ini"), f("t.txt"), f("tl.txt"));
            Assert.False(l.PracticeOn);
            Assert.Equal(4, l.PracticeDayCount);
            Assert.Equal(21 * 60 + 30, l.PracticeEveningAt);
            Assert.Equal(25, l.PracticeEveningMin);
            Assert.True(l.PracticeMorning);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
