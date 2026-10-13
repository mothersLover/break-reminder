namespace BreakReminder.Core;

public enum WorkState { Idle, Running, Paused }
public enum ReminderResult { Done, Snooze, Skip }

/// <summary>Итоги рабочего дня — для отчёта в конце дня.</summary>
public sealed record DayReport(
    DateTime Start, DateTime End, TimeSpan Worked,
    int Done, int Skipped, int Snoozed, int EyeRests,
    int Meetings, TimeSpan MeetingTime, bool Auto);

/// <summary>«Главное на завтра» из вечернего отчёта — показывается в утреннем приветствии.</summary>
public static class DayPlan
{
    static string FilePath => Path.Combine(Paths.DataDir, "plan.txt");

    public static void Save(DateTime date, string text)
    {
        try
        {
            Directory.CreateDirectory(Paths.DataDir);
            if (string.IsNullOrWhiteSpace(text)) File.Delete(FilePath);
            else File.WriteAllText(FilePath, date.ToString("yyyy-MM-dd") + "\n" + text.Trim());
        }
        catch { }
    }

    /// <summary>План, записанный в последние несколько дней (до сегодня включительно), или null.</summary>
    public static string? Load(DateTime today)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var lines = File.ReadAllText(FilePath).Split('\n', 2);
            if (lines.Length < 2 || !DateTime.TryParse(lines[0], System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var d)) return null;
            return d.Date <= today.Date && today.Date - d.Date <= TimeSpan.FromDays(4) ? lines[1].Trim() : null;
        }
        catch { return null; }
    }
}

/// <summary>Состояние рабочего дня для перезапуска программы (session.json).</summary>
public sealed class EngineSnapshot
{
    public DateTime SavedAt { get; set; }
    public bool Paused { get; set; }
    public DateTime PauseStart { get; set; }
    public DateTime DayStart { get; set; }
    public TimeSpan Worked { get; set; }
    public TimeSpan Remaining { get; set; }
    public TimeSpan SinceLastBreak { get; set; }
    public int Done { get; set; }
    public int Skipped { get; set; }
    public int Snoozed { get; set; }
    public int Cycle { get; set; }
    public int EyeRests { get; set; }
    public bool ForceLong { get; set; }

    public static string FilePath => Path.Combine(Paths.DataDir, "session.json");

    public void Save()
    {
        Directory.CreateDirectory(Paths.DataDir);
        File.WriteAllText(FilePath, System.Text.Json.JsonSerializer.Serialize(this));
    }

    /// <summary>Прочитать и удалить. Старше maxAge — не восстанавливаем (значит, перезапуск не удался).</summary>
    public static EngineSnapshot? TakeSaved(DateTime now, TimeSpan maxAge)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var s = System.Text.Json.JsonSerializer.Deserialize<EngineSnapshot>(File.ReadAllText(FilePath));
            File.Delete(FilePath);
            return s != null && now - s.SavedAt <= maxAge && now >= s.SavedAt ? s : null;
        }
        catch { return null; }
    }
}

/// <summary>
/// Состояние рабочего дня: время работы, отсчёт до перерыва, большие перерывы, «не беспокоить»,
/// учёт сна/простоя/паузы как отдыха, автоматическое начало и конец дня.
/// Не знает ни про окна, ни про ОС: время передаётся в каждый вызов, система — через IEnvironmentProbe.
/// </summary>
public sealed class WorkDayEngine
{
    readonly IEnvironmentProbe probe;
    readonly IHistoryStore history;
    AppSettings settings;

    DateTime lastTick, pauseStart, lastDayStart, lastBusyCheck = DateTime.MinValue;
    bool callActive, fullscreenActive;
    TimeSpan awayIdle;

    // календарь
    static readonly TimeSpan ChainGap = TimeSpan.FromMinutes(10); // встречи с паузой ≤ 10 мин — «подряд»
    readonly HashSet<string> alertedMeetings = new();
    readonly HashSet<string> breakBeforeOffered = new();
    Meeting? lastMeeting;      // встреча, в которой были на прошлом тике
    DateTime chainStart;
    bool forceLong;            // следующий перерыв — большой (после длинной серии встреч)
    string? pendingNote;       // пояснение к ближайшему напоминанию

