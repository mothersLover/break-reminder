using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>Окно «Время отдохнуть!»: в углу, по центру или на весь экран.</summary>
public partial class ReminderWindow : Window
{
    static readonly Random Rnd = new();

    public event Action<ReminderResult>? Answered;
    /// <summary>Нажали «Начать перерыв» — вернуть длительность перерыва (движок отмечает начало перерыва).</summary>
    public event Func<TimeSpan>? BreakRequested;
    /// <summary>В перерыве нажали «🎧 Послушай и повтори».</summary>
    public event Action? ListenRequested;
    /// <summary>Показывать ли в перерыве «Послушай и повтори» (есть карточки и голос).</summary>
    public bool ListenAvailable { get; set; }

    readonly AppSettings s;
    readonly IPlatform platform;
    readonly Color accent;
    readonly bool isLong;
    readonly DispatcherTimer blinkTimer, soundTimer, animTimer, breakTimer;
    bool answered, phase;

    // перерыв с подсказками
    bool breakRunning, breakFinished;
    DateTime breakStart;
    TimeSpan breakLength;
    List<PlannedStep> plan = new();
    int animStep;
    const int AnimSteps = 14;
    PixelPoint animFrom, animTo;
    double targetOpacity = 1;

    // для дизайнера XAML
    public ReminderWindow() : this(new AppSettings(), TimeSpan.Zero, true, false, null!) { }

