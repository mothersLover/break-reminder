using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

public enum PracticeChoice { Start, Later, Skip }

/// <summary>«Пора позаниматься» в углу экрана: «Начать» / «Позже» (через 30 мин) / «Не сегодня». Без ответа 3 минуты — как «позже».</summary>
public sealed class PracticeOfferWindow : Window
{
    bool answered;

    public event Action<PracticeChoice>? Answered;

    /// <param name="cardsDue">Не null — вместо занятия с помощником повторение карточек (столько к повторению).</param>
    public PracticeOfferWindow(PracticeKind kind, AppSettings s, IPlatform platform, int streak, int? cardsDue = null)
    {
        string title = cardsDue != null ? "Повторение карточек" : PracticeText.Title(kind);
        Title = title;
        Icon = Icons.Clock(Palette.Blue);
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Palette.B(Palette.Blue);
        Opacity = 0;
        PopupDrag.Enable(this, "practice");

        int minutes = PracticeText.Minutes(kind, s);
        string hint = cardsDue is { } due ? $"{GreetingWindow.Plural(due, "карточка ждёт", "карточки ждут", "карточек ждут")} повторения — быстро, без ожидания ответов."
            : kind switch
        {
            PracticeKind.Morning => "Пара лёгких заданий на повторение — и за работу.",
            PracticeKind.Day => "Несколько быстрых заданий на то, что путается.",
            _ => "Новое по текущей теме и повторение ошибок."
        };

        var start = new Button { Content = "Начать", Classes = { "flat" }, Background = Palette.B(Palette.Green) };
        var later = new Button { Content = "Позже", Classes = { "flat" }, Background = Palette.B(Palette.Gray) };
        ToolTip.SetTip(later, $"Напомню через {(int)PracticeCoach.SnoozeTime.TotalMinutes} минут");
        var skip = new Button { Content = "Не сегодня", Classes = { "flat" }, Background = Palette.B(Palette.Gray) };
        start.Click += (_, _) => Answer(PracticeChoice.Start);
        later.Click += (_, _) => Answer(PracticeChoice.Later);
        skip.Click += (_, _) => Answer(PracticeChoice.Skip);
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("4*,8,3*,8,3*"), Height = 40, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(start);
        Grid.SetColumn(later, 2);
        buttons.Children.Add(later);
        Grid.SetColumn(skip, 4);
        buttons.Children.Add(skip);

        var body = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Card.Text($"{minutes} мин · " + (cardsDue != null ? "карточки" : "помощник") + (streak > 0 ? $" · серия {streak} дн." : ""), 13, Palette.Muted),
                Card.Text(title, 21, Palette.Text, FontWeight.Bold),
                Card.Text(hint, 13.5, Palette.Text),
                buttons
            }
        };

        Content = new Border
        {
            Padding = new Thickness(5),
            Background = Palette.B(Palette.Blue),
            Child = new Border { Background = Palette.B(Palette.Card), Width = 420, Padding = new Thickness(22, 14, 22, 18), Child = body }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Answer(PracticeChoice.Later);
            if (e.Key is Key.Escape or Key.Enter or Key.Space) e.Handled = true;
        };
        Opened += (_, _) =>
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var wa = screen.WorkingArea;
                double k = screen.Scaling;
                int y = wa.Y + (int)(16 * k) + (MeetingWindow.IsOpen ? (int)(210 * k) : 0);
                Position = PopupDrag.Place(this, "practice", new PixelPoint(wa.Right - (int)((Bounds.Width + 16) * k), y));
            }
            Opacity = 1;
            if (s.SoundOn) platform.PlayWav(Synth.Melody("soft", s.Volume / 100.0));
            DispatcherTimer.RunOnce(() => Answer(PracticeChoice.Later), TimeSpan.FromMinutes(3));
        };
        Closed += (_, _) => Answer(PracticeChoice.Later);
    }

    void Answer(PracticeChoice c)
    {
        if (answered) return;
        answered = true;
        Answered?.Invoke(c);
        Close();
    }
}
