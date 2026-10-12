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

    readonly AppSettings s;
    readonly IPlatform platform;
    readonly Color accent;
    readonly DispatcherTimer blinkTimer, soundTimer, animTimer;
    bool answered, phase;
    int animStep;
    const int AnimSteps = 14;
    PixelPoint animFrom, animTo;
    double targetOpacity = 1;

    // для дизайнера XAML
    public ReminderWindow() : this(new AppSettings(), TimeSpan.Zero, true, false, null!) { }

    public ReminderWindow(AppSettings settings, TimeSpan withoutBreak, bool preview, bool isLong, IPlatform platform, string? note = null)
    {
        InitializeComponent();
        s = settings;
        this.platform = platform;
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
        if (note != null)
        {
            // пояснение от календаря важнее обычной строки
            InfoText.Text = note;
            InfoText.Foreground = Palette.B(Palette.Text);
        }
        Frame.Background = Palette.B(accent);

        DoneButton.Content = s.BtnDone;
        SnoozeButton.Content = s.BtnSnooze.Replace("{N}", s.SnoozeMin.ToString());
        SkipButton.Content = s.BtnSkip;
        DoneButton.Click += (_, _) => Answer(ReminderResult.Done);
        SnoozeButton.Click += (_, _) => Answer(ReminderResult.Snooze);
        SkipButton.Click += (_, _) => Answer(ReminderResult.Skip);

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
            if (s.Popup == PopupMode.Center)
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

    void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Answer(ReminderResult.Snooze);
        // Глотаем Enter/Пробел, чтобы набор текста не «нажал» кнопку.
        if (e.Key is Key.Escape or Key.Enter or Key.Space) e.Handled = true;
    }

    void StopAll()
    {
        blinkTimer.Stop();
        soundTimer.Stop();
        animTimer.Stop();
        platform?.StopSound();
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
        // Alt+F4 и т.п. — считаем как «отложить».
        if (answered) return;
        answered = true;
        StopAll();
        Answered?.Invoke(ReminderResult.Snooze);
    }
}
