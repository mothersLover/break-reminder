using BreakReminder.Core;

namespace BreakReminder.Core.Tests;

sealed class FakeProbe : IEnvironmentProbe
{
    public TimeSpan Idle;
    public bool Call, Fullscreen, Locked;

    public TimeSpan IdleTime() => Idle;
    public bool IsCallActive() => Call;
    public bool IsFullscreenAppActive() => Fullscreen;
    public bool IsScreenLocked() => Locked;
}

sealed class MemoryHistory : IHistoryStore
{
    public readonly List<(DateTime Start, DateTime End, DayStat Day)> Rows = new();
    public DateTime Last = DateTime.MinValue;

    public void Append(DateTime start, DateTime end, DayStat day) => Rows.Add((start, end, day));
    public DateTime LastStart() => Last;
    public Dictionary<DateTime, DayStat> LoadByDay() => new();
}

/// <summary>Удобная обёртка: движок + «часы», которые двигаются тиками по полсекунды.</summary>
sealed class Rig
{
    public readonly FakeProbe Probe = new();
    public readonly MemoryHistory History = new();
    public readonly AppSettings Settings = new() { IntervalMin = 50, BreakMin = 5, SnoozeMin = 5, LongEvery = 4, LongBreakMin = 15 };
    public readonly WorkDayEngine Engine;
    public DateTime Now;
    public int RemindersShown, RemindersCancelled;
    public bool LastWasLong;
    public readonly List<string> Notifications = new();

    public Rig(DateTime start, Action<AppSettings>? configure = null)
    {
        configure?.Invoke(Settings);
        Now = start;
        Engine = new WorkDayEngine(Settings, Probe, History, Now);
        Engine.ReminderDue += isLong => { RemindersShown++; LastWasLong = isLong; };
        Engine.ReminderCancelled += () => RemindersCancelled++;
        Engine.Notification += n => Notifications.Add(n);
    }

    /// <summary>Прокрутить время вперёд тиками по 0.5 с (как настоящий таймер).</summary>
    public void Advance(TimeSpan span)
    {
        DateTime end = Now + span;
        while (Now < end)
        {
            Now += TimeSpan.FromMilliseconds(500);
            if (Now > end) Now = end;
            Engine.Tick(Now);
        }
    }

    /// <summary>Один тик после большого скачка времени (сон компьютера).</summary>
    public void Jump(TimeSpan span)
    {
        Now += span;
        Engine.Tick(Now);
    }
}