    public WorkDayEngine(AppSettings settings, IEnvironmentProbe probe, IHistoryStore history, DateTime now)
    {
        this.settings = settings;
        this.probe = probe;
        this.history = history;
        lastTick = now;
        lastDayStart = history.LastStart();
        StatusText = "Нажми «Начать», когда садишься за работу";
    }

    // ---------- состояние (только чтение для UI) ----------

    public WorkState State { get; private set; } = WorkState.Idle;
    public TimeSpan Worked { get; private set; }          // время работы за день (без пауз)
    public TimeSpan Remaining { get; private set; }       // до следующего напоминания
    public TimeSpan SinceLastBreak { get; private set; }  // сколько работаешь без настоящего перерыва
    public DateTime DayStart { get; private set; }
    public int Done { get; private set; }
    public int Skipped { get; private set; }
    public int Snoozed { get; private set; }
    public int Cycle { get; private set; }                // сделано обычных перерывов с последнего большого
    public bool Away { get; private set; }
    public string? DndReason { get; private set; }        // почему напоминание сейчас ждёт (null — не ждёт)
    public bool ReminderOpen { get; private set; }
    public bool ReminderIsLong { get; private set; }
    public string StatusText { get; private set; }

    /// <summary>Пояснение к открытому напоминанию («через 8 мин встреча…»), или null.</summary>
    public string? ReminderNote { get; private set; }

    /// <summary>Встречи из календаря (задаёт приложение после загрузки).</summary>
    public IReadOnlyList<Meeting> Meetings { get; set; } = Array.Empty<Meeting>();

    public bool NextIsLong => forceLong || (settings.LongBreaks && Cycle >= settings.LongEvery - 1);

    public Meeting? CurrentMeeting(DateTime now) =>
        settings.CalendarOn ? Meetings.Where(m => m.Start <= now && now < m.End).MaxBy(m => m.End) : null;

    public Meeting? NextMeeting(DateTime now) =>
        settings.CalendarOn ? Meetings.Where(m => m.Start > now).MinBy(m => m.Start) : null;

    public AppSettings Settings
    {
        get => settings;
        set
        {
            settings = value;
            if (Remaining > value.Interval) Remaining = value.Interval;
        }
    }

    public DayStat TodaySoFar() => new() { Minutes = (int)Worked.TotalMinutes, Done = Done, Skipped = Skipped, Snoozed = Snoozed };

    // ---------- события для UI ----------

    /// <summary>Пора показать напоминание (аргумент — большой ли перерыв).</summary>
    public event Action<bool>? ReminderDue;
    /// <summary>Напоминание нужно закрыть без ответа (пауза, конец дня).</summary>
    public event Action? ReminderCancelled;
    /// <summary>Рабочий день начался (аргумент — автоматически ли). Не вызывается при восстановлении после обновления.</summary>
    public event Action<bool>? DayStarted;
    /// <summary>Рабочий день закончился — итоги для отчёта (второй аргумент — автоматически ли).</summary>
    public event Action<DayReport, bool>? DayEnded;
    /// <summary>Пора дать отдых глазам (20-20-20).</summary>
    public event Action? EyeRestDue;
    /// <summary>Скоро встреча — показать напоминание о ней.</summary>
    public event Action<Meeting>? MeetingSoon;

    /// <summary>Сколько раз за день был отдых для глаз.</summary>
    public int EyeRests { get; private set; }
    /// <summary>Идёт перерыв с таймером (нажали «Начать перерыв»).</summary>
    public bool OnBreak { get; private set; }
    public DateTime BreakUntil { get; private set; }

    TimeSpan eyeElapsed;      // работа с последнего отдыха глаз

    // ---------- основной цикл ----------

