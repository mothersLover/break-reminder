using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>Обновление заметок помощника по разговору — в фоне, ошибки молча пропускаем (попробуем в следующий раз).</summary>
static class AssistantNotes
{
    static bool running;

    public static async Task UpdateAsync(AppSettings settings, TutorMemory memory)
    {
        if (running || !settings.AiOn || memory.SummaryRequest(DateTime.Now) is not { } req) return;
        running = true;
        try
        {
            var notes = await AiClients.Create(settings).CompleteAsync(req.System, new[] { req.Request });
            memory.Notes = notes;
            memory.MarkSummarized(req.Count);
        }
        catch { }
        finally { running = false; }
    }
}

/// <summary>Чат с ИИ-помощником: вопросы, проверка заданий. Помнит человека по профилю и заметкам.</summary>
public sealed class AssistantWindow : Window
{
    readonly Func<AppSettings> settings;
    readonly TutorMemory memory;
    readonly StackPanel list = new() { Spacing = 10, Margin = new Thickness(0, 0, 8, 0) };
    readonly ScrollViewer scroll;
    readonly TextBox input = new()
    {
        AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 76, MaxLength = 8000,
        Watermark = "Напиши вопрос или своё решение…  (Enter — отправить, Shift+Enter — новая строка)"
    };
    readonly Button send = new() { Content = "Отправить", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    readonly TextBlock subtitle = Card.Text("", 12.5, Palette.Muted);
    CancellationTokenSource? cts;
    Control? typing;

    // ---- занятие по расписанию ----
    PracticeKind? practice;
    int practiceMinutes;
    DateTime practiceStart, practiceEnd;
    bool practiceAnswered, practiceFinishing;
    readonly TextBlock bannerText = Card.Text("", 14, Palette.Text, FontWeight.SemiBold);
    readonly Button bannerButton = new() { Content = "Закончить", VerticalAlignment = VerticalAlignment.Center };
    readonly Border banner;
    readonly DispatcherTimer practiceTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Занятие закончилось: вид и сколько минут длилось (0 — так и не начал отвечать).</summary>
    public event Action<PracticeKind, int>? PracticeEnded;

    public bool InPractice => practice != null;

    public AssistantWindow(Func<AppSettings> settings, TutorMemory memory)
    {
        this.settings = settings;
        this.memory = memory;
        Title = "Помощник";
        Icon = Icons.Clock(Palette.Green);
        Width = 620;
        Height = 760;
        MinWidth = 460;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.B(Palette.Bg);

        var memoryButton = new Button { Content = "Память" };
        ToolTip.SetTip(memoryButton, "Что помощник о тебе знает: профиль и заметки о прошлых занятиях");
        memoryButton.Click += async (_, _) => await new MemoryWindow(memory).ShowDialog(this);
        var newButton = new Button { Content = "Новый разговор" };
        ToolTip.SetTip(newButton, "Очистить переписку. Заметки о занятиях сохранятся — помощник тебя не забудет");
        newButton.Click += async (_, _) => await NewConversation();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        header.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children = { Card.Text("Помощник", 26, Palette.Text, FontWeight.Bold), subtitle }
        });
        Grid.SetColumn(memoryButton, 1);
        memoryButton.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(memoryButton);
        Grid.SetColumn(newButton, 2);
        newButton.VerticalAlignment = VerticalAlignment.Center;
        newButton.Margin = new Thickness(8, 0, 0, 0);
        header.Children.Add(newButton);

        scroll = new ScrollViewer { Content = list, Margin = new Thickness(0, 14, 0, 12) };

