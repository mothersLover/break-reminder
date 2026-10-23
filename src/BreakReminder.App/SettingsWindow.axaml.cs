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

    /// <summary>Нажали «Установить…» — после закрытия настроек показать окно обновления.</summary>
    public ReleaseInfo? InstallRequested { get; private set; }

    ReleaseInfo? foundRelease;
    string skipVersion;

    // погода: выбранный населённый пункт
    readonly IReadOnlyList<Meeting> meetings;
    List<Place> places = new();
    string placeName;
    double placeLat, placeLon;

    readonly TutorMemory? memory;

    public SettingsWindow() : this(new AppSettings(), null!, "") { }

    public SettingsWindow(AppSettings s, IPlatform platform, string calendarStatus, IReadOnlyList<Meeting>? meetings = null,
        TutorMemory? memory = null)
    {
        this.memory = memory;
        this.meetings = meetings ?? Array.Empty<Meeting>();
        placeName = s.WeatherCity;
        placeLat = s.WeatherLat;
        placeLon = s.WeatherLon;
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

        // ---- Перерыв и глаза ----
        GuidedCheck.IsChecked = s.GuidedBreak;
        EyeCheck.IsChecked = s.EyeRest;
        EyeEveryNum.Value = s.EyeEveryMin;
        EyeSecondsNum.Value = s.EyeSeconds;
        EyeCheck.IsCheckedChanged += (_, _) => UpdateEnabled();

        // ---- Утро и вечер ----
        GreetingCheck.IsChecked = s.Greeting;
        UserNameBox.Text = s.UserName;
        ReportCheck.IsChecked = s.DayReport;
        TaskAlertCheck.IsChecked = s.TaskAlerts;
        TaskAlertNum.Value = s.TaskAlertMin;
        TaskAlertCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        WeatherCheck.IsChecked = s.Weather;
        CityBox.Text = s.WeatherCity;
        CityStatusText.Text = s.HasWeatherPlace ? "Выбрано: " + s.WeatherCity : "";
        WeatherCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        CitySearchButton.Click += async (_, _) => await SearchCity();
        CityBox.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; await SearchCity(); } };
        CityBox.TextChanged += (_, _) =>
        {
            // город поменяли руками — прежние координаты больше не подходят
            if ((CityBox.Text ?? "").Trim() != placeName) { placeLat = placeLon = double.NaN; CityStatusText.Text = "Нажми «Найти», чтобы выбрать город."; }
        };
        CityResultsCombo.SelectionChanged += (_, _) =>
        {
            if (CityResultsCombo.SelectedIndex < 0 || CityResultsCombo.SelectedIndex >= places.Count) return;
            var p = places[CityResultsCombo.SelectedIndex];
            placeName = p.Name;
            placeLat = p.Lat;
            placeLon = p.Lon;
            CityBox.Text = p.Name;
            CityStatusText.Text = "Выбрано: " + p.Display;
        };
        PreviewGreetingButton.Click += async (_, _) => await PreviewGreeting();
        PreviewReportButton.Click += (_, _) =>
        {
            var now = DateTime.Now;
            var sample = new DayReport(now.Date.AddHours(9).AddMinutes(58), now, TimeSpan.FromMinutes(462), 6, 1, 2, 14, 4, TimeSpan.FromMinutes(150), false);
            var w = new DayReportWindow(sample, new Dictionary<DateTime, DayStat>(), Collect()) { Title = "Итоги дня (пример)" };
            w.Show(this);
        };

        // ---- Обновления ----
        skipVersion = s.UpdateSkipVersion;
        CurrentVersionText.Text = $"Установлена версия {UpdateChecker.Current}";
        UpdateOnStartCheck.IsChecked = s.UpdateOnStart;
        UpdateDailyCheck.IsChecked = s.UpdateDaily;
        UpdateHowText.Text = Updater.CanSelfUpdate
            ? "Если есть новая версия, программа предложит её установить: скачает, заменит себя и перезапустится. Идущий рабочий день не прервётся."
            : "Если есть новая версия, программа предложит её скачать и откроет страницу загрузки — замени приложение новым.";
        ShowSkipped();
        UnskipButton.Click += (_, _) => { skipVersion = ""; ShowSkipped(); };
        CheckUpdateButton.Click += async (_, _) => await CheckUpdate();
        InstallUpdateButton.Click += (_, _) =>
        {
            InstallRequested = foundRelease;
            Close(Collect());
        };
        OpenReleasesButton.Click += (_, _) => platform?.OpenPath($"https://github.com/{UpdateChecker.Repo}/releases");

        // ---- ИИ-помощник ----
        AiCheck.IsChecked = s.AiOn;
        AiProviderCombo.ItemsSource = AiClients.ProviderNames;
        AiProviderCombo.SelectedIndex = (int)s.AiProvider;
        AiClaudeModelBox.Text = s.AiClaudeModel;
        AiClaudePathBox.Text = s.AiClaudePath;
        AiAnthropicKeyBox.Text = s.AiAnthropicKey;
        AiAnthropicModelBox.Text = s.AiAnthropicModel;
        AiOpenAiUrlBox.Text = s.AiOpenAiUrl;
        AiOpenAiKeyBox.Text = s.AiOpenAiKey;
        AiOpenAiModelBox.Text = s.AiOpenAiModel;
        AiOllamaUrlBox.Text = s.AiOllamaUrl;
        AiOllamaModelBox.Text = s.AiOllamaModel;
        AiHintsCheck.IsChecked = s.AiHintsFirst;
        AiWeatherGateCheck.IsChecked = s.AiRequireWeather;
        PracticeCheck.IsChecked = s.PracticeOn;
        PracticeMorningCheck.IsChecked = s.PracticeMorning;
        PracticeMorningNum.Value = s.PracticeMorningMin;
        PracticeDayCountNum.Value = s.PracticeDayCount;
        PracticeDayMinNum.Value = s.PracticeDayMin;
        PracticeCardsCheck.IsChecked = s.PracticeCards;
        PracticeEveningCheck.IsChecked = s.PracticeEvening;
        PracticeEveningPicker.SelectedTime = TimeSpan.FromMinutes(s.PracticeEveningAt);
        PracticeEveningNum.Value = s.PracticeEveningMin;
        PracticeCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        PracticeMorningCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        PracticeEveningCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        PracticeDayCountNum.ValueChanged += (_, _) => UpdateEnabled();
        AiCheck.IsCheckedChanged += (_, _) => UpdateEnabled();
        AiProviderCombo.SelectionChanged += (_, _) => { AiStatusText.Text = ""; UpdateEnabled(); };
        AiClaudePathBox.TextChanged += (_, _) => ShowClaudeFound();
        ShowClaudeFound();
        AiTestButton.Click += async (_, _) => await TestAi();
        AiMemoryButton.IsEnabled = memory != null;
        AiMemoryButton.Click += async (_, _) => { if (memory != null) await new MemoryWindow(memory).ShowDialog(this); };

        // ---- озвучка ----
        SpeechLangCombo.ItemsSource = SpeechText.Languages.Select(l => l.Name).ToList();
        SpeechLangCombo.SelectedIndex = Math.Max(0, Array.FindIndex(SpeechText.Languages, l => l.Code == s.SpeechLang));
        SpeechAutoCheck.IsChecked = s.SpeechAutoCards;
        ListenBreakCheck.IsChecked = s.ListenInBreak;
        ListenCountNum.Value = s.ListenCount;
        SpeechSlowCheck.IsChecked = s.SpeechSlow;
        SpeechLangCombo.SelectionChanged += (_, _) => ShowVoice();
        SpeechTestButton.Click += async (_, _) =>
        {
            if (platform == null) return;
            string lang = SpeechLang();
            if (!await platform.SpeakAsync(SpeechText.Sample(lang), lang, false))
                SpeechStatusText.Text = SpeechText.InstallHint(platform.Name, lang);
        };
        ListenNowButton.Click += (_, _) => ListenRequested?.Invoke(Collect());
        ShowVoice();

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

    async Task SearchCity()
    {
        var q = (CityBox.Text ?? "").Trim();
        if (q.Length < 2) { CityStatusText.Text = "Введи название населённого пункта."; return; }
        CitySearchButton.IsEnabled = false;
        CityStatusText.Text = "Ищу…";
        try
        {
            places = await Weather.SearchAsync(q);
            if (places.Count == 0)
            {
                CityResultsCombo.IsVisible = false;
                CityStatusText.Text = "Ничего не нашлось — попробуй написать иначе.";
                return;
            }
            CityResultsCombo.ItemsSource = places.Select(p => p.Display).ToList();
            CityResultsCombo.IsVisible = places.Count > 1;
            CityResultsCombo.SelectedIndex = 0; // выберет и заполнит координаты
            if (places.Count > 1) CityStatusText.Text = "Выбрано: " + places[0].Display + ". Если не тот — выбери в списке.";
        }
        catch (Exception e)
        {
            CityStatusText.Text = "Не получилось: " + e.Message;
        }
        finally
        {
            CitySearchButton.IsEnabled = true;
        }
    }

    async Task PreviewGreeting()
    {
        var s = Collect();
        var w = new GreetingWindow(s, meetings, DayPlan.Load(DateTime.Today), "Так будет начинаться каждый рабочий день.") { Title = "Приветствие (пример)" };
        w.Show(this);
        if (s.Weather)
        {
            var (weather, error) = await WeatherLoader.LoadAsync(s);
            w.SetWeather(weather, error);
        }
    }

    void ShowSkipped()
    {
        bool has = !string.IsNullOrEmpty(skipVersion);
        SkippedVersionText.Text = has ? $"Версию {skipVersion} ты пропустил — о ней не напоминаю." : "";
        SkippedVersionText.IsVisible = UnskipButton.IsVisible = has;
    }

    async Task CheckUpdate()
    {
        CheckUpdateButton.IsEnabled = false;
        InstallUpdateButton.IsVisible = false;
        UpdateStatusText.Text = "Проверяю…";
        try
        {
            var r = await UpdateChecker.GetLatestAsync();
            if (r == null || r.Version <= UpdateChecker.Current)
            {
                UpdateStatusText.Text = $"У тебя последняя версия ({UpdateChecker.Current}).";
            }
            else
            {
                foundRelease = r;
                UpdateStatusText.Text = $"Доступна версия {r.Version}.";
                InstallUpdateButton.IsVisible = true;
            }
        }
        catch (Exception e)
        {
            UpdateStatusText.Text = "Не получилось проверить: " + e.Message;
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
        }
    }

    /// <summary>«Послушать сейчас» — открыть «Послушай и повтори» с текущими (ещё не сохранёнными) настройками.</summary>
    public event Action<AppSettings>? ListenRequested;

    string SpeechLang() => SpeechText.Languages[Math.Max(0, SpeechLangCombo.SelectedIndex)].Code;

    void ShowVoice()
    {
        if (platform == null) return;
        string lang = SpeechLang();
        var voice = platform.VoiceFor(lang);
        SpeechStatusText.Text = voice != null ? "Голос: " + voice : SpeechText.InstallHint(platform.Name, lang);
        SpeechTestButton.IsEnabled = ListenNowButton.IsEnabled = voice != null;
    }

    void ShowClaudeFound()
    {
        var custom = (AiClaudePathBox.Text ?? "").Trim();
        var found = ClaudeCodeClient.Locate(custom);
        AiClaudeFoundText.Text = found != null
            ? (custom.Length > 0 ? "Файл найден." : "Найден: " + found)
            : (custom.Length > 0 ? "По этому пути файла нет." : "Claude Code не найден — установи его или укажи путь.");
    }

    async Task TestAi()
    {
        AiTestButton.IsEnabled = false;
        AiStatusText.Text = "Спрашиваю…";
        var started = DateTime.Now;
        try
        {
            var client = AiClients.Create(Collect());
            var reply = await client.CompleteAsync("Это проверка подключения. Отвечай очень коротко.",
                new[] { new ChatMessage(true, "Скажи по-французски «подключение работает» — одной фразой.", DateTime.Now) });
            AiStatusText.Text = $"Работает ({(DateTime.Now - started).TotalSeconds:0} с). Ответ: {reply}";
        }
        catch (Exception e)
        {
            AiStatusText.Text = "Не получилось: " + e.Message;
        }
        finally
        {
            AiTestButton.IsEnabled = true;
        }
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
        var provider = (AiProvider)Math.Max(0, AiProviderCombo.SelectedIndex);
        ClaudeCodePanel.IsVisible = provider == AiProvider.ClaudeCode;
        AnthropicPanel.IsVisible = provider == AiProvider.Anthropic;
        OpenAiPanel.IsVisible = provider == AiProvider.OpenAi;
        OllamaPanel.IsVisible = provider == AiProvider.Ollama;
        bool practice = PracticeCheck.IsChecked == true;
        PracticeMorningCheck.IsEnabled = PracticeEveningCheck.IsEnabled = PracticeDayCountNum.IsEnabled = practice;
        PracticeMorningNum.IsEnabled = practice && PracticeMorningCheck.IsChecked == true;
        PracticeDayMinNum.IsEnabled = PracticeCardsCheck.IsEnabled = practice && N(PracticeDayCountNum, 0) > 0;
        PracticeEveningPicker.IsEnabled = PracticeEveningNum.IsEnabled = practice && PracticeEveningCheck.IsChecked == true;

        EyeEveryNum.IsEnabled = EyeSecondsNum.IsEnabled = EyeCheck.IsChecked == true;
        TaskAlertNum.IsEnabled = TaskAlertCheck.IsChecked == true;
        CityBox.IsEnabled = CitySearchButton.IsEnabled = CityResultsCombo.IsEnabled = WeatherCheck.IsChecked == true;
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

        s.GuidedBreak = GuidedCheck.IsChecked == true;
        s.EyeRest = EyeCheck.IsChecked == true;
        s.EyeEveryMin = N(EyeEveryNum, s.EyeEveryMin);
        s.EyeSeconds = N(EyeSecondsNum, s.EyeSeconds);
        s.Greeting = GreetingCheck.IsChecked == true;
        s.UserName = (UserNameBox.Text ?? "").Trim();
        s.DayReport = ReportCheck.IsChecked == true;
        s.TaskAlerts = TaskAlertCheck.IsChecked == true;
        s.TaskAlertMin = N(TaskAlertNum, s.TaskAlertMin);
        s.Weather = WeatherCheck.IsChecked == true;
        s.WeatherCity = (CityBox.Text ?? "").Trim();
        s.WeatherLat = placeLat;
        s.WeatherLon = placeLon;

        s.UpdateOnStart = UpdateOnStartCheck.IsChecked == true;
        s.UpdateDaily = UpdateDailyCheck.IsChecked == true;
        s.UpdateSkipVersion = skipVersion;

        s.AiOn = AiCheck.IsChecked == true;
        s.AiProvider = (AiProvider)Math.Max(0, AiProviderCombo.SelectedIndex);
        s.AiClaudeModel = (AiClaudeModelBox.Text ?? "").Trim();
        s.AiClaudePath = (AiClaudePathBox.Text ?? "").Trim().Trim('"');
        s.AiAnthropicKey = (AiAnthropicKeyBox.Text ?? "").Trim();
        s.AiAnthropicModel = Or(AiAnthropicModelBox.Text, AiClients.DefaultAnthropicModel);
        s.AiOpenAiUrl = Or(AiOpenAiUrlBox.Text, AiClients.DefaultOpenAiUrl);
        s.AiOpenAiKey = (AiOpenAiKeyBox.Text ?? "").Trim();
        s.AiOpenAiModel = (AiOpenAiModelBox.Text ?? "").Trim();
        s.AiOllamaUrl = Or(AiOllamaUrlBox.Text, AiClients.DefaultOllamaUrl);
        s.AiOllamaModel = (AiOllamaModelBox.Text ?? "").Trim();
        s.AiHintsFirst = AiHintsCheck.IsChecked == true;
        s.AiRequireWeather = AiWeatherGateCheck.IsChecked == true;
        s.SpeechLang = SpeechLang();
        s.SpeechAutoCards = SpeechAutoCheck.IsChecked == true;
        s.ListenInBreak = ListenBreakCheck.IsChecked == true;
        s.ListenCount = N(ListenCountNum, s.ListenCount);
        s.SpeechSlow = SpeechSlowCheck.IsChecked == true;
        s.PracticeOn = PracticeCheck.IsChecked == true;
        s.PracticeMorning = PracticeMorningCheck.IsChecked == true;
        s.PracticeMorningMin = N(PracticeMorningNum, s.PracticeMorningMin);
        s.PracticeDayCount = N(PracticeDayCountNum, s.PracticeDayCount);
        s.PracticeDayMin = N(PracticeDayMinNum, s.PracticeDayMin);
        s.PracticeCards = PracticeCardsCheck.IsChecked == true;
        s.PracticeEvening = PracticeEveningCheck.IsChecked == true;
        s.PracticeEveningAt = Minutes(PracticeEveningPicker, s.PracticeEveningAt);
        s.PracticeEveningMin = N(PracticeEveningNum, s.PracticeEveningMin);

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
