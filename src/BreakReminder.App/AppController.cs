using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using BreakReminder.Core;
using BreakReminder.Platform;

namespace BreakReminder.App;

/// <summary>Связывает движок рабочего дня, окна, трей и системные функции.</summary>
public sealed class AppController
{
    readonly IClassicDesktopStyleApplicationLifetime desktop;
    readonly IPlatform platform;
    readonly FileHistoryStore history = new();
    readonly TaskStore tasks = new();
    AgendaWindow? agenda;
    readonly TutorMemory memory = new();
    AssistantWindow? assistant;
    readonly PracticeLog practiceLog = new();
    readonly CardStore cards = new();
    CardsWindow? cardsWindow;
    readonly PracticeCoach coach;
    PracticeOfferWindow? practiceOffer;
    readonly WorkDayEngine engine;
    readonly MainWindow main;
    readonly TrayIcon tray;
    readonly NativeMenuItem miPause, miMain, miAssistant, miCards;
    readonly DispatcherTimer timer, calendarTimer, updateTimer, practiceTimer;
    AppSettings settings;
    ReminderWindow? reminder;
    UpdateWindow? updateWindow;
    bool exiting, trayHintShown, blinkPhase;
    string calendarStatus = "";
    int calendarRequest; // номер последней загрузки — чтобы старый ответ не перезаписал новый
    WeatherToday? weather;          // прогноз на сегодня (для приветствия и большого перерыва)
    DateTime weatherAt = DateTime.MinValue;

    public AppController(IClassicDesktopStyleApplicationLifetime desktop, bool startHidden)
    {
        this.desktop = desktop;
        platform = PlatformFactory.Create();
        platform.Initialize();
        settings = AppSettings.Load();

        engine = new WorkDayEngine(settings, platform, history, DateTime.Now);
        engine.ReminderDue += ShowReminder;
        engine.ReminderCancelled += CloseReminderWindow;
        engine.MeetingSoon += m => MeetingWindow.Show(m, settings, platform);
        engine.EyeRestDue += () => EyeRestWindow.Show(settings.EyeSeconds);
        engine.DayStarted += OnDayStarted;
        engine.DayEnded += OnDayEnded;
        engine.Tasks = tasks.All;
        engine.TaskDue += t => TaskAlertWindow.Show(t, settings, platform, tasks);
        tasks.Changed += () => Render();
        coach = new PracticeCoach(settings, engine, platform, practiceLog);
        coach.Due += OfferPractice;

        main = new MainWindow { Icon = Icons.Clock(Palette.Green) };
        main.MainClicked += OnMainClicked;
        main.PauseClicked += TogglePause;
        main.StatsClicked += OpenStats;
        main.SettingsClicked += OpenSettings;
        main.AgendaClicked += OpenAgenda;
        main.AssistantClicked += OpenAssistant;
        main.Closing += OnMainClosing;

        // ---- трей ----
        var menu = new NativeMenu();
        var miShow = new NativeMenuItem("Показать окно");
        miShow.Click += (_, _) => ShowMain();
        miPause = new NativeMenuItem("Пауза");
        miPause.Click += (_, _) => TogglePause();
        miMain = new NativeMenuItem("Начать день");
        miMain.Click += (_, _) => OnMainClicked();
        var miAgenda = new NativeMenuItem("Задачи на сегодня");
        miAgenda.Click += (_, _) => OpenAgenda();
        miAssistant = new NativeMenuItem("Помощник");
        miAssistant.Click += (_, _) => OpenAssistant();
        miCards = new NativeMenuItem("Карточки");
        miCards.Click += (_, _) => OpenCards();
        var miExit = new NativeMenuItem("Выход");
        miExit.Click += async (_, _) => await ExitWithConfirm();
        menu.Items.Add(miShow);
        menu.Items.Add(miPause);
        menu.Items.Add(miMain);
        menu.Items.Add(miAgenda);
        menu.Items.Add(miAssistant);
        menu.Items.Add(miCards);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(miExit);
        tray = new TrayIcon { Icon = Icons.Clock(Palette.Gray), ToolTipText = "Отдыхалка", Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => ShowMain();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });

        ApplyHotkey();