        var bannerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        bannerText.VerticalAlignment = VerticalAlignment.Center;
        bannerGrid.Children.Add(bannerText);
        Grid.SetColumn(bannerButton, 1);
        bannerGrid.Children.Add(bannerButton);
        banner = new Border
        {
            Background = Palette.B(Palette.Card), BorderBrush = Palette.B(Palette.Blue), BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 8), Margin = new Thickness(0, 14, 0, 0),
            IsVisible = false, Child = bannerGrid
        };
        bannerButton.Click += async (_, _) => await FinishPractice();
        practiceTimer.Tick += (_, _) => UpdateBanner();

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        bottom.Children.Add(input);
        Grid.SetColumn(send, 1);
        bottom.Children.Add(send);
        send.Click += async (_, _) => await Send();
        // Enter — отправить, Shift+Enter — перенос строки (перехватываем до TextBox)
        input.AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.Enter && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                e.Handled = true;
                await Send();
            }
        }, RoutingStrategies.Tunnel);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(header);
        Grid.SetRow(banner, 1);
        root.Children.Add(banner);
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);
        Content = root;

        Opened += (_, _) => input.Focus();
        Closed += (_, _) =>
        {
            cts?.Cancel();
            EndPractice();
            _ = AssistantNotes.UpdateAsync(settings(), memory);   // запомнить, о чём говорили
        };
        Rebuild();
    }

    /// <summary>Перерисовать переписку (и подпись с подключением — его могли поменять в настройках).</summary>
    public void Rebuild()
    {
        subtitle.Text = AiClients.Describe(settings());
        list.Children.Clear();
        if (memory.Chat.Count == 0) list.Children.Add(Bubble(Welcome(), false));
        foreach (var m in memory.Chat) list.Children.Add(Bubble(m.Text, m.IsUser));
        ScrollToEnd();
    }

    string Welcome()
    {
        var s = settings();
        string hi = s.UserName.Length > 0 ? $"Привет, {s.UserName}!" : "Привет!";
        return memory.Profile.Length == 0 && memory.Notes.Length == 0
            ? hi + " Я помогаю с хобби, привычками и учёбой. Расскажи, чем занимаешься и чего хочешь добиться — " +
              "например: «Учу французский по Édito A2, дошёл до Unité 5, хочу свободнее говорить на работе».\n\n" +
              "Я запомню это и буду помогать: объяснять, проверять домашние задания, гонять по пройденному. " +
              "Что я о тебе знаю, можно посмотреть и поправить в «Памяти»."
            : hi + " Над чем сегодня работаем? Можно задать вопрос, прислать упражнение на проверку или попросить потренировать пройденное.";
    }

    async Task Send()
    {
        var text = (input.Text ?? "").Trim();
        if (text.Length == 0 || cts != null) return;
        input.Text = "";
        if (practice != null) practiceAnswered = true;
        await SendText(text);
    }

    /// <summary>Отправить сообщение от имени человека; extra — дополнительные указания помощнику на этот запрос.</summary>
    async Task SendText(string text, string? extra = null)
    {
        while (cts != null) await Task.Delay(200);           // дождаться прошлого ответа
        if (memory.Chat.Count == 0) list.Children.Clear();  // убрать приветствие

        var s = settings();
        memory.Add(new ChatMessage(true, text, DateTime.Now));
        list.Children.Add(Bubble(text, true));
        SetBusy(true);

        string system = memory.SystemPrompt(s, DateTime.Now);
        if (practice is { } kind) system += "\n\n" + PracticeText.Instructions(kind, practiceMinutes);
        if (extra != null) system += "\n" + extra;

        cts = new CancellationTokenSource();
        try
        {
            var client = AiClients.Create(s);
            var reply = await client.CompleteAsync(system, AiClients.ForModel(memory.Chat), cts.Token);
            memory.Add(new ChatMessage(false, reply, DateTime.Now));
            RemoveTyping();
            list.Children.Add(Bubble(reply, false));
        }
        catch (OperationCanceledException) { return; }   // окно закрыли
        catch (Exception e)
        {
            RemoveTyping();
            // сообщение человека остаётся в разговоре — следующая отправка его подхватит
            list.Children.Add(ErrorBubble(e is AiException ? e.Message : "Не получилось: " + e.Message));
        }
        finally
        {
            cts?.Dispose();
            cts = null;
            SetBusy(false);
        }
        ScrollToEnd();
        input.Focus();
    }

    // ---------- занятие ----------

    /// <summary>Начать занятие: таймер сверху, помощник сам даёт первое задание.</summary>
    public async Task StartPractice(PracticeKind kind, int minutes)
    {
        if (practice != null) return;
        practice = kind;
        practiceMinutes = minutes;
        practiceStart = DateTime.Now;
        practiceEnd = practiceStart.AddMinutes(minutes);
        practiceAnswered = practiceFinishing = false;
        banner.IsVisible = true;
        UpdateBanner();
        practiceTimer.Start();
        await SendText($"▶ {PracticeText.Title(kind)} · {minutes} мин", "Начни занятие сразу с первого задания.");
        input.Focus();
    }

    void UpdateBanner()
    {
        if (practice is not { } kind) return;
        var left = practiceEnd - DateTime.Now;
        if (left > TimeSpan.Zero)
        {
            bannerText.Text = $"{PracticeText.Title(kind)} · осталось {Format.Ms(left)}";
            bannerText.Foreground = Palette.B(Palette.Text);
            bannerButton.Content = "Закончить";
            bannerButton.Classes.Remove("accent");
        }
        else
        {
            bannerText.Text = "Время вышло — подведём итог?";
            bannerText.Foreground = Palette.B(Palette.Green);
            bannerButton.Content = "Подвести итог";
            if (!bannerButton.Classes.Contains("accent")) bannerButton.Classes.Add("accent");
        }
    }

    /// <summary>«Закончить» / «Подвести итог»: помощник подводит итог, занятие засчитывается.</summary>
    async Task FinishPractice()
    {
        if (practice == null || practiceFinishing) return;
        practiceFinishing = true;
        bannerButton.IsEnabled = false;
        if (practiceAnswered)
            await SendText("Стоп, подведи итог занятия.", "Сейчас подведи итог занятия: 2–3 строки.");
        bannerButton.IsEnabled = true;
        EndPractice();
    }

    void EndPractice()
    {
        if (practice is not { } kind) return;
        practice = null;
        practiceTimer.Stop();
        banner.IsVisible = false;
        var elapsed = DateTime.Now - practiceStart;
        int minutes = practiceAnswered ? Math.Clamp((int)Math.Round(elapsed.TotalMinutes), 1, practiceMinutes * 3) : 0;
        PracticeEnded?.Invoke(kind, minutes);
    }

    async Task NewConversation()
    {
        if (cts != null || memory.Chat.Count == 0 || practice != null) return;
        if (!await ConfirmDialog.Ask(this, "Начать новый разговор? Переписка очистится, но помощник сохранит заметки о ней.")) return;
        var s = settings();
        subtitle.Text = "Запоминаю разговор…";
        await AssistantNotes.UpdateAsync(s, memory);
        memory.ClearChat();
        Rebuild();
        input.Focus();
    }

    void SetBusy(bool busy)
    {
        send.IsEnabled = !busy;
        if (busy)
        {
            typing = Bubble("Думаю…", false, muted: true);
            list.Children.Add(typing);
            ScrollToEnd();
        }
    }

    void RemoveTyping()
    {
        if (typing != null) list.Children.Remove(typing);
        typing = null;
    }

    void ScrollToEnd() => Dispatcher.UIThread.Post(() => scroll.ScrollToEnd(), DispatcherPriority.Background);

    static Control Bubble(string text, bool user, bool muted = false) => new Border
    {
        Background = Palette.B(user ? Color.Parse("#2D4A63") : Palette.Card),
        CornerRadius = new CornerRadius(10),
        Padding = new Thickness(14, 10),
        MaxWidth = 470,
        HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
        Child = new SelectableTextBlock
        {
            Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.B(muted ? Palette.Muted : Palette.Text),
            FontStyle = muted ? FontStyle.Italic : FontStyle.Normal
        }
    };

    static Control ErrorBubble(string text) => new Border
    {
        Background = Palette.B(Palette.Card),
        BorderBrush = Palette.B(Palette.Red),
        BorderThickness = new Thickness(4, 0, 0, 0),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(12, 8),
        MaxWidth = 470,
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new SelectableTextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Text) }
    };
}

