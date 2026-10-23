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

/// <summary>Обновление заметок помощника по разговору — в фоне, ошибки молча пропускаем (попробуем в следующий раз).</summary>
static class AssistantNotes
{
    static bool running;

    public static async Task UpdateAsync(AppSettings settings, TutorMemory memory, CardStore cards)
    {
        if (running || !settings.AiOn || memory.SummaryRequest(DateTime.Now, cards.RecentFronts(60)) is not { } req) return;
        running = true;
        try
        {
            var reply = await AiClients.Create(settings).CompleteAsync(req.System, new[] { req.Request });
            var (notes, found) = CardStore.Extract(reply);   // заметки + новые карточки из разговора
            if (notes.Length > 0) memory.Notes = notes;
            cards.Add(found, DateTime.Now);
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

    readonly CardStore cards;
    readonly Button cardsButton = new() { Content = "Карточки" };

    // ---- фото к сообщению ----
    readonly List<string> pending = new();          // подготовленные фото, ещё не отправленные
    readonly WrapPanel pendingStrip = new() { Margin = new Thickness(0, 0, 0, 8), IsVisible = false };
    readonly Button attach = new() { Content = "📎", FontSize = 18, VerticalAlignment = VerticalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 0) };
    readonly Border homeworkRow;

    // ---- закреплённое ----
    readonly Expander pinsPanel = new() { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 12, 0, 0) };
    readonly StackPanel pinsList = new() { Spacing = 6 };
    readonly List<Action> pinSyncs = new();         // обновить 📌 на пузырях, когда закреплённое изменилось

    // ---- нет связи (прогноз погоды недоступен) ----
    readonly Border offlineBar;
    readonly TextBlock offlineText = Card.Text("", 13.5, Palette.Text);

    /// <summary>Открыть карточки (повторение и список).</summary>
    public event Action? CardsRequested;

    readonly IPlatform? platform;

    /// <summary>Произнести иностранные фразы из сообщения (русские пояснения — пропускаются).</summary>
    void SpeakForeign(string text)
    {
        var parts = SpeechText.Foreign(text);
        if (platform == null || parts.Count == 0) return;
        _ = platform.SpeakAsync(string.Join(". ", parts), settings().SpeechLang, false);
    }

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