        // Выход из системы / Cmd+Q — сохранить день в историю.
        desktop.ShutdownRequested += (_, _) =>
        {
            if (exiting) return;
            exiting = true; // система выключается — отчёт не показываем, просто сохраняем день
            engine.EndDay(DateTime.Now);
        };

        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            engine.Tick(DateTime.Now);
            Render();
        };
        timer.Start();

        calendarTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        calendarTimer.Tick += (_, _) =>
        {
            RefreshCalendar();
            if (engine.State != WorkState.Idle && DateTime.Now - weatherAt > TimeSpan.FromHours(3)) _ = RefreshWeather();
        };
        calendarTimer.Start();
        RefreshCalendar();

        // После обновления — продолжить рабочий день с того же места.
        if (EngineSnapshot.TakeSaved(DateTime.Now, TimeSpan.FromMinutes(30)) is { } snap)
            engine.Restore(snap, $"Обновлено до версии {UpdateChecker.Current} — рабочий день продолжается");
        if (Program.AfterUpdate) ToastWindow.Show($"Программа обновлена до версии {UpdateChecker.Current}");

        // Проверка обновлений: через несколько секунд после запуска и раз в сутки.
        if (settings.UpdateOnStart && !Program.AfterUpdate)
            DispatcherTimer.RunOnce(() => CheckForUpdates(), TimeSpan.FromSeconds(5));
        updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
        updateTimer.Tick += (_, _) => { if (settings.UpdateDaily) CheckForUpdates(); };
        updateTimer.Start();

        // Занятия с помощником: проверять, не пора ли, раз в полминуты.
        practiceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        practiceTimer.Tick += (_, _) => coach.Tick(DateTime.Now);
        practiceTimer.Start();

        // Следующие запуски программы передают сюда команды ("show", "toggle-pause").
        SingleInstance.Listen(cmd => Dispatcher.UIThread.Post(() => HandleCommand(cmd)));

        Render();
        if (!startHidden) main.Show();
    }

    // ---------- отображение ----------

    void Render()
    {
        blinkPhase = !blinkPhase;
        var now = DateTime.Now;
        main.Render(engine, now, blinkPhase);
        int upcoming = (settings.CalendarOn ? engine.Meetings.Count(m => m.Start.Date == now.Date && m.End > now) : 0)
                       + tasks.ForDay(now).Count(t => !t.Done);
        main.SetAgendaCount(upcoming);
        main.SetAssistantVisible(settings.AiOn);
        miAssistant.IsVisible = settings.AiOn;
        miCards.IsVisible = settings.AiOn && cards.All.Count > 0;
        int due = settings.AiOn ? cards.DueCount(now) : 0;
        miCards.Header = due > 0 ? $"Карточки ({due})" : "Карточки";

        bool idle = engine.State == WorkState.Idle, paused = engine.State == WorkState.Paused;
        tray.Icon = Icons.Clock(idle ? Palette.Gray : paused ? Palette.Amber : engine.ReminderOpen ? Palette.Red : Palette.Green);
        string hint = engine.AutoStartHint(now);
        tray.ToolTipText = idle ? "Отдыхалка — " + (hint.Length > 0 ? hint.ToLowerInvariant() : "день не начат")
            : paused ? "Пауза · работа " + Format.Hms(engine.Worked)
            : engine.OnBreak ? "Перерыв · осталось " + Format.Ms(engine.BreakUntil - now)
            : "Работа " + Format.Hms(engine.Worked) + " · перерыв через " + Format.Ms(engine.Remaining);

        miPause.IsEnabled = !idle;
        miPause.Header = paused ? "Продолжить" : "Пауза";
        miMain.Header = idle ? "Начать день" : "Закончить день";
    }

    // ---------- действия ----------

    async void OnMainClicked()
    {
        if (engine.State == WorkState.Idle)
        {
            engine.StartDay(DateTime.Now);
        }
        else
        {
            ShowMain();
            if (await ConfirmDialog.Ask(main, "Закончить рабочий день?")) engine.EndDay(DateTime.Now);
        }
        Render();
    }

    void TogglePause()
    {
        engine.TogglePause(DateTime.Now);
        Render();
    }

    void ShowReminder(bool isLong)
    {
        settings.ReloadTips();
        // на большом перерыве — стоит ли выходить на улицу
        string? weatherLine = isLong && settings.Weather && weather != null && weather.Date == DateTime.Today
            ? Weather.BreakLine(weather, DateTime.Now) : null;
        reminder = new ReminderWindow(settings, engine.SinceLastBreak, false, isLong, platform, engine.ReminderNote,
            string.IsNullOrEmpty(weatherLine) ? null : weatherLine);
        reminder.BreakRequested += () =>
        {
            var length = engine.BeginBreak(DateTime.Now);
            Render();
            return length;
        };
        reminder.Answered += r =>
        {
            reminder = null;
            engine.Answer(r, DateTime.Now);
            Render();
        };
        reminder.Show();
    }

    void CloseReminderWindow()
    {
        reminder?.CloseSilently();
        reminder = null;
    }

    async void OpenSettings()
    {
        ShowMain();
        var w = new SettingsWindow(settings, platform, calendarStatus, engine.Meetings, memory);
        var result = await w.ShowDialog<AppSettings?>(main);
        if (result == null) return;
        bool calendarChanged = result.CalendarOn != settings.CalendarOn || result.CalendarUrl != settings.CalendarUrl;
        settings = result;
        settings.Save();
        engine.Settings = settings;
        coach.Settings = settings;
        platform.SetAutostart(w.AutostartChecked);
        ApplyHotkey();
        if (calendarChanged) RefreshCalendar();
        if (!settings.AiOn) assistant?.Close();   // помощника выключили
        else assistant?.Rebuild();                // могли сменить подключение
        Render();
        if (w.InstallRequested is { } release) ShowUpdateWindow(release);
    }

    // ---------- обновления ----------

    /// <summary>Тихая проверка: при ошибке сети молчим, пропущенную версию не предлагаем.</summary>
    async void CheckForUpdates()
    {
        try
        {
            var r = await UpdateChecker.GetLatestAsync();
            if (r == null || r.Version <= UpdateChecker.Current) return;
            if (r.Version.ToString() == settings.UpdateSkipVersion) return;
            ShowUpdateWindow(r);
        }
        catch { /* нет сети — проверим в следующий раз */ }
    }

    void ShowUpdateWindow(ReleaseInfo r)
    {
        if (updateWindow != null)
        {
            updateWindow.Activate();
            return;
        }
        updateWindow = new UpdateWindow(r, UpdateChecker.Current, engine.State != WorkState.Idle, platform);
        updateWindow.Ready += RestartForUpdate;
        updateWindow.SkipRequested += () =>
        {
            settings.UpdateSkipVersion = r.Version.ToString();
            settings.Save();
        };
        updateWindow.Closed += (_, _) => updateWindow = null;
        updateWindow.Show();
    }

    /// <summary>Новая версия скачана: сохранить день, подменить файл, запустить новую версию и выйти.</summary>
    void RestartForUpdate(string staged)
    {
        try
        {
            engine.Snapshot(DateTime.Now)?.Save();
            Updater.ApplyAndRestart(staged, startHidden: !main.IsVisible);
        }
        catch (Exception e)
        {
            try { File.Delete(EngineSnapshot.FilePath); } catch { }
            updateWindow?.Close();
            ToastWindow.Show("Не удалось установить обновление: " + e.Message);
            return;
        }
        Exit(endDay: false); // день продолжит новая версия
    }

    /// <summary>Загрузить встречи по ссылке (в фоне); при ошибке оставляем прежние.</summary>
    async void RefreshCalendar()
    {
        int request = ++calendarRequest;
        if (!settings.CalendarOn || string.IsNullOrWhiteSpace(settings.CalendarUrl))
        {
            engine.Meetings = Array.Empty<Meeting>();
            calendarStatus = settings.CalendarOn ? "Ссылка не указана." : "";
            return;
        }
        try
        {
            var now = DateTime.Now;
            var meetings = await CalendarFeed.FetchAsync(settings.CalendarUrl, now);
            if (request != calendarRequest) return;
            engine.Meetings = meetings;
            int today = meetings.Count(m => m.Start.Date == now.Date);
            calendarStatus = $"Обновлено в {DateTime.Now:HH:mm}. Встреч сегодня: {today}.";
            agenda?.Rebuild();
        }
        catch (Exception e)
        {
            if (request != calendarRequest) return;
            calendarStatus = $"Не удалось обновить ({DateTime.Now:HH:mm}): {e.Message}";
        }
        Render();
    }

    // ---------- задачи на сегодня ----------

    void OpenAgenda()
    {
        if (agenda != null)
        {
            agenda.Activate();
            return;
        }
        agenda = new AgendaWindow(tasks, () => engine.Meetings, settings.CalendarOn, () => calendarStatus, platform);
        agenda.Closed += (_, _) => agenda = null;
        agenda.Show();
    }

    // ---------- ИИ-помощник ----------

    void OpenAssistant()
    {
        if (!settings.AiOn) return;
        if (assistant != null)
        {
            if (assistant.WindowState == WindowState.Minimized) assistant.WindowState = WindowState.Normal;
            assistant.Activate();
            return;
        }
        assistant = new AssistantWindow(() => settings, memory, cards);
        assistant.CardsRequested += () => OpenCards();
        assistant.Closed += (_, _) => assistant = null;
        assistant.PracticeEnded += (kind, minutes) =>
        {
            if (minutes > 0) coach.Finished(kind, DateTime.Now, minutes);
            else coach.Abandoned(kind, DateTime.Now);
        };
        assistant.Show();
    }

    /// <summary>Карточки: просто повторение или (с kind) занятие по расписанию на minutes минут.</summary>
    void OpenCards(PracticeKind? kind = null, int? minutes = null)
    {
        if (cardsWindow != null)
        {
            if (kind is { } k) coach.Abandoned(k, DateTime.Now);   // окно уже открыто — занятие не начинаем заново
            cardsWindow.Activate();
            return;
        }
        cardsWindow = new CardsWindow(cards, minutes);
        cardsWindow.Closed += (_, _) => cardsWindow = null;
        cardsWindow.Finished += (reviewed, mins) =>
        {
            if (kind is not { } k) return;
            if (reviewed > 0) coach.Finished(k, DateTime.Now, mins);
            else coach.Abandoned(k, DateTime.Now);
        };
        cardsWindow.Show();
    }

    /// <summary>Пора позаниматься — предложение в углу экрана. Днём, если накопились карточки, — их повторение.</summary>
    void OfferPractice(PracticeKind kind)
    {
        practiceOffer?.Close();
        int due = cards.DueCount(DateTime.Now);
        bool useCards = kind == PracticeKind.Day && settings.PracticeCards && due >= 3;
        practiceOffer = new PracticeOfferWindow(kind, settings, platform, practiceLog.Streak(DateTime.Today), useCards ? due : null);
        practiceOffer.Answered += async choice =>
        {
            practiceOffer = null;
            var now = DateTime.Now;
            switch (choice)
            {
                case PracticeChoice.Start when useCards:
                    coach.Started(kind, now);
                    OpenCards(kind, PracticeText.Minutes(kind, settings));
                    break;
                case PracticeChoice.Start:
                    coach.Started(kind, now);
                    OpenAssistant();
                    if (assistant != null) await assistant.StartPractice(kind, PracticeText.Minutes(kind, settings));
                    else coach.Abandoned(kind, now);
                    break;
                case PracticeChoice.Later:
                    coach.Snooze(kind, now);
                    break;
                default:
                    coach.Skip(kind, now);
                    break;
            }
        };
        practiceOffer.Show();
    }

    // ---------- утро и вечер ----------

    async void OnDayStarted(bool auto)
    {
        if (!settings.Greeting)
        {
            if (auto) ToastWindow.Show("Рабочий день начат автоматически в " + DateTime.Now.ToString("HH:mm"));
            return;
        }
        string dayInfo = $"Перерыв каждые {settings.IntervalMin} мин"
                         + (settings.AutoDay ? $" · автозавершение в {DateTime.Today + settings.AutoEndTime:HH:mm}" : "")
                         + (auto ? " · день начат автоматически" : "")
                         + (coach.PlanLine(engine.DayStart) is { } plan ? "\n" + plan : "");
        var w = new GreetingWindow(settings, engine.Meetings, DayPlan.Load(DateTime.Today), dayInfo,
            tasks.ForDay(DateTime.Today).Where(t => !t.Done).ToList(),
            settings.AiOn ? cards.WordOfDay(DateTime.Today) : null, settings.AiOn ? cards.DueCount(DateTime.Now) : 0);
        w.Show();
        if (settings.Weather)
        {
            await RefreshWeather();
            w.SetWeather(weather, weatherError);
        }
    }

    string? weatherError;

    async Task RefreshWeather()
    {
        if (!settings.Weather) return;
        var (w, error) = await WeatherLoader.LoadAsync(settings);
        weatherAt = DateTime.Now;
        weatherError = error;
        if (w != null) weather = w;
        if (settings.HasWeatherPlace && error == null) settings.Save(); // если город нашёлся по названию — запомнить координаты
    }

    void OnDayEnded(DayReport report, bool auto)
    {
        if (exiting) return;                                   // выход из программы / выключение системы
        // Автоконец при заблокированном экране — никого нет, просто сохраняем в статистику (уже сохранено).
        if (auto && platform.IsScreenLocked()) return;
        if (!settings.DayReport)
        {
            if (auto) ToastWindow.Show("Рабочий день завершён: " + Format.Minutes(report.Worked) + " работы");
            return;
        }
        var w = new DayReportWindow(report, history.LoadByDay(), settings,
            settings.AiOn && settings.PracticeOn ? (practiceLog.MinutesOn(report.End), practiceLog.Streak(report.End)) : null);
        w.StatsRequested += () => { w.Close(); OpenStats(); };
        w.Show();
    }

    async void OpenStats()
    {
        ShowMain();
        var w = new StatsWindow(history.LoadByDay(), engine.State == WorkState.Idle ? null : engine.TodaySoFar(),
            platform, history.FilePath);
        await w.ShowDialog(main);
    }

    void ApplyHotkey()
    {
        platform.UnregisterPauseHotkey();
        main.SetHotkeyName(settings.PauseHotkey ? platform.PauseHotkeyName : null);
        if (!settings.PauseHotkey || platform.PauseHotkeyName == null) return;
        if (!platform.RegisterPauseHotkey(() => Dispatcher.UIThread.Post(OnHotkey)))
            ToastWindow.Show(platform.PauseHotkeyName + " уже занята другой программой");
    }

    void OnHotkey()
    {
        if (engine.State == WorkState.Idle) return;
        TogglePause();
        if (!main.IsVisible) ToastWindow.Show(engine.State == WorkState.Paused ? "Пауза" : "Продолжаем работу");
    }

    void HandleCommand(string cmd)
    {
        if (cmd == "toggle-pause") OnHotkey();
        else ShowMain();
    }

    void ShowMain()
    {
        if (!main.IsVisible) main.Show();
        if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
        main.Activate();
    }

    // ---------- закрытие и выход ----------

    void OnMainClosing(object? sender, WindowClosingEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;

        // В авторежиме программа нужна и без начатого дня — иначе некому будет начать его вовремя.
        bool keepInTray = engine.State != WorkState.Idle || settings.AutoDay;
        if (keepInTray && (settings.CloseToTray || settings.AutoDay))
        {
            main.Hide();
            if (!trayHintShown)
            {
                ToastWindow.Show(engine.State != WorkState.Idle
                    ? "Работаю в трее. Рабочий день продолжается."
                    : "Работаю в трее: жду времени автоматического начала дня.");
                trayHintShown = true;
            }
            return;
        }
        Dispatcher.UIThread.Post(async () => await ExitWithConfirm());
    }

    async Task ExitWithConfirm()
    {
        if (engine.State != WorkState.Idle)
        {
            ShowMain();
            if (!await ConfirmDialog.Ask(main, "Рабочий день ещё идёт. Закончить его и выйти?")) return;
        }
        Exit();
    }

    void Exit(bool endDay = true)
    {
        exiting = true;
        timer.Stop();
        calendarTimer.Stop();
        updateTimer.Stop();
        practiceTimer.Stop();
        practiceOffer?.Close();
        cardsWindow?.Close();
        updateWindow?.Close();
        assistant?.Close();
        if (endDay) engine.EndDay(DateTime.Now);
        CloseReminderWindow();
        tray.IsVisible = false;
        tray.Dispose();
        platform.Dispose();
        main.Close();
        desktop.Shutdown();
    }
}