    public void Tick(DateTime now)
    {
        DateTime prevTick = lastTick;
        TimeSpan delta = now - lastTick;
        lastTick = now;
        if (delta < TimeSpan.Zero) delta = TimeSpan.Zero; // перевели часы назад

        if (CheckAutoDay(now, prevTick)) return;
        if (State != WorkState.Idle) CheckMeetingAlerts(now);
        if (State != WorkState.Running) return;

        Worked += delta;
        SinceLastBreak += delta;
        TrackMeetingChain(now);
        if (ReminderOpen) return; // ждём ответа — отсчёт стоит

        TimeSpan rest = settings.BreakSpan;
        TimeSpan idle;
        if (delta >= rest)
        {
            // Между тиками прошло много времени — компьютер спал.
            ResetAfterRest("Компьютер спал " + Format.Minutes(delta) + " — засчитано как перерыв", delta);
        }
        else if (settings.IdleDetect && (idle = probe.IdleTime()) >= rest && !BusyWithoutInput(now))
        {
            // Созвон или видео на весь экран без мыши — это не отдых.
            if (!Away) StatusText = "Ты отошёл — засчитываю это как отдых";
            Away = true;
            awayIdle = idle;
            Remaining = settings.Interval;
            SinceLastBreak = TimeSpan.Zero;
            DndReason = null;
            eyeElapsed = TimeSpan.Zero;
        }
        else
        {
            if (Away)
            {
                Away = false;
                if (awayIdle >= settings.LongBreakSpan) Cycle = 0;
                StatusText = "С возвращением! Следующий перерыв в " + NextAt(now);
            }
            if (Remaining > TimeSpan.Zero) Remaining -= delta;
            CheckEyeRest(now, delta);
            CheckBreakBeforeMeeting(now);
            if (Remaining <= TimeSpan.Zero)
            {
                string? why = DndReasonNow(now);
                if (why == null)
                {
                    DndReason = null;
                    ShowReminder();
                }
                else if (why != DndReason)
                {
                    DndReason = why;
                    StatusText = "Не беспокою: " + why + ". Напомню, когда закончится";
                }
            }
        }
    }

    // ---------- действия пользователя ----------

    public void StartDay(DateTime now, bool auto = false)
    {
        State = WorkState.Running;
        Worked = SinceLastBreak = eyeElapsed = TimeSpan.Zero;
        Remaining = settings.Interval;
        Done = Skipped = Snoozed = Cycle = EyeRests = 0;
        OnBreak = false;
        Away = false;
        DndReason = null;
        forceLong = false;
        pendingNote = null;
        lastMeeting = null;
        breakBeforeOffered.Clear();
        DayStart = lastTick = lastDayStart = now;
        StatusText = (auto ? "Рабочий день начат автоматически." : "Рабочий день начат.")
            + " Первый перерыв в " + NextAt(now)
            + (settings.AutoDay ? "\nАвтозавершение в " + AutoEndFor(DayStart).ToString("HH:mm") : "");
        DayStarted?.Invoke(auto);
    }

    public void EndDay(DateTime now, bool auto = false)
    {
        if (State == WorkState.Idle) return;
        CloseReminder();
        State = WorkState.Idle;
        OnBreak = false;
        history.Append(DayStart, now, TodaySoFar());
        StatusText = $"День окончен: {Format.Minutes(Worked)} работы\nперерывов {Done}, пропущено {Skipped}";
        DayEnded?.Invoke(BuildReport(now, auto), auto);
    }

    DayReport BuildReport(DateTime end, bool auto)
    {
        int meetings = 0;
        TimeSpan meetingTime = TimeSpan.Zero;
        if (settings.CalendarOn)
            foreach (var m in Meetings)
            {
                var from = m.Start > DayStart ? m.Start : DayStart;
                var to = m.End < end ? m.End : end;
                if (to <= from) continue;
                meetings++;
                meetingTime += to - from;
            }
        return new DayReport(DayStart, end, Worked, Done, Skipped, Snoozed, EyeRests, meetings, meetingTime, auto);
    }

    /// <summary>Нажали «Начать перерыв»: отсчёт перерыва (напоминание остаётся открытым до «Вернуться к работе»).</summary>
    public TimeSpan BeginBreak(DateTime now)
    {
        var length = ReminderIsLong ? settings.LongBreakSpan : settings.BreakSpan;
        OnBreak = true;
        BreakUntil = now + length;
        StatusText = (ReminderIsLong ? "Большой перерыв" : "Перерыв") + " до " + BreakUntil.ToString("HH:mm");
        return length;
    }