    /// <summary>Панель «Закреплено»: самое новое сверху; щелчок по тексту — развернуть, ✕ — открепить.</summary>
    void RebuildPins()
    {
        foreach (var sync in pinSyncs) sync();
        pinsList.Children.Clear();
        var items = memory.Pins.Reverse().ToList();
        pinsPanel.IsVisible = items.Count > 0;
        if (items.Count == 0) return;
        string first = items[0].Text.Split('\n')[0];
        pinsPanel.Header = new TextBlock
        {
            Text = $"📌 Закреплено · {items.Count}   {first}", FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Palette.B(Palette.Text), MaxWidth = 500
        };
        foreach (var pin in items)
        {
            var text = new SelectableTextBlock
            {
                Text = pin.Text, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Palette.B(Palette.Text)
            };
            var more = new Button { Content = "развернуть", Classes = { "link" }, FontSize = 12 };
            more.Click += (_, _) =>
            {
                text.MaxLines = text.MaxLines == 3 ? 0 : 3;
                more.Content = text.MaxLines == 0 ? "свернуть" : "развернуть";
            };
            var unpin = new Button
            {
                Content = "✕", Background = Brushes.Transparent, Foreground = Palette.B(Palette.Muted),
                Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Top
            };
            ToolTip.SetTip(unpin, "Открепить");
            string id = pin.Id;
            unpin.Click += (_, _) => memory.Unpin(id);
            var body = new StackPanel
            {
                Spacing = 2,
                Children = { Card.Text($"{pin.At:d MMMM, HH:mm} · {(pin.FromUser ? "ты" : "помощник")}", 11.5, Palette.Muted), text }
            };
            if (pin.Text.Length > 160 || pin.Text.Count(c => c == '\n') > 2) body.Children.Add(more);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(body);
            Grid.SetColumn(unpin, 1);
            row.Children.Add(unpin);
            pinsList.Children.Add(new Border
            {
                Background = Palette.B(Palette.Card), BorderBrush = Palette.B(Palette.Amber), BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6), Child = row
            });
        }
    }

    void TogglePin(string text, bool fromUser)
    {
        if (memory.IsPinned(text)) memory.UnpinText(text);
        else if (!memory.Pin(text, fromUser, DateTime.Now))
            list.Children.Add(ErrorBubble($"Закреплено уже {TutorMemory.MaxPins} — открепи что-нибудь ненужное."));
        if (memory.IsPinned(text)) pinsPanel.IsExpanded = true;   // показать, куда попало
    }

    async Task CopyText(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    /// <summary>Меню по правой кнопке на сообщении: закрепить/открепить, копировать.</summary>
    ContextMenu MessageMenu(string text, bool fromUser)
    {
        var pin = new MenuItem();
        var copy = new MenuItem { Header = "Копировать" };
        pin.Click += (_, _) => TogglePin(text, fromUser);
        copy.Click += async (_, _) => await CopyText(text);
        var menu = new ContextMenu { Items = { pin, copy } };
        if (platform != null && SpeechText.Foreign(text).Count > 0)
        {
            var listen = new MenuItem { Header = "🔊 Прослушать" };
            listen.Click += (_, _) => SpeakForeign(text);
            menu.Items.Insert(0, listen);
        }
        menu.Opening += (_, _) => pin.Header = memory.IsPinned(text) ? "Открепить" : "📌 Закрепить";
        return menu;
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

    // ---------- занятие ----------

    /// <summary>Начать занятие: таймер сверху, помощник сам даёт первое задание.</summary>
    public async Task StartPractice(PracticeKind kind, int minutes)
    {
        if (practice != null) return;
        if (!await AiAvailable())
        {
            PracticeEnded?.Invoke(kind, 0);   // занятие не состоялось — предложим позже
            return;
        }
        practice = kind;
        practiceMinutes = minutes;
        practiceStart = DateTime.Now;
        practiceEnd = practiceStart.AddMinutes(minutes);
        practiceAnswered = practiceFinishing = false;
        banner.IsVisible = true;
        UpdateBanner();
        practiceTimer.Start();
        if (kind == PracticeKind.Homework)
        {
            // фото, которые уже приложены, уходят вместе с началом домашки
            var images = pending.ToList();
            pending.Clear();
            RefreshPending();
            practiceAnswered = images.Count > 0;
            await SendText("📚 Сел за домашнее задание", "Начни: коротко попроси прислать задание и свои ответы (если их ещё нет).", images);
        }
        else
            await SendText($"▶ {PracticeText.Title(kind)} · {minutes} мин", "Начни занятие сразу с первого задания.");
        input.Focus();
    }

    void UpdateBanner()
    {
        if (practice is not { } kind) return;
        homeworkRow.IsVisible = false;
        if (practiceMinutes == 0)
        {
            bannerText.Text = $"{PracticeText.Title(kind)} · идёт {Format.Ms(DateTime.Now - practiceStart)}";
            bannerButton.Content = "Закончить и подвести итог";
            return;
        }
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
        homeworkRow.IsVisible = true;
        var elapsed = DateTime.Now - practiceStart;
        int cap = practiceMinutes > 0 ? practiceMinutes * 3 : 240;
        int minutes = practiceAnswered ? Math.Clamp((int)Math.Round(elapsed.TotalMinutes), 1, cap) : 0;
        PracticeEnded?.Invoke(kind, minutes);
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

    Control Bubble(string text, bool user, bool muted = false, IReadOnlyList<string>? images = null, bool pinnable = true)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (images is { Count: > 0 })
        {
            var strip = new WrapPanel();
            foreach (var path in images) strip.Children.Add(Thumb(path, 150));
            panel.Children.Add(strip);
        }
        pinnable = pinnable && !muted && text.Trim().Length > 0;
        SelectableTextBlock? body = null;
        if (text.Length > 0)
            panel.Children.Add(body = new SelectableTextBlock
            {
                Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.B(muted ? Palette.Muted : Palette.Text),
                FontStyle = muted ? FontStyle.Italic : FontStyle.Normal
            });
        var bubble = new Border
        {
            Background = Palette.B(user ? Color.Parse("#2D4A63") : Palette.Card),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10),
            MaxWidth = 470,
            HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = panel
        };
        if (!pinnable) return bubble;

        // меню и на пузыре, и на тексте (у выделяемого текста своё меню перехватило бы правый щелчок)
        bubble.ContextMenu = MessageMenu(text, user);
        if (body != null) body.ContextMenu = MessageMenu(text, user);

        // в углу при наведении: 🔊 (если есть иностранные фразы) и 📌 (видна всегда, если сообщение закреплено)
        var pinButton = new Button
        {
            Content = "📌", FontSize = 12, Padding = new Thickness(5, 1), Background = Palette.B(Palette.Gray),
            IsVisible = memory.IsPinned(text)
        };
        var speakButton = new Button
        {
            Content = "🔊", FontSize = 12, Padding = new Thickness(5, 1), Background = Palette.B(Palette.Gray), IsVisible = false
        };
        bool speakable = platform != null && SpeechText.Foreign(text).Count > 0;
        ToolTip.SetTip(speakButton, "Прослушать фразы на изучаемом языке");
        speakButton.Click += (_, _) => SpeakForeign(text);
        void Sync(bool hover)
        {
            bool pinned = memory.IsPinned(text);
            pinButton.IsVisible = hover || pinned;
            pinButton.Opacity = pinned ? 1 : 0.75;
            speakButton.IsVisible = hover && speakable;
            ToolTip.SetTip(pinButton, pinned ? "Открепить" : "Закрепить сверху чата");
        }
        pinButton.Click += (_, _) => { TogglePin(text, user); Sync(true); };
        var corner = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -8, -8, 0), Children = { speakButton, pinButton }
        };
        var holder = new Panel { HorizontalAlignment = bubble.HorizontalAlignment, Children = { bubble, corner } };
        holder.PointerEntered += (_, _) => Sync(true);
        holder.PointerExited += (_, _) => Sync(false);
        pinSyncs.Add(() => Sync(holder.IsPointerOver));
        return holder;
    }

    /// <summary>Миниатюра фото (если файла уже нет — подпись).</summary>
    static Control Thumb(string path, int height)
    {
        try
        {
            using var f = File.OpenRead(path);
            return new Image
            {
                Source = Bitmap.DecodeToHeight(f, height * 2), Height = height, Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 6, 6), HorizontalAlignment = HorizontalAlignment.Left
            };
        }
        catch
        {
            return Card.Text("[фото: " + Path.GetFileName(path) + "]", 12.5, Palette.Muted);
        }
    }

    // ---------- фото ----------

    async Task PickImages()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Фото задания",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Фото") { Patterns = AiImages.Extensions.Select(e => "*" + e).ToArray() } }
        });
        AddImages(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList());
    }

    const int MaxImages = 4;

    void AddImages(IReadOnlyList<string> paths)
    {
        var errors = new List<string>();
        foreach (var path in paths)
        {
            if (pending.Count >= MaxImages) { errors.Add($"Не больше {MaxImages} фото за раз."); break; }
            try { pending.Add(ImagePrep.Prepare(path, memory.ImagesDir)); }
            catch (Exception e) { errors.Add(e.Message); }
        }
        RefreshPending();
        if (errors.Count > 0) list.Children.Add(ErrorBubble(string.Join("\n", errors.Distinct())));
        ScrollToEnd();
        input.Focus();
    }

    void RefreshPending()
    {
        pendingStrip.Children.Clear();
        foreach (var path in pending)
        {
            var remove = new Button
            {
                Content = "✕", FontSize = 11, Padding = new Thickness(5, 1), Background = Palette.B(Palette.Gray),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top
            };
            ToolTip.SetTip(remove, "Убрать фото");
            string p = path;
            remove.Click += (_, _) =>
            {
                pending.Remove(p);
                try { File.Delete(p); } catch { }
                RefreshPending();
            };
            pendingStrip.Children.Add(new Panel { Margin = new Thickness(0, 0, 8, 0), Children = { Thumb(path, 64), remove } });
        }
        pendingStrip.IsVisible = pending.Count > 0;
        input.Watermark = pending.Count > 0
            ? "Добавь пояснение к фото (необязательно) и нажми Enter"
            : "Напиши вопрос или своё решение…  (Enter — отправить, Shift+Enter — новая строка)";
    }

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
            if (!await ConfirmDialog.Ask(this, "Стереть профиль, заметки, закреплённое и переписку? Помощник забудет всё, что знал о тебе.")) return;
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

