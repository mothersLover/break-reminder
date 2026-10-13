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
    readonly WorkDayEngine engine;
    readonly MainWindow main;
    readonly TrayIcon tray;
    readonly NativeMenuItem miPause, miMain;
    readonly DispatcherTimer timer, calendarTimer, updateTimer;
    AppSettings settings;
    ReminderWindow? reminder;
    UpdateWindow? updateWindow;
    bool exiting, trayHintShown, blinkPhase;
    string calendarStatus = "";
    int calendarRequest; // номер последней загрузки — чтобы старый ответ не перезаписал новый

    public AppController(IClassicDesktopStyleApplicationLifetime desktop, bool startHidden)
    {
        this.desktop = desktop;
        platform = PlatformFactory.Create();
        platform.Initialize();
        settings = AppSettings.Load();

        engine = new WorkDayEngine(settings, platform, history, DateTime.Now);
        engine.ReminderDue += ShowReminder;
        engine.ReminderCancelled += CloseReminderWindow;
        engine.Notification += ToastWindow.Show;
        engine.MeetingSoon += m => MeetingWindow.Show(m, settings, platform);

        main = new MainWindow { Icon = Icons.Clock(Palette.Green) };
        main.MainClicked += OnMainClicked;
        main.PauseClicked += TogglePause;
        main.StatsClicked += OpenStats;
        main.SettingsClicked += OpenSettings;
        main.Closing += OnMainClosing;

        // ---- трей ----
        var menu = new NativeMenu();
        var miShow = new NativeMenuItem("Показать окно");
        miShow.Click += (_, _) => ShowMain();
        miPause = new NativeMenuItem("Пауза");
        miPause.Click += (_, _) => TogglePause();
        miMain = new NativeMenuItem("Начать день");
        miMain.Click += (_, _) => OnMainClicked();
        var miExit = new NativeMenuItem("Выход");
        miExit.Click += async (_, _) => await ExitWithConfirm();
        menu.Items.Add(miShow);
        menu.Items.Add(miPause);
        menu.Items.Add(miMain);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(miExit);
        tray = new TrayIcon { Icon = Icons.Clock(Palette.Gray), ToolTipText = "Отдыхалка", Menu = menu, IsVisible = true };
        tray.Clicked += (_, _) => ShowMain();
        TrayIcon.SetIcons(Application.Current!, new TrayIcons { tray });

        ApplyHotkey();

        // Выход из системы / Cmd+Q — сохранить день в историю.
        desktop.ShutdownRequested += (_, _) => { if (!exiting) engine.EndDay(DateTime.Now); };

        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) =>
        {
            engine.Tick(DateTime.Now);
            Render();
        };
        timer.Start();

        calendarTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        calendarTimer.Tick += (_, _) => RefreshCalendar();
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

        bool idle = engine.State == WorkState.Idle, paused = engine.State == WorkState.Paused;
        tray.Icon = Icons.Clock(idle ? Palette.Gray : paused ? Palette.Amber : engine.ReminderOpen ? Palette.Red : Palette.Green);
        string hint = engine.AutoStartHint(now);
        tray.ToolTipText = idle ? "Отдыхалка — " + (hint.Length > 0 ? hint.ToLowerInvariant() : "день не начат")
            : paused ? "Пауза · работа " + Format.Hms(engine.Worked)
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
        reminder = new ReminderWindow(settings, engine.SinceLastBreak, false, isLong, platform, engine.ReminderNote);
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
        var w = new SettingsWindow(settings, platform, calendarStatus);
        var result = await w.ShowDialog<AppSettings?>(main);
        if (result == null) return;
        bool calendarChanged = result.CalendarOn != settings.CalendarOn || result.CalendarUrl != settings.CalendarUrl;
        settings = result;
        settings.Save();
        engine.Settings = settings;
        platform.SetAutostart(w.AutostartChecked);
        ApplyHotkey();
        if (calendarChanged) RefreshCalendar();
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
        }
        catch (Exception e)
        {
            if (request != calendarRequest) return;
            calendarStatus = $"Не удалось обновить ({DateTime.Now:HH:mm}): {e.Message}";
        }
        Render();
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
        updateWindow?.Close();
        if (endDay) engine.EndDay(DateTime.Now);
        CloseReminderWindow();
        tray.IsVisible = false;
        tray.Dispose();
        platform.Dispose();
        main.Close();
        desktop.Shutdown();
    }
}
