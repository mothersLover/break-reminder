using Avalonia.Controls;
using Avalonia.Platform.Storage;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>Настройки. ShowDialog&lt;AppSettings?&gt;: новые настройки или null (отмена).</summary>
public partial class SettingsWindow : Window
{
    readonly AppSettings original;
    readonly IPlatform platform;
    string customPath;

    public bool AutostartChecked => AutostartCheck.IsChecked == true;

    public SettingsWindow() : this(new AppSettings(), null!, "") { }

    public SettingsWindow(AppSettings s, IPlatform platform, string calendarStatus)
    {
        InitializeComponent();
        original = s;
        this.platform = platform;
        customPath = s.CustomSoundPath;
        Icon = Icons.Clock(Palette.Green);

        // ---- Основные ----
        IntervalNum.Value = s.IntervalMin;
        BreakNum.Value = s.BreakMin;
        SnoozeNum.Value = s.SnoozeMin;
        IdleCheck.IsChecked = s.IdleDetect;
        LongCheck.IsChecked = s.LongBreaks;
        LongEveryNum.Value = s.LongEvery;
        LongMinNum.Value = s.LongBreakMin;
        DndCallsCheck.IsChecked = s.DndCalls;
        DndFullCheck.IsChecked = s.DndFullscreen;
        if (platform != null && !platform.SupportsFullscreenDetection)
        {
            DndFullHint.Text = platform.Name == "Linux"
                ? "Работает только в сеансе X11 — Wayland не даёт смотреть на чужие окна."
                : "На этой системе определить полноэкранную программу нельзя.";
            DndFullHint.IsVisible = true;
        }

        PopupCombo.ItemsSource = new[] { "Всплывающее окно в углу", "Окно по центру экрана", "Затемнение всего экрана" };
        PopupCombo.SelectedIndex = (int)s.Popup;
        BlinkCheck.IsChecked = s.Blink;

        SoundCheck.IsChecked = s.SoundOn;
        SoundCombo.ItemsSource = SoundCatalog.Names;
        SoundCombo.SelectedIndex = Math.Max(0, Array.IndexOf(SoundCatalog.Keys, s.Sound));
        VolumeSlider.Value = s.Volume;
        RepeatCheck.IsChecked = s.RepeatSound;
        RepeatNum.Value = s.RepeatSec;

        // ---- Рабочий день и запуск ----
        AutoDayCheck.IsChecked = s.AutoDay;
        StartTimePicker.SelectedTime = s.AutoStartTime;
        EndTimePicker.SelectedTime = s.AutoEndTime;
        AutostartCheck.IsChecked = platform?.IsAutostartEnabled() ?? false;
        TrayCheck.IsChecked = s.CloseToTray;
        HotkeyCheck.IsChecked = s.PauseHotkey;
        if (platform?.PauseHotkeyName is { } hk)
        {
            HotkeyCheck.Content = $"Пауза / продолжить по {hk} (из любой программы)";
            HotkeyHint.IsVisible = false;
        }
        else
        {
            HotkeyCheck.IsVisible = false;
            HotkeyHint.Text = "Глобальные горячие клавиши здесь недоступны. Назначь в настройках системы " +
                              "(Клавиатура → Свои сочетания клавиш) любое сочетание на команду:\n" +
                              $"\"{Environment.GetEnvironmentVariable("APPIMAGE") ?? Environment.ProcessPath}\" --toggle-pause";
        }

        // ---- Календарь ----
        CalendarCheck.IsChecked = s.CalendarOn;
        CalendarUrlBox.Text = s.CalendarUrl;
        CalendarStatusText.Text = calendarStatus;
        CalWaitCheck.IsChecked = s.CalWaitMeetings;
        CalBeforeCheck.IsChecked = s.CalBreakBefore;
        CalBeforeNum.Value = s.CalBreakBeforeMin;
        CalAlertCheck.IsChecked = s.CalMeetingAlert;
        CalAlertNum.Value = s.CalMeetingAlertMin;
        CalChainCheck.IsChecked = s.CalChainLong;
        CalChainNum.Value = s.CalChainMin;
        CalendarRevealCheck.IsCheckedChanged += (_, _) => CalendarUrlBox.RevealPassword = CalendarRevealCheck.IsChecked == true;
        CalendarCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        CalBeforeCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        CalAlertCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        CalChainCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        CalendarTestButton.Click += async (_, _) => await TestCalendar();

        // ---- Тексты ----
        FillTexts(s.TitleShort, s.TitleLong, s.TipsShort, s.TipsLong, s.BtnDone, s.BtnSnooze, s.BtnSkip);

        // ---- события ----
        SoundCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        RepeatCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        LongCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        SoundCombo.SelectionChanged += (_, _) => UpdateEnabled();
        AutoDayCheck.IsCheckedChanged += (_, _) =>
        {
            if (AutoDayCheck.IsChecked == true) AutostartCheck.IsChecked = true;
            UpdateEnabled();
        };

        PlayButton.Click += (_, _) => { if (platform != null) SoundCatalog.Play(platform, Collect()); };
        BrowseButton.Click += async (_, _) => await BrowseSound();
        TestButton.Click += (_, _) => Preview(false);
        PreviewShortButton.Click += (_, _) => Preview(false);
        PreviewLongButton.Click += (_, _) => Preview(true);
        ResetTextsButton.Click += (_, _) =>
            FillTexts(Texts.TitleShort, Texts.TitleLong, Texts.Tips, Texts.LongTips, Texts.BtnDone, Texts.BtnSnooze, Texts.BtnSkip);
        OpenFolderButton.Click += (_, _) =>
        {
            Directory.CreateDirectory(Paths.DataDir);
            platform?.OpenPath(Paths.DataDir);
        };
        SaveButton.Click += (_, _) => Close(Collect());
        CancelButton.Click += (_, _) => Close(null);
        Closed += (_, _) => platform?.StopSound();

        UpdateEnabled();
    }

