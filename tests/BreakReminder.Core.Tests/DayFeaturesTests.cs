using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

public class BreakPlanTests
{
    [Theory]
    [InlineData(5, false)]
    [InlineData(15, true)]
    [InlineData(1, false)]
    [InlineData(30, true)]
    public void Plan_covers_whole_break_without_gaps(int minutes, bool isLong)
    {
        var duration = TimeSpan.FromMinutes(minutes);
        var plan = BreakPlan.Build(duration, isLong, new Random(1));
        Assert.Equal(TimeSpan.Zero, plan[0].Start);
        for (int i = 1; i < plan.Count; i++) Assert.Equal(plan[i - 1].End, plan[i].Start);
        Assert.Equal(duration, plan[^1].End);
        Assert.All(plan, s => Assert.True(s.Length >= TimeSpan.FromSeconds(20) || plan.Count == 1));
    }

    [Fact]
    public void Long_break_starts_outside()
    {
        var plan = BreakPlan.Build(TimeSpan.FromMinutes(15), true, new Random(1));
        Assert.Equal("На улицу", plan[0].Exercise.Title);
    }

    [Fact]
    public void Current_step_by_elapsed()
    {
        var plan = BreakPlan.Build(TimeSpan.FromMinutes(5), false, new Random(2));
        Assert.Same(plan[0], BreakPlan.Current(plan, TimeSpan.Zero));
        Assert.Same(plan[1], BreakPlan.Current(plan, plan[1].Start + TimeSpan.FromSeconds(1)));
        Assert.Same(plan[^1], BreakPlan.Current(plan, TimeSpan.FromHours(1)));
    }
}

public class EyeRestAndDayEventsTests
{
    static readonly DateTime Monday9 = new(2026, 10, 12, 9, 0, 0);
    static TimeSpan Min(double m) => TimeSpan.FromMinutes(m);

    [Fact]
    public void Eye_rest_every_20_minutes_but_not_right_before_break()
    {
        var r = new Rig(Monday9, s => s.EyeRest = true);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(20.1));
        Assert.Equal(1, r.EyeRests);
        r.Advance(Min(20));            // 40 мин
        Assert.Equal(2, r.EyeRests);
        r.Advance(Min(9.5));           // 49:30 — до перерыва < 2 мин: 60-я минута была бы, но мы ещё не дошли
        Assert.Equal(2, r.EyeRests);
        Assert.Equal(2, r.Engine.EyeRests);
    }

    [Fact]
    public void Eye_rest_skipped_during_call_and_reset_after_break()
    {
        var r = new Rig(Monday9, s => { s.EyeRest = true; s.IntervalMin = 90; });
        r.Engine.StartDay(r.Now);
        r.Probe.Call = true;
        r.Advance(Min(20.1));
        Assert.Equal(0, r.EyeRests);   // на созвоне не мешаем
        r.Probe.Call = false;
        r.Advance(Min(19.8));          // 39:54 — пропуск в 20:00 начал отсчёт заново
        Assert.Equal(0, r.EyeRests);
        r.Advance(Min(0.2));           // 40:06
        Assert.Equal(1, r.EyeRests);
    }

    [Fact]
    public void Guided_break_marks_on_break_until_answer()
    {
        var r = new Rig(Monday9);
        r.Engine.StartDay(r.Now);
        r.Advance(Min(50.1));
        var length = r.Engine.BeginBreak(r.Now);
        Assert.Equal(Min(5), length);
        Assert.True(r.Engine.OnBreak);
        Assert.Equal(r.Now + Min(5), r.Engine.BreakUntil);
        r.Advance(Min(5));
        r.Engine.Answer(ReminderResult.Done, r.Now);
        Assert.False(r.Engine.OnBreak);
        Assert.Equal(1, r.Engine.Done);
    }

    [Fact]
    public void Day_events_and_report_with_meetings()
    {
        var r = new Rig(Monday9, s => { s.CalendarOn = true; s.EyeRest = true; });
        r.Engine.Meetings = new[]
        {
            new Meeting(Monday9.AddMinutes(-30), Monday9.AddMinutes(30), "До начала дня"), // считаем с 9:00 — 30 мин
            new Meeting(Monday9.AddHours(2), Monday9.AddHours(3), "Ревью")
        };
        r.Engine.StartDay(r.Now);
        Assert.Equal(new[] { false }, r.Starts);
        r.Engine.ReminderDue += _ => r.Engine.Answer(ReminderResult.Done, r.Now);
        r.Advance(Min(240));
        r.Engine.EndDay(r.Now);

        var (rep, auto) = Assert.Single(r.Reports);
        Assert.False(auto);
        Assert.Equal(Min(240), rep.Worked);
        Assert.Equal(2, rep.Meetings);
        Assert.Equal(Min(90), rep.MeetingTime);
        Assert.True(rep.Done >= 3);
        Assert.True(rep.EyeRests >= 1);
    }
}

public class WeatherTests
{
    const string Forecast = """
        {
          "utc_offset_seconds": 25200,
          "daily": { "time": ["2026-10-13"], "weather_code": [3], "temperature_2m_max": [11.4], "temperature_2m_min": [3.6], "precipitation_probability_max": [40] },
          "hourly": {
            "time": ["2026-10-13T09:00", "2026-10-13T12:00", "2026-10-13T14:00", "2026-10-13T16:00"],
            "temperature_2m": [4.0, 9.0, 11.0, 10.0],
            "precipitation_probability": [10, 70, 5, 0],
            "weather_code": [3, 61, 2, 3]
          }
        }
        """;

    static DateTime NskToLocal(int h) => new DateTimeOffset(2026, 10, 13, h, 0, 0, TimeSpan.FromHours(7)).LocalDateTime;

    [Fact]
    public void Parses_forecast_into_local_time()
    {
        var w = Weather.ParseToday(Forecast);
        Assert.Equal(3.6, w.Min);
        Assert.Equal(11.4, w.Max);
        Assert.Equal(40, w.PrecipProbMax);
        Assert.Equal(NskToLocal(14), w.Hours[2].Time);
        Assert.Equal(61, w.At(NskToLocal(12).AddMinutes(30))!.Code);
    }

    [Fact]
    public void Best_walk_avoids_rain()
    {
        var w = Weather.ParseToday(Forecast);
        var best = w.BestWalk(NskToLocal(10), NskToLocal(18))!;
        Assert.Equal(NskToLocal(14), best.Time);  // в 12 дождь, в 14 теплее всего без осадков
    }

    [Fact]
    public void Texts()
    {
        var w = Weather.ParseToday(Forecast);
        Assert.Equal("Сейчас +4°, пасмурно. Днём +4°…+11°, вероятность осадков 40%.", Weather.Summary(w, NskToLocal(9)));
        Assert.Contains("пройдись лучше по офису", Weather.BreakLine(w, NskToLocal(12)));
        Assert.Contains("хорошее время выйти", Weather.BreakLine(w, NskToLocal(14)));
        Assert.Equal("-3°", Weather.Temp(-2.6));
        Assert.Equal("0°", Weather.Temp(0.2));
    }

    [Fact]
    public void Parses_places()
    {
        var list = Weather.ParsePlaces("""{"results":[{"name":"Новосибирск","latitude":55.04,"longitude":82.93,"country":"Россия","admin1":"Новосибирская область"}]}""");
        var p = Assert.Single(list);
        Assert.Equal("Новосибирск, Новосибирская область, Россия", p.Display);
        Assert.Empty(Weather.ParsePlaces("""{"generationtime_ms":0.5}"""));
    }
}
