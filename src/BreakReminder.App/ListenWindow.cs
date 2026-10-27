using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>
/// «Послушай и повтори»: фраза из карточек звучит медленно → пауза, чтобы повторить вслух → ещё раз в обычном темпе.
/// Небольшое окно в углу — на экран можно не смотреть.
/// </summary>
public sealed class ListenWindow : Window
{
    static ListenWindow? current;

    readonly IReadOnlyList<Flashcard> phrases;
    readonly AppSettings s;
    readonly IPlatform platform;
    readonly CancellationTokenSource stop = new();
    CancellationTokenSource? skip;

    readonly TextBlock counter = Card.Text("", 13, Palette.Muted);
    readonly TextBlock front = Card.Text("", 14, Palette.Muted);
    readonly TextBlock back = Card.Text("", 22, Palette.Text, FontWeight.SemiBold);
    readonly TextBlock status = Card.Text("", 14, Palette.Green, FontWeight.SemiBold);
    readonly ProgressBar progress = new() { Height = 4, Minimum = 0, Maximum = 1, Foreground = Palette.B(Palette.Blue) };
    readonly Button next = new() { Content = "Дальше", Classes = { "flat" }, Background = Palette.B(Palette.Gray) };
    readonly Button close = new() { Content = "Стоп", Classes = { "flat" }, Background = Palette.B(Palette.Gray) };

    /// <summary>Показать (если уже открыто — заново с новыми фразами).</summary>
    public static void Show(IReadOnlyList<Flashcard> phrases, AppSettings s, IPlatform platform)
    {
        current?.Close();
        current = new ListenWindow(phrases, s, platform);
        current.Show();
    }

    ListenWindow(IReadOnlyList<Flashcard> phrases, AppSettings s, IPlatform platform)
    {
        this.phrases = phrases;
        this.s = s;
        this.platform = platform;
        Title = "Послушай и повтори";
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
        PopupDrag.Enable(this, "listen");

        next.Click += (_, _) => skip?.Cancel();
        close.Click += (_, _) => Close();
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*"), Height = 40, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(next);
        Grid.SetColumn(close, 2);
        buttons.Children.Add(close);

        var body = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    Children = { Card.Text("🎧 Послушай и повтори", 17, Palette.Text, FontWeight.Bold), counter.WithColumn(1) }
                },
                progress.WithMargin(0, 4, 0, 8),
                back, front, status.WithMargin(0, 8, 0, 0),
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
                Position = PopupDrag.Place(this, "listen", new PixelPoint(wa.Right - (int)((Bounds.Width + 16) * k), wa.Bottom - (int)((Bounds.Height + 16) * k)));
            }
            Opacity = 1;
            _ = Run();
        };
        Closed += (_, _) =>
        {
            stop.Cancel();
            platform.StopSpeaking();
            if (current == this) current = null;
        };
    }

    async Task Run()
    {
        if (platform.VoiceFor(s.SpeechLang) == null)
        {
            Finish("Нет голоса", SpeechText.InstallHint(platform.Name, s.SpeechLang));
            return;
        }
        if (phrases.Count == 0)
        {
            Finish("Пока нечего слушать", "Фразы берутся из карточек — попроси помощника сделать карточки по теме.");
            return;
        }

        int done = 0;
        for (int i = 0; i < phrases.Count && !stop.IsCancellationRequested; i++)
        {
            var c = phrases[i];
            counter.Text = $"{i + 1} из {phrases.Count}";
            progress.Value = (double)i / phrases.Count;
            back.Text = c.Back;
            front.Text = c.Front;
            skip = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            try
            {
                await Say("Слушай…", c.Back, s.SpeechSlow, skip.Token);
                await Pause("Повтори вслух", SpeechText.RepeatPause(c.Back), skip.Token);
                await Say("Ещё раз", c.Back, false, skip.Token);
                await Pause("Повтори ещё раз", SpeechText.RepeatPause(c.Back), skip.Token);
                done++;
            }
            catch (OperationCanceledException) when (!stop.IsCancellationRequested)
            {
                platform.StopSpeaking();   // «Дальше» — к следующей фразе
            }
            catch (OperationCanceledException) { return; }
            finally
            {
                skip.Dispose();
                skip = null;
            }
        }
        if (stop.IsCancellationRequested) return;
        progress.Value = 1;
        Finish("Готово!", $"Прослушано и повторено: {GreetingWindow.Plural(done, "фраза", "фразы", "фраз")}.");
        DispatcherTimer.RunOnce(() => Close(), TimeSpan.FromSeconds(6));
    }

    async Task Say(string what, string text, bool slow, CancellationToken ct)
    {
        status.Text = what;
        status.Foreground = Palette.B(Palette.Blue);
        await platform.SpeakAsync(text, s.SpeechLang, slow, ct);
        ct.ThrowIfCancellationRequested();
    }

    async Task Pause(string what, TimeSpan length, CancellationToken ct)
    {
        status.Text = what;
        status.Foreground = Palette.B(Palette.Green);
        await Task.Delay(length, ct);
    }

    void Finish(string title, string text)
    {
        counter.Text = "";
        back.Text = title;
        front.Text = text;
        front.Foreground = Palette.B(Palette.Text);
        status.Text = "";
        next.IsVisible = false;
        Grid.SetColumn(close, 0);
        Grid.SetColumnSpan(close, 3);
        close.Content = "Закрыть";
    }
}