/// <summary>«Что помощник обо мне знает»: профиль (пишет человек) и заметки (ведёт помощник). Можно править и стереть.</summary>
public sealed class MemoryWindow : Window
{
    public MemoryWindow(TutorMemory memory)
    {
        Title = "Память помощника";
        Icon = Icons.Clock(Palette.Green);
        Width = 640;
        Height = 680;
        MinWidth = 480;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Palette.B(Palette.Bg);

        var profileBox = new TextBox
        {
            Text = memory.Profile, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 3000,
            Watermark = "Например: Учу французский по учебнику Édito A2, сейчас Unité 5. Цель — к лету говорить с коллегами. " +
                        "Занимаюсь по вечерам 20–30 минут. Путаю être и avoir в passé composé."
        };
        var notesBox = new TextBox
        {
            Text = memory.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = TutorMemory.MaxNotesChars,
            Watermark = "Пока пусто — заметки появятся после первого разговора."
        };

        var save = new Button { Content = "Сохранить", Classes = { "accent" }, IsDefault = true };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var forget = new Button { Content = "Забыть всё" };
        save.Click += (_, _) =>
        {
            memory.Profile = profileBox.Text ?? "";
            memory.Notes = notesBox.Text ?? "";
            Close();
        };
        cancel.Click += (_, _) => Close();
        forget.Click += async (_, _) =>
        {
            if (!await ConfirmDialog.Ask(this, "Стереть профиль, заметки и переписку? Помощник забудет всё, что знал о тебе.")) return;
            memory.ForgetAll();
            Close();
        };

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(forget);
        Grid.SetColumn(save, 2);
        buttons.Children.Add(save);
        Grid.SetColumn(cancel, 3);
        buttons.Children.Add(cancel);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,2*,Auto,Auto,3*,Auto"), Margin = new Thickness(20, 16, 20, 16) };
        void Add(Control c, int row) { Grid.SetRow(c, row); root.Children.Add(c); }
        Add(Card.Text("О себе", 16, Palette.Text, FontWeight.SemiBold), 0);
        Add(new TextBlock
        {
            Classes = { "hint" }, Margin = new Thickness(0, 2, 0, 6),
            Text = "Чем занимаешься, по какому учебнику, где остановился, чего хочешь добиться. Помощник видит это в каждом разговоре."
        }, 1);
        Add(profileBox, 2);
        Add(Card.Text("Заметки помощника", 16, Palette.Text, FontWeight.SemiBold).WithMargin(0, 14, 0, 0), 3);
        Add(new TextBlock
        {
            Classes = { "hint" }, Margin = new Thickness(0, 2, 0, 6),
            Text = "Помощник сам обновляет их после разговора: что пройдено, типичные ошибки, договорённости. Можно поправить руками."
        }, 4);
        Add(notesBox, 5);
        Add(buttons, 6);
        Content = root;
    }
}

static class ControlExt
{
    public static T WithMargin<T>(this T c, double left, double top, double right, double bottom) where T : Control
    {
        c.Margin = new Thickness(left, top, right, bottom);
        return c;
    }
}

public enum PracticeChoice { Start, Later, Skip }

/// <summary>«Пора позаниматься» в углу экрана: «Начать» / «Позже» (через 30 мин) / «Не сегодня». Без ответа 3 минуты — как «позже».</summary>
public sealed class PracticeOfferWindow : Window
{
    bool answered;

    public event Action<PracticeChoice>? Answered;

    public PracticeOfferWindow(PracticeKind kind, AppSettings s, IPlatform platform, int streak)
    {
        Title = PracticeText.Title(kind);
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

        int minutes = PracticeText.Minutes(kind, s);
        string hint = kind switch
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
                Card.Text($"{minutes} мин · помощник" + (streak > 0 ? $" · серия {streak} дн." : ""), 13, Palette.Muted),
                Card.Text(PracticeText.Title(kind), 21, Palette.Text, FontWeight.Bold),
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
                Position = new PixelPoint(wa.Right - (int)((Bounds.Width + 16) * k), y);
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