/// <summary>
/// Подготовка фото для помощника: копия в папку разговора; большие (больше ~3,5 МБ или 2000 пикселей по стороне)
/// уменьшаются — сервисы ИИ не принимают слишком большие картинки, а лишние пиксели только тратят лимиты.
/// </summary>
static class ImagePrep
{
    const long MaxBytes = 3_500_000;
    const int MaxSide = 2000, TargetSide = 1568;

    public static string Prepare(string source, string dir)
    {
        string ext = Path.GetExtension(source).ToLowerInvariant();
        if (!AiImages.Extensions.Contains(ext)) throw new AiException($"«{Path.GetFileName(source)}» — не фото. Подходят JPG, PNG, WEBP, GIF.");
        Directory.CreateDirectory(dir);
        string name = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];

        Bitmap bmp;
        try
        {
            using var f = File.OpenRead(source);
            bmp = new Bitmap(f);
        }
        catch { throw new AiException($"Не получилось открыть «{Path.GetFileName(source)}» как картинку."); }

        using (bmp)
        {
            var size = bmp.PixelSize;
            if (new FileInfo(source).Length <= MaxBytes && Math.Max(size.Width, size.Height) <= MaxSide)
            {
                string copy = Path.Combine(dir, name + ext);
                File.Copy(source, copy);
                return copy;
            }
            // уменьшаем (PNG), пока не влезет в лимит
            int side = TargetSide;
            for (int attempt = 0; attempt < 4; attempt++, side = side * 3 / 4)
            {
                double k = Math.Min(1.0, (double)side / Math.Max(size.Width, size.Height));
                var target = new PixelSize(Math.Max(1, (int)(size.Width * k)), Math.Max(1, (int)(size.Height * k)));
                using var scaled = bmp.CreateScaledBitmap(target, BitmapInterpolationMode.HighQuality);
                string png = Path.Combine(dir, name + ".png");
                scaled.Save(png);
                if (new FileInfo(png).Length <= MaxBytes) return png;
                File.Delete(png);
            }
            throw new AiException($"Фото «{Path.GetFileName(source)}» слишком большое даже после уменьшения.");
        }
    }
}