    void FillTexts(string titleShort, string titleLong, string[] tipsShort, string[] tipsLong, string done, string snooze, string skip)
    {
        TitleShortBox.Text = titleShort;
        TitleLongBox.Text = titleLong;
        TipsShortBox.Text = string.Join(Environment.NewLine, tipsShort);
        TipsLongBox.Text = string.Join(Environment.NewLine, tipsLong);
        BtnDoneBox.Text = done;
        BtnSnoozeBox.Text = snooze;
        BtnSkipBox.Text = skip;
    }

    void Preview(bool isLong)
    {
        if (platform == null) return;
        new ReminderWindow(Collect(), TimeSpan.FromMinutes(52), true, isLong, platform).Show();
    }

    async Task BrowseSound()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Выберите мелодию",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(OperatingSystem.IsWindows() ? "Звук WAV" : "Звук")
                {
                    Patterns = OperatingSystem.IsWindows()
                        ? new[] { "*.wav" }                                     // Windows PlaySound умеет только WAV
                        : new[] { "*.wav", "*.mp3", "*.ogg", "*.oga", "*.aiff", "*.m4a", "*.flac" }
                }
            }
        });
        var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path == null) return;
        customPath = path;
        SoundCombo.SelectedIndex = Array.IndexOf(SoundCatalog.Keys, "custom");
        UpdateEnabled();
    }

    async Task TestCalendar()
    {
        var url = CalendarUrlBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            CalendarStatusText.Text = "Вставь ссылку на календарь.";
            return;
        }
        CalendarTestButton.IsEnabled = false;
        CalendarStatusText.Text = "Загружаю…";
        try
        {
            var now = DateTime.Now;
            var meetings = await CalendarFeed.FetchAsync(url, now);
            int today = meetings.Count(m => m.Start.Date == now.Date);
            int tomorrow = meetings.Count(m => m.Start.Date == now.Date.AddDays(1));
            var next = meetings.FirstOrDefault(m => m.End > now);
            CalendarStatusText.Text = $"Работает. Встреч сегодня: {today}, завтра: {tomorrow}."
                + (next != null ? $"\nБлижайшая: {next.Title}, {(next.Start.Date == now.Date ? "сегодня" : next.Start.ToString("d MMMM"))} в {next.Start:HH:mm}" : "");
        }
        catch (Exception e)
        {
            CalendarStatusText.Text = "Не получилось: " + e.Message;
        }
        finally
        {
            CalendarTestButton.IsEnabled = true;
        }
    }

    void UpdateEnabled()
    {
        bool cal = CalendarCheck.IsChecked == true;
        CalWaitCheck.IsEnabled = CalBeforeCheck.IsEnabled = CalAlertCheck.IsEnabled = CalChainCheck.IsEnabled = cal;
        CalBeforeNum.IsEnabled = cal && CalBeforeCheck.IsChecked == true;
        CalAlertNum.IsEnabled = cal && CalAlertCheck.IsChecked == true;
        CalChainNum.IsEnabled = cal && CalChainCheck.IsChecked == true;

        bool snd = SoundCheck.IsChecked == true;
        string key = SoundCatalog.Keys[Math.Max(0, SoundCombo.SelectedIndex)];
        SoundCombo.IsEnabled = PlayButton.IsEnabled = RepeatCheck.IsEnabled = snd;
        BrowseButton.IsEnabled = snd && key == "custom";
        VolumeSlider.IsEnabled = snd && SoundCatalog.IsBuiltIn(key);
        RepeatNum.IsEnabled = snd && RepeatCheck.IsChecked == true;
        LongEveryNum.IsEnabled = LongMinNum.IsEnabled = LongCheck.IsChecked == true;
        StartTimePicker.IsEnabled = EndTimePicker.IsEnabled = AutoDayCheck.IsChecked == true;
        CustomFileText.Text = string.IsNullOrEmpty(customPath) ? "файл не выбран" : Path.GetFileName(customPath);
    }

    static int N(NumericUpDown n, int def) => n.Value is decimal d ? (int)d : def;

    static string Or(string? v, string def) => string.IsNullOrWhiteSpace(v) ? def : v.Trim();

    static int Minutes(TimePicker t, int def) => t.SelectedTime is TimeSpan ts ? (int)ts.TotalMinutes % (24 * 60) : def;

    AppSettings Collect()
    {
        var s = original.Clone();
        s.IntervalMin = N(IntervalNum, s.IntervalMin);
        s.BreakMin = N(BreakNum, s.BreakMin);
        s.SnoozeMin = N(SnoozeNum, s.SnoozeMin);
        s.IdleDetect = IdleCheck.IsChecked == true;
        s.LongBreaks = LongCheck.IsChecked == true;
        s.LongEvery = N(LongEveryNum, s.LongEvery);
        s.LongBreakMin = N(LongMinNum, s.LongBreakMin);
        s.DndCalls = DndCallsCheck.IsChecked == true;
        s.DndFullscreen = DndFullCheck.IsChecked == true;
        s.Popup = (PopupMode)Math.Max(0, PopupCombo.SelectedIndex);
        s.Blink = BlinkCheck.IsChecked == true;
        s.SoundOn = SoundCheck.IsChecked == true;
        s.Sound = SoundCatalog.Keys[Math.Max(0, SoundCombo.SelectedIndex)];
        s.CustomSoundPath = customPath ?? "";
        s.Volume = (int)VolumeSlider.Value;
        s.RepeatSound = RepeatCheck.IsChecked == true;
        s.RepeatSec = N(RepeatNum, s.RepeatSec);

        s.AutoDay = AutoDayCheck.IsChecked == true;
        s.AutoStartMin = Minutes(StartTimePicker, s.AutoStartMin);
        s.AutoEndMin = Minutes(EndTimePicker, s.AutoEndMin);
        s.CloseToTray = TrayCheck.IsChecked == true;
        s.PauseHotkey = HotkeyCheck.IsChecked == true;

        s.CalendarOn = CalendarCheck.IsChecked == true;
        s.CalendarUrl = CalendarFeed.NormalizeUrl(CalendarUrlBox.Text ?? "");
        s.CalWaitMeetings = CalWaitCheck.IsChecked == true;
        s.CalBreakBefore = CalBeforeCheck.IsChecked == true;
        s.CalBreakBeforeMin = N(CalBeforeNum, s.CalBreakBeforeMin);
        s.CalMeetingAlert = CalAlertCheck.IsChecked == true;
        s.CalMeetingAlertMin = N(CalAlertNum, s.CalMeetingAlertMin);
        s.CalChainLong = CalChainCheck.IsChecked == true;
        s.CalChainMin = N(CalChainNum, s.CalChainMin);

        s.TitleShort = Or(TitleShortBox.Text, Texts.TitleShort);
        s.TitleLong = Or(TitleLongBox.Text, Texts.TitleLong);
        s.TipsShort = Texts.ParseLines(TipsShortBox.Text, Texts.Tips);
        s.TipsLong = Texts.ParseLines(TipsLongBox.Text, Texts.LongTips);
        s.BtnDone = Or(BtnDoneBox.Text, Texts.BtnDone);
        s.BtnSnooze = Or(BtnSnoozeBox.Text, Texts.BtnSnooze);
        s.BtnSkip = Or(BtnSkipBox.Text, Texts.BtnSkip);
        return s;
    }
}