    public ReminderWindow(AppSettings settings, TimeSpan withoutBreak, bool preview, bool isLong, IPlatform platform,
        string? note = null, string? extraInfo = null)
    {
        InitializeComponent();
        s = settings;
        this.platform = platform;
        this.isLong = isLong;
        accent = isLong ? Palette.Purple : Palette.Amber;
        Icon = Icons.Clock(Palette.Amber);

        string title = isLong ? s.TitleLong : s.TitleShort;
        string[] tips = isLong ? s.TipsLong : s.TipsShort;
        if (tips.Length == 0) tips = isLong ? Texts.LongTips : Texts.Tips;

        Title = title;
        TitleText.Text = title;
        TipText.Text = tips[Rnd.Next(tips.Length)];
        InfoText.Text = preview
            ? "Это тестовое уведомление."
            : (isLong ? $"Перерыв {s.LongBreakMin} мин" : $"Перерыв {s.BreakMin} мин")
              + (withoutBreak.TotalMinutes >= 1 ? "  ·  без перерыва " + Format.Minutes(withoutBreak) : "");
        if (note != null || extraInfo != null)
        {
            // пояснение от календаря (или погода на большом перерыве) важнее обычной строки
            InfoText.Text = note ?? extraInfo;
            InfoText.Foreground = Palette.B(Palette.Text);
        }
        Frame.Background = Palette.B(accent);

        DoneButton.Content = s.GuidedBreak ? "Начать перерыв" : s.BtnDone;
        if (s.GuidedBreak) Buttons.ColumnDefinitions = new ColumnDefinitions("1.3*,10,*,10,*"); // длинная надпись — кнопка шире
        SnoozeButton.Content = s.BtnSnooze.Replace("{N}", s.SnoozeMin.ToString());
        SkipButton.Content = s.BtnSkip;
        DoneButton.Click += (_, _) =>
        {
            if (s.GuidedBreak && !breakRunning) StartBreak();
            else Answer(ReminderResult.Done);
        };
        SnoozeButton.Click += (_, _) => Answer(ReminderResult.Snooze);
        SkipButton.Click += (_, _) => Answer(ReminderResult.Skip);
        ListenButton.Click += (_, _) =>
        {
            ListenButton.IsVisible = false;
            Grid.SetColumnSpan(DoneButton, 5);
            ListenRequested?.Invoke();
        };

        if (s.Popup == PopupMode.Fullscreen)
        {
            SizeToContent = SizeToContent.Manual;
            WindowState = WindowState.FullScreen;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Root.Background = new SolidColorBrush(Color.FromArgb(235, 12, 12, 16));
        }
        else
        {
            // В углу и по центру окно не забирает фокус — чтобы не нажать кнопку случайно, пока печатаешь.
            ShowActivated = false;
            PopupDrag.Enable(this, "reminder-" + s.Popup);
        }
        Opacity = 0;

        blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        blinkTimer.Tick += (_, _) =>
        {
            phase = !phase;
            Frame.Background = Palette.B(phase ? Palette.Card : accent);
            TitleText.Foreground = Palette.B(phase ? accent : Palette.Text);
        };
        soundTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(10, s.RepeatSec)) };
        soundTimer.Tick += (_, _) => SoundCatalog.Play(platform, s);
        animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        animTimer.Tick += OnAnim;
        breakTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        breakTimer.Tick += (_, _) => UpdateBreak();

        Opened += OnOpened;
        KeyDown += OnKeyDown;
        Closing += OnClosing;
    }

    void OnOpened(object? sender, EventArgs e)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen != null && s.Popup != PopupMode.Fullscreen)
        {
            var wa = screen.WorkingArea;
            double k = screen.Scaling;
            int w = (int)(Bounds.Width * k), h = (int)(Bounds.Height * k), m = (int)(16 * k);
            string key = "reminder-" + s.Popup;
            if (PopupDrag.HasSaved(key))
            {
                // отодвигали — появиться там же, лёгким подъёмом
                animTo = PopupDrag.Place(this, key, default);
                animFrom = new PixelPoint(animTo.X, animTo.Y + (int)(30 * k));
            }
            else if (s.Popup == PopupMode.Center)
            {
                animTo = new PixelPoint(wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2);
                animFrom = new PixelPoint(animTo.X, animTo.Y + (int)(30 * k));
            }
            else
            {
                animTo = new PixelPoint(wa.Right - w - m, wa.Bottom - h - m);
                animFrom = new PixelPoint(animTo.X, wa.Bottom);
            }
            Position = animFrom;
            platform?.RoundCorners(this.Handle());
        }
        else
        {
            animFrom = animTo = Position;
            targetOpacity = 1;
        }

        animTimer.Start();
        if (s.Blink)
        {
            blinkTimer.Start();
            platform?.FlashWindow(this.Handle(), true);
        }
        if (s.SoundOn && platform != null)
        {
            SoundCatalog.Play(platform, s);
            if (s.RepeatSound) soundTimer.Start();
        }
    }

    void OnAnim(object? sender, EventArgs e)
    {
        animStep++;
        double t = Math.Min(1.0, (double)animStep / AnimSteps);
        double ease = 1 - Math.Pow(1 - t, 3);
        if (animFrom != animTo)
            Position = new PixelPoint(
                (int)(animFrom.X + (animTo.X - animFrom.X) * ease),
                (int)(animFrom.Y + (animTo.Y - animFrom.Y) * ease));
        Opacity = targetOpacity * ease;
        if (t >= 1) animTimer.Stop();
    }

    // ---------- перерыв с подсказками ----------

    void StartBreak()
    {
        breakLength = BreakRequested?.Invoke() ?? TimeSpan.FromMinutes(isLong ? s.LongBreakMin : s.BreakMin);
        breakRunning = true;
        breakStart = DateTime.Now;
        plan = BreakPlan.Build(breakLength, isLong, Rnd);

        // тишина и спокойный вид: мигание и повтор звука больше не нужны
        blinkTimer.Stop();
        soundTimer.Stop();
        platform?.StopSound();
        platform?.FlashWindow(this.Handle(), false);
        Frame.Background = Palette.B(Palette.Green);
        TitleText.Foreground = Palette.B(Palette.Text);
        TitleText.Text = isLong ? "Большой перерыв" : "Перерыв";
        TipText.Foreground = Palette.B(Palette.Text);
        InfoText.Foreground = Palette.B(Palette.Muted);
        CountdownText.IsVisible = BreakProgress.IsVisible = true;
        SnoozeButton.IsVisible = SkipButton.IsVisible = false;
        Grid.SetColumnSpan(DoneButton, ListenAvailable ? 1 : 5);
        ListenButton.IsVisible = ListenAvailable;
        DoneButton.Content = "Закончить раньше";
        DoneButton.Background = Palette.B(Palette.Gray);

        UpdateBreak();
        breakTimer.Start();
    }

    void UpdateBreak()
    {
        var elapsed = DateTime.Now - breakStart;
        if (elapsed >= breakLength)
        {
            FinishBreak();
            return;
        }
        CountdownText.Text = Format.Ms(breakLength - elapsed);
        BreakProgress.Value = elapsed / breakLength;
        var step = BreakPlan.Current(plan, elapsed);
        TipText.Text = step.Exercise.Text;
        InfoText.Text = plan.Count > 1 ? $"{step.Exercise.Title}  ·  {plan.IndexOf(step) + 1} из {plan.Count}" : step.Exercise.Title;
    }

    void FinishBreak()
    {
        breakTimer.Stop();
        breakFinished = true;
        TitleText.Text = "Перерыв окончен";
        CountdownText.IsVisible = false;
        BreakProgress.Value = 1;
        TipText.Text = "Можно возвращаться к работе. Хорошего продолжения!";
        InfoText.Text = "";
        DoneButton.Content = "Вернуться к работе";
        ListenButton.IsVisible = false;
        Grid.SetColumnSpan(DoneButton, 5);
        DoneButton.Background = Palette.B(Palette.Green);
        if (s.SoundOn && platform != null) platform.PlayWav(Synth.Melody("soft", s.Volume / 100.0));
        platform?.FlashWindow(this.Handle(), true);
    }

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !breakRunning) Answer(ReminderResult.Snooze);
        // Глотаем Enter/Пробел, чтобы набор текста не «нажал» кнопку.
        if (e.Key is Key.Escape or Key.Enter or Key.Space) e.Handled = true;
    }

    void StopAll()
    {
        blinkTimer.Stop();
        soundTimer.Stop();
        animTimer.Stop();
        breakTimer.Stop();
        if (!breakFinished) platform?.StopSound(); // мелодию конца перерыва даём доиграть
        platform?.FlashWindow(this.Handle(), false);
    }

    void Answer(ReminderResult r)
    {
        if (answered) return;
        answered = true;
        StopAll();
        Close();
        Answered?.Invoke(r);
    }

    /// <summary>Закрыть без ответа (пауза / конец дня).</summary>
    public void CloseSilently()
    {
        answered = true;
        StopAll();
        Close();
    }

    void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // Alt+F4 и т.п.: во время перерыва — перерыв сделан, иначе — «отложить».
        if (answered) return;
        answered = true;
        StopAll();
        Answered?.Invoke(breakRunning ? ReminderResult.Done : ReminderResult.Snooze);
    }
}