    /// <summary>20-20-20: каждые N минут работы — отдых для глаз, если сейчас уместно.</summary>
    void CheckEyeRest(DateTime now, TimeSpan delta)
    {
        if (!settings.EyeRest) return;
        eyeElapsed += delta;
        if (eyeElapsed < TimeSpan.FromMinutes(settings.EyeEveryMin)) return;
        eyeElapsed = TimeSpan.Zero;
        // Не мешаем: скоро настоящий перерыв, идёт встреча/созвон/полноэкранная программа.
        if (Remaining <= TimeSpan.FromMinutes(2)) return;
        if (CurrentMeeting(now) != null || BusyWithoutInput(now)) return;
        EyeRests++;
        EyeRestDue?.Invoke();
    }

    public void TogglePause(DateTime now)
    {
        if (State == WorkState.Running)
        {
            State = WorkState.Paused;
            pauseStart = now;
            CloseReminder(); // напоминание вернётся после паузы, если перерыв так и не сделан
            StatusText = "Пауза — время и напоминания остановлены";
        }
        else if (State == WorkState.Paused)
        {
            TimeSpan p = now - pauseStart;
            State = WorkState.Running;
            lastTick = now;
            eyeElapsed = TimeSpan.Zero;
            if (p >= settings.BreakSpan)
                ResetAfterRest("Пауза " + Format.Minutes(p) + " — засчитано как перерыв", p);
            else
                StatusText = "Продолжаем";
        }
    }

    public void Answer(ReminderResult r, DateTime now)
    {
        if (!ReminderOpen) return;
        ReminderOpen = false;
        OnBreak = false;
        ReminderNote = null;
        if (ReminderIsLong && r != ReminderResult.Snooze) forceLong = false;
        switch (r)
        {
            case ReminderResult.Done:
                Done++;
                Cycle = ReminderIsLong ? 0 : Cycle + 1;
                ResetAfterRest("Отлично! Следующий " + (NextIsLong ? "(большой) " : "") + "перерыв в "
                    + (now + settings.Interval).ToString("HH:mm"));
                break;
            case ReminderResult.Skip:
                Skipped++;
                Remaining = settings.Interval;
                StatusText = "Перерыв пропущен. Следующий в " + NextAt(now);
                break;
            default:
                Snoozed++;
                Remaining = settings.Snooze;
                StatusText = "Напомню через " + settings.SnoozeMin + " мин";
                break;
        }
    }

    // ---------- внутреннее ----------

    string NextAt(DateTime now) => (now + Remaining).ToString("HH:mm");

    void ShowReminder()
    {
        if (ReminderOpen) return;
        ReminderIsLong = NextIsLong;
        ReminderNote = pendingNote;
        pendingNote = null;
        ReminderOpen = true;
        ReminderDue?.Invoke(ReminderIsLong);
    }

    void CloseReminder()
    {
        if (!ReminderOpen) return;
        ReminderOpen = false;
        OnBreak = false;
        pendingNote ??= ReminderNote; // вернётся вместе с напоминанием
        ReminderNote = null;
        ReminderCancelled?.Invoke();
    }

    void ResetAfterRest(string message)
    {
        Remaining = settings.Interval;
        SinceLastBreak = TimeSpan.Zero;
        eyeElapsed = TimeSpan.Zero;
        DndReason = null;
        pendingNote = null;
        StatusText = message;
    }

    /// <summary>Отдых известной длины: если он не короче большого перерыва — цикл начинается заново.</summary>
    void ResetAfterRest(string message, TimeSpan restLength)
    {
        if (restLength >= settings.LongBreakSpan)
        {
            Cycle = 0;
            forceLong = false;
        }
        ResetAfterRest(message);
    }

    // ---------- календарь ----------

    /// <summary>За N минут до встречи — событие MeetingSoon (один раз на встречу).</summary>
    void CheckMeetingAlerts(DateTime now)
    {
        if (!settings.CalendarOn || !settings.CalMeetingAlert) return;
        var lead = TimeSpan.FromMinutes(settings.CalMeetingAlertMin);
        foreach (var m in Meetings)
        {
            if (m.Start <= now || m.Start - now > lead || !alertedMeetings.Add(m.Key)) continue;
            MeetingSoon?.Invoke(m);
        }
    }

