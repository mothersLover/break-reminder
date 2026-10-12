using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace BreakReminder.App;

/// <summary>Короткое сообщение в углу экрана, исчезает само (замена всплывашек трея).</summary>
sealed class ToastWindow : Window
{
    static ToastWindow? current;

    public static void Show(string text)
    {
        current?.Close();
        current = new ToastWindow(text);
        current.Show();
    }

    ToastWindow(string text)
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Palette.B(Palette.Card);
        Opacity = 0;
        Content = new Border
        {
            BorderBrush = Palette.B(Palette.Gray),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 12),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = "Отдыхалка", FontSize = 12, Foreground = Palette.B(Palette.Muted) },
                    new TextBlock { Text = text, FontSize = 14, Foreground = Palette.B(Palette.Text), MaxWidth = 360, TextWrapping = TextWrapping.Wrap }
                }
            }
        };

        Opened += (_, _) =>
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var wa = screen.WorkingArea;
                double k = screen.Scaling;
                Position = new PixelPoint(wa.Right - (int)((Bounds.Width + 16) * k), wa.Bottom - (int)((Bounds.Height + 16) * k));
            }
            Opacity = 1;
            DispatcherTimer.RunOnce(() => { if (current == this) current = null; Close(); }, TimeSpan.FromSeconds(4));
        };
    }
}

/// <summary>Вопрос «Да / Нет».</summary>
sealed class ConfirmDialog : Window
{
    public static Task<bool> Ask(Window owner, string question) => new ConfirmDialog(question, false).ShowDialog<bool>(owner);

    public static Task Info(Window owner, string text) => new ConfirmDialog(text, true).ShowDialog<bool>(owner);

    ConfirmDialog(string question, bool okOnly)
    {
        Title = "Отдыхалка";
        Icon = Icons.Clock(Palette.Green);
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var yes = new Button { Content = okOnly ? "OK" : "Да", IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, Classes = { "accent" } };
        var no = new Button { Content = "Нет", IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center, IsVisible = !okOnly };
        yes.Click += (_, _) => Close(true);
        no.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20),
            Spacing = 20,
            Children =
            {
                new TextBlock { Text = question, FontSize = 15, MaxWidth = 380, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { yes, no }
                }
            }
        };
    }
}
