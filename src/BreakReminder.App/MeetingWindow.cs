using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>«Через 5 мин встреча» — в правом верхнем углу, не забирает фокус, закрывается сама после начала встречи.</summary>
public sealed class MeetingWindow : Window
{
    static MeetingWindow? current;

    public static void Show(Meeting m, AppSettings s, IPlatform platform)
    {
        current?.Close();
        current = new MeetingWindow(m, s, platform);
        current.Show();
    }

    readonly IPlatform platform;

    MeetingWindow(Meeting m, AppSettings s, IPlatform platform)
    {
        this.platform = platform;
        Title = "Встреча: " + m.Title;
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

        int minutes = Math.Max(1, (int)Math.Round((m.Start - DateTime.Now).TotalMinutes));
        var ok = new Button { Content = "Понял", Classes = { "flat" }, Background = Palette.B(Palette.Blue), Height = 40, Margin = new Thickness(0, 12, 0, 0) };
        ok.Click += (_, _) => Close();

        Content = new Border
        {
            Padding = new Thickness(5),
            Background = Palette.B(Palette.Blue),
            Child = new Border
            {
                Background = Palette.B(Palette.Card),
                Width = 400,
                Padding = new Thickness(22, 14, 22, 18),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = $"Через {minutes} мин встреча", FontSize = 13, Foreground = Palette.B(Palette.Muted) },
                        new TextBlock
                        {
                            Text = m.Title, FontSize = 21, FontWeight = FontWeight.Bold, Foreground = Palette.B(Palette.Text),
                            TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock { Text = m.TimeRange, FontSize = 13, Foreground = Palette.B(Palette.Muted) },
                        ok
                    }
                }
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            if (e.Key is Key.Escape or Key.Enter or Key.Space) e.Handled = true;
        };

        Opened += (_, _) =>
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var wa = screen.WorkingArea;
                double k = screen.Scaling;
                Position = new PixelPoint(wa.Right - (int)((Bounds.Width + 16) * k), wa.Y + (int)(16 * k));
            }
            Opacity = 1;
            if (s.SoundOn) platform.PlayWav(Synth.Melody("soft", s.Volume / 100.0));
            // само закроется через пару минут после начала встречи
            var life = m.Start - DateTime.Now + TimeSpan.FromMinutes(2);
            DispatcherTimer.RunOnce(() => { if (IsVisible) Close(); }, life > TimeSpan.Zero ? life : TimeSpan.FromMinutes(1));
        };
        Closed += (_, _) => { if (current == this) current = null; };
    }
}