    /// <summary>
    /// Перерыв выпадает на встречу, а до неё осталось ≤ N минут — предлагаем отдохнуть сейчас.
    /// Только если так лучше: отдохнуть раньше срока на меньшее время, чем пришлось бы ждать конца встреч.
    /// Не дёргаем, если перерыв был совсем недавно (меньше половины интервала назад).
    /// </summary>
    void CheckBreakBeforeMeeting(DateTime now)
    {
        if (!settings.CalendarOn || !settings.CalBreakBefore || Remaining <= TimeSpan.Zero) return;
        if (CurrentMeeting(now) != null) return;
        var next = NextMeeting(now);
        if (next == null) return;
        var until = next.Start - now;
        if (until > TimeSpan.FromMinutes(settings.CalBreakBeforeMin)) return;
        DateTime due = now + Remaining;
        if (due + settings.BreakSpan <= next.Start) return;                 // перерыв успевает до встречи
        TimeSpan early = Remaining, late = ChainEnd(next) - due;           // насколько раньше / позже срока
        if (early > late) return;                                           // лучше подождать конца встреч
        if (SinceLastBreak < TimeSpan.FromTicks(settings.Interval.Ticks / 2)) return;
        if (!breakBeforeOffered.Add(next.Key)) return;

        Remaining = TimeSpan.Zero;
        pendingNote = $"Через {Math.Max(1, (int)Math.Round(until.TotalMinutes))} мин встреча «{next.Title}» — лучше отдохнуть сейчас";
    }

    /// <summary>Конец серии встреч, начинающейся с этой (встречи с паузой ≤ 10 мин считаем подряд).</summary>
    DateTime ChainEnd(Meeting first)
    {
        DateTime end = first.End;
        bool extended = true;
        while (extended)
        {
            extended = false;
            foreach (var m in Meetings)
                if (m.Start <= end + ChainGap && m.End > end)
                {
                    end = m.End;
                    extended = true;
                }
        }
        return end;
    }

    /// <summary>
    /// Следим за серией встреч подряд; когда серия длиной ≥ порога закончилась — большой перерыв сразу.
    /// Если в этот момент напоминание и так открыто — ничего не навязываем.
    /// </summary>
    void TrackMeetingChain(DateTime now)
    {
        var cm = CurrentMeeting(now);
        if (cm != null)
        {
            if (lastMeeting == null || cm.Start - lastMeeting.End > ChainGap) chainStart = cm.Start;
            lastMeeting = cm;
            return;
        }
        if (lastMeeting == null) return;

        // Между встречами: если следующая скоро — серия продолжается.
        var next = NextMeeting(now);
        if (next != null && next.Start - lastMeeting.End <= ChainGap) return;

        var chain = lastMeeting.End - chainStart;
        lastMeeting = null;
        if (ReminderOpen || !settings.CalChainLong || chain < TimeSpan.FromMinutes(settings.CalChainMin)) return;

        forceLong = true;
        Remaining = TimeSpan.Zero;
        pendingNote = $"После {Format.Minutes(chain)} встреч подряд — большой перерыв";
    }

    void RefreshBusy(DateTime now)
    {
        if (now - lastBusyCheck < TimeSpan.FromSeconds(2) && now >= lastBusyCheck) return;
        lastBusyCheck = now;
        callActive = probe.IsCallActive();
        fullscreenActive = probe.IsFullscreenAppActive();
    }

    bool BusyWithoutInput(DateTime now)
    {
        RefreshBusy(now);
        return callActive || fullscreenActive || CurrentMeeting(now) != null; // встреча — не отдых
    }

    string? DndReasonNow(DateTime now)
    {
        if (settings.CalWaitMeetings && CurrentMeeting(now) is { } m) return $"встреча «{m.Title}» до {m.End:HH:mm}";
        RefreshBusy(now);
        if (settings.DndCalls && callActive) return "идёт созвон";
        if (settings.DndFullscreen && fullscreenActive) return "открыта полноэкранная программа";
        return null;
    }

    // ---------- автоматический рабочий день ----------

