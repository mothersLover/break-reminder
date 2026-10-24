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

/// <summary>Чат с ИИ-помощником: вопросы, проверка заданий. Помнит человека по профилю и заметкам.</summary>
public sealed partial class AssistantWindow : Window
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

    readonly CardStore cards;
    readonly Button cardsButton = new() { Content = "Карточки" };

    // ---- нет связи (прогноз погоды недоступен) ----
    readonly Border offlineBar;
    readonly TextBlock offlineText = Card.Text("", 13.5, Palette.Text);

    /// <summary>Открыть карточки (повторение и список).</summary>
    public event Action? CardsRequested;

    readonly IPlatform? platform;

    public AssistantWindow(Func<AppSettings> settings, TutorMemory memory, CardStore cards, IPlatform? platform = null)
    {
        this.platform = platform;
        this.settings = settings;
        this.memory = memory;
        this.cards = cards;
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

        ToolTip.SetTip(cardsButton, "Повторить карточки: ошибки и новые слова из разговоров");
        cardsButton.Click += (_, _) => CardsRequested?.Invoke();

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto") };
        header.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children = { Card.Text("Помощник", 26, Palette.Text, FontWeight.Bold), subtitle }
        });
        Grid.SetColumn(cardsButton, 1);
        cardsButton.VerticalAlignment = VerticalAlignment.Center;
        cardsButton.Margin = new Thickness(0, 0, 8, 0);
        header.Children.Add(cardsButton);
        Grid.SetColumn(memoryButton, 2);
        memoryButton.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(memoryButton);
        Grid.SetColumn(newButton, 3);
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

        var homework = new Button { Content = "📚 Сел за домашку", Classes = { "link" }, FontSize = 14 };
        homework.Click += async (_, _) => await StartPractice(PracticeKind.Homework, 0);
        homeworkRow = new Border
        {
            Margin = new Thickness(0, 12, 0, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children = { homework, Card.Text("пришли фото задания — проверю по шагам", 12.5, Palette.Muted).WithMargin(0, 1, 0, 0) }
            }
        };

        // 📎 — выбрать фото; можно и перетащить файлы в окно
        ToolTip.SetTip(attach, "Приложить фото (страница учебника, тетрадь). Можно перетащить файл в окно");
        attach.Click += async (_, _) => await PickImages();
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            var files = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
            if (files is { Count: > 0 }) AddImages(files);
        });

        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        Grid.SetColumnSpan(pendingStrip, 3);
        bottom.Children.Add(pendingStrip);
        Grid.SetRow(attach, 1);
        bottom.Children.Add(attach);
        Grid.SetRow(input, 1);
        Grid.SetColumn(input, 1);
        bottom.Children.Add(input);
        Grid.SetRow(send, 1);
        Grid.SetColumn(send, 2);
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

        pinsPanel.Content = new ScrollViewer { MaxHeight = 230, Content = pinsList };

        var retry = new Button { Content = "Проверить снова", VerticalAlignment = VerticalAlignment.Center };
        retry.Click += async (_, _) =>
        {
            retry.IsEnabled = false;
            if (AiClients.Gate is { } g) await g.CheckAsync(force: true);
            retry.IsEnabled = true;
            UpdateOnline();
        };
        var offlineGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        offlineText.VerticalAlignment = VerticalAlignment.Center;
        offlineGrid.Children.Add(offlineText);
        Grid.SetColumn(retry, 1);
        offlineGrid.Children.Add(retry);
        offlineBar = new Border
        {
            Background = Palette.B(Palette.Card), BorderBrush = Palette.B(Palette.Red), BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 8), Margin = new Thickness(0, 12, 0, 0),
            IsVisible = false, Child = offlineGrid
        };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(header);
        Grid.SetRow(banner, 1);
        root.Children.Add(banner);
        Grid.SetRow(homeworkRow, 1);
        root.Children.Add(homeworkRow);
        var middle = new StackPanel { Children = { offlineBar, pinsPanel } };
        Grid.SetRow(middle, 2);
        root.Children.Add(middle);
        Grid.SetRow(scroll, 3);
        root.Children.Add(scroll);
        Grid.SetRow(bottom, 4);
        root.Children.Add(bottom);
        Content = root;

        Opened += (_, _) => input.Focus();
        Closed += (_, _) =>
        {
            cts?.Cancel();
            EndPractice();
            cards.Changed -= UpdateCardsButton;
            memory.PinsChanged -= RebuildPins;
            _ = AssistantNotes.UpdateAsync(settings(), memory, cards);   // запомнить, о чём говорили
        };
        cards.Changed += UpdateCardsButton;
        memory.PinsChanged += RebuildPins;
        UpdateCardsButton();
        RebuildPins();
        Rebuild();
        Opened += async (_, _) =>
        {
            if (settings().AiRequireWeather && AiClients.Gate is { } g) await g.CheckAsync();
            UpdateOnline();
        };
    }

    /// <summary>Показать или спрятать плашку «нет связи» по последней проверке прогноза.</summary>
    public void UpdateOnline()
    {
        bool offline = settings().AiRequireWeather && AiClients.Gate is { Online: false };
        offlineBar.IsVisible = offline;
        if (offline)
            offlineText.Text = "📡 Нет связи — прогноз погоды недоступен. Помощник отключён, запросы не отправляются. " +
                               "Карточки работают как обычно.";
    }

    /// <summary>Можно ли сейчас обращаться к ИИ (если включена проверка связи по прогнозу).</summary>
    async Task<bool> AiAvailable()
    {
        if (!settings().AiRequireWeather || AiClients.Gate is not { } g) return true;
        bool ok = await g.CheckAsync();
        UpdateOnline();
        if (!ok)
        {
            list.Children.Add(ErrorBubble(ConnectivityGate.OfflineMessage));
            ScrollToEnd();
        }
        return ok;
    }

    void UpdateCardsButton()
    {
        int due = cards.DueCount(DateTime.Now);
        cardsButton.Content = due > 0 ? $"Карточки ({due})" : "Карточки";
        cardsButton.IsVisible = cards.All.Count > 0;
    }

    /// <summary>Перерисовать переписку (и подпись с подключением — его могли поменять в настройках).</summary>
    public void Rebuild()
    {
        subtitle.Text = AiClients.Describe(settings());
        list.Children.Clear();
        pinSyncs.Clear();
        if (memory.Chat.Count == 0) list.Children.Add(Bubble(Welcome(), false, pinnable: false));
        foreach (var m in memory.Chat) list.Children.Add(Bubble(m.Text, m.IsUser, images: m.Images));
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
        if ((text.Length == 0 && pending.Count == 0) || cts != null) return;
        if (!await AiAvailable()) return;   // без связи — текст и фото остаются в поле ввода
        input.Text = "";
        if (practice != null) practiceAnswered = true;
        var images = pending.ToList();
        pending.Clear();
        RefreshPending();
        await SendText(text, images: images);
    }

    /// <summary>Отправить сообщение от имени человека; extra — дополнительные указания помощнику на этот запрос.</summary>
    async Task SendText(string text, string? extra = null, IReadOnlyList<string>? images = null)
    {
        while (cts != null) await Task.Delay(200);           // дождаться прошлого ответа
        if (memory.Chat.Count == 0) list.Children.Clear();  // убрать приветствие

        var s = settings();
        if (images is { Count: 0 }) images = null;
        memory.Add(new ChatMessage(true, text, DateTime.Now, Images: images));
        list.Children.Add(Bubble(text, true, images: images));
        SetBusy(true);

        string system = memory.SystemPrompt(s, DateTime.Now);
        if (practice is { } kind) system += "\n\n" + PracticeText.Instructions(kind, practiceMinutes);
        if (extra != null) system += "\n" + extra;

        cts = new CancellationTokenSource();
        try
        {
            var client = AiClients.Create(s);
            var reply = await client.CompleteAsync(system, AiClients.ForModel(memory.Chat), cts.Token);
            // карточки, которые помощник сделал по просьбе, — сразу в повторение, а блок из текста убираем
            var (shown, found) = CardStore.Extract(reply);
            int added = cards.Add(found, DateTime.Now);
            if (added > 0) shown = (shown.Length > 0 ? shown + "\n\n" : "") + $"＋ {GreetingWindow.Plural(added, "карточка", "карточки", "карточек")} в повторении";
            else if (shown.Length == 0) shown = found.Count > 0 ? "Такие карточки уже есть." : reply;
            memory.Add(new ChatMessage(false, shown, DateTime.Now));
            RemoveTyping();
            list.Children.Add(Bubble(shown, false));
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

    async Task NewConversation()
    {
        if (cts != null || memory.Chat.Count == 0 || practice != null) return;
        if (!await ConfirmDialog.Ask(this, "Начать новый разговор? Переписка очистится, но помощник сохранит заметки о ней.")) return;
        var s = settings();
        subtitle.Text = "Запоминаю разговор…";
        await AssistantNotes.UpdateAsync(s, memory, cards);
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
}
