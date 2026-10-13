using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>«Доступна новая версия»: что нового + Обновить / Позже / Пропустить эту версию.</summary>
public sealed class UpdateWindow : Window
{
    /// <summary>Новая версия скачана и подготовлена (путь к файлу) — пора перезапускаться.</summary>
    public event Action<string>? Ready;
    /// <summary>«Пропустить эту версию».</summary>
    public event Action? SkipRequested;

    readonly ReleaseInfo release;
    readonly IPlatform platform;
    readonly Button updateButton, laterButton, skipButton;
    readonly ProgressBar progress;
    readonly TextBlock status;
    CancellationTokenSource? cts;
    bool failed;

    public UpdateWindow(ReleaseInfo release, Version current, bool dayRunning, IPlatform platform)
    {
        this.release = release;
        this.platform = platform;
        Title = "Обновление";
        Icon = Icons.Clock(Palette.Green);
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.B(Palette.Bg);

        bool self = Updater.CanSelfUpdate;
        updateButton = new Button { Content = self ? "Обновить" : "Скачать", Classes = { "flat" }, Background = Palette.B(Palette.Green), Width = 140, Height = 38 };
        laterButton = new Button { Content = "Позже", Classes = { "flat" }, Background = Palette.B(Palette.Gray), Width = 110, Height = 38 };
        skipButton = new Button { Content = "Пропустить эту версию", Background = Brushes.Transparent, Foreground = Palette.B(Palette.Muted), Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 6, IsVisible = false, Foreground = Palette.B(Palette.Green) };
        status = new TextBlock { FontSize = 12.5, Foreground = Palette.B(Palette.Muted), TextWrapping = TextWrapping.Wrap, IsVisible = false };

        updateButton.Click += async (_, _) => await OnUpdate();
        laterButton.Click += (_, _) => { cts?.Cancel(); Close(); };
        skipButton.Click += (_, _) => { SkipRequested?.Invoke(); Close(); };
        Closed += (_, _) => cts?.Cancel();

        string hint = !self
            ? "Откроется страница загрузки — скачай архив для своего Mac и замени приложение."
            : dayRunning
                ? "Программа перезапустится, рабочий день продолжится с того же места."
                : "Программа перезапустится сама.";

        Content = new StackPanel
        {
            Margin = new Thickness(24, 20, 24, 20),
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Доступна новая версия", FontSize = 22, FontWeight = FontWeight.Bold, Foreground = Palette.B(Palette.Text) },
                new TextBlock { Text = $"{release.Version}  ·  у тебя {current}", FontSize = 13.5, Foreground = Palette.B(Palette.Muted) },
                new TextBlock { Text = "Что нового", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Palette.B(Palette.Text), Margin = new Thickness(0, 10, 0, 0) },
                new Border
                {
                    Background = Palette.B(Palette.Card), CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 10),
                    Child = new ScrollViewer
                    {
                        MaxHeight = 260,
                        Content = new SelectableTextBlock
                        {
                            Text = string.IsNullOrWhiteSpace(release.Notes) ? "Описание изменений — на странице релиза." : release.Notes,
                            TextWrapping = TextWrapping.Wrap, FontSize = 13.5, Foreground = Palette.B(Palette.Text)
                        }
                    }
                },
                new TextBlock { Text = hint, FontSize = 12.5, Foreground = Palette.B(Palette.Muted), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) },
                progress,
                status,
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
                    Margin = new Thickness(0, 12, 0, 0),
                    Children = { skipButton, Col(updateButton, 1, new Thickness(0, 0, 8, 0)), Col(laterButton, 2) }
                }
            }
        };
    }

    static Control Col(Control c, int col, Thickness? margin = null)
    {
        Grid.SetColumn(c, col);
        if (margin is { } m) c.Margin = m;
        return c;
    }

    async Task OnUpdate()
    {
        if (!Updater.CanSelfUpdate || failed)
        {
            platform.OpenPath(release.PageUrl);
            Close();
            return;
        }

        updateButton.IsEnabled = skipButton.IsEnabled = false;
        progress.IsVisible = status.IsVisible = true;
        status.Foreground = Palette.B(Palette.Muted);
        status.Text = "Скачиваю…";
        cts = new CancellationTokenSource();
        try
        {
            var staged = await Updater.DownloadAsync(release, new Progress<double>(p =>
            {
                progress.Value = p;
                status.Text = $"Скачиваю… {p:P0}";
            }), cts.Token);
            status.Text = "Устанавливаю и перезапускаю…";
            Ready?.Invoke(staged);
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            progress.IsVisible = false;
            status.Foreground = Palette.B(Palette.Red);
            status.Text = e.Message;
            failed = true; // следующее нажатие откроет страницу релиза
            updateButton.Content = "Открыть страницу релиза";
            updateButton.Width = double.NaN;
            updateButton.Padding = new Thickness(16, 0);
            updateButton.IsEnabled = skipButton.IsEnabled = true;
        }
    }
}