    /// <summary>Если сейчас рабочее «окно» (между началом и концом), возвращает момент его начала.</summary>
    internal DateTime? CurrentAutoWindowStart(DateTime now)
    {
        TimeSpan s = settings.AutoStartTime, e = settings.AutoEndTime, t = now.TimeOfDay;
        if (s == e) return null;
        if (s < e) return t >= s && t < e ? now.Date + s : null;
        // окно через полночь (например, 22:00 → 06:00)
        if (t >= s) return now.Date + s;
        if (t < e) return now.Date.AddDays(-1) + s;
        return null;
    }

    /// <summary>Ближайшее время конца после начала дня.</summary>
    internal DateTime AutoEndFor(DateTime start)
    {
        DateTime end = start.Date + settings.AutoEndTime;
        return end <= start ? end.AddDays(1) : end;
    }

    internal DateTime NextAutoStart(DateTime now)
    {
        DateTime d = now.Date + settings.AutoStartTime;
        return d <= now ? d.AddDays(1) : d;
    }

    /// <summary>Автоначало/автоконец дня. true — состояние поменялось, тик дальше не обрабатываем.</summary>
    bool CheckAutoDay(DateTime now, DateTime prevTick)
    {
        if (!settings.AutoDay) return false;

        if (State == WorkState.Idle)
        {
            // Один раз за «окно»: если в этом окне день уже начинали (вручную или сами) — не трогаем.
            DateTime? ws = CurrentAutoWindowStart(now);
            if (ws == null || lastDayStart >= ws.Value || probe.IsScreenLocked()) return false;
            StartDay(now, auto: true);
            return true;
        }

        DateTime endAt = AutoEndFor(DayStart);
        if (now < endAt) return false;
        // Если компьютер спал и проснулся после конца — работу считаем только до времени конца.
        if (State == WorkState.Running && endAt > prevTick) Worked += endAt - prevTick;
        EndDay(endAt, auto: true);
        StatusText = $"День завершён автоматически в {endAt:HH:mm}: {Format.Minutes(Worked)} работы\nперерывов {Done}, пропущено {Skipped}";
        return true;
    }

    // ---------- перезапуск без потери дня (обновление программы) ----------

    /// <summary>Состояние идущего дня; null, если день не начат.</summary>
    public EngineSnapshot? Snapshot(DateTime now) => State == WorkState.Idle ? null : new EngineSnapshot
    {
        SavedAt = now,
        Paused = State == WorkState.Paused,
        PauseStart = pauseStart,
        DayStart = DayStart,
        Worked = Worked,
        Remaining = ReminderOpen ? TimeSpan.Zero : Remaining, // открытое напоминание покажется снова
        SinceLastBreak = SinceLastBreak,
        Done = Done,
        Skipped = Skipped,
        Snoozed = Snoozed,
        Cycle = Cycle,
        EyeRests = EyeRests,
        ForceLong = forceLong
    };

    /// <summary>Продолжить день после перезапуска. Время перезапуска засчитывается как работа.</summary>
    public void Restore(EngineSnapshot s, string message)
    {
        State = s.Paused ? WorkState.Paused : WorkState.Running;
        pauseStart = s.PauseStart;
        DayStart = lastDayStart = s.DayStart;
        Worked = s.Worked;
        Remaining = s.Remaining;
        SinceLastBreak = s.SinceLastBreak;
        Done = s.Done;
        Skipped = s.Skipped;
        Snoozed = s.Snoozed;
        Cycle = s.Cycle;
        EyeRests = s.EyeRests;
        forceLong = s.ForceLong;
        lastTick = s.SavedAt;
        ReminderOpen = false;
        StatusText = message;
    }

    /// <summary>Подсказка для главного окна, пока день не начат.</summary>
    public string AutoStartHint(DateTime now)
    {
        if (!settings.AutoDay || State != WorkState.Idle) return "";
        DateTime? ws = CurrentAutoWindowStart(now);
        if (ws != null && lastDayStart < ws.Value) return "Начну после разблокировки экрана";
        DateTime next = NextAutoStart(now);
        return (next.Date == now.Date ? "Автостарт сегодня в " : "Автостарт завтра в ") + next.ToString("HH:mm");
    }
}
