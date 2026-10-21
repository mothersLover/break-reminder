using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>
/// Карточки: повторение (вопрос → «Показать ответ» → «Не помню / Трудно / Помню / Легко») и список всех карточек.
/// С minutes — как занятие по расписанию: таймер сверху, по окончании времени — итог.
/// </summary>
public sealed class CardsWindow : Window
{
    readonly CardStore store;
    readonly int? minutes;
    readonly DateTime started = DateTime.Now;
    readonly List<Flashcard> queue;
    Flashcard? current;
    bool shown;
    int reviewed;

    readonly TextBlock subtitle = Card.Text("", 12.5, Palette.Muted);
    readonly Button modeButton = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock timerText = Card.Text("", 14, Palette.Text, FontWeight.SemiBold);
    readonly Border timerBar;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly ContentControl body = new();
    readonly Control reviewPane, listPane;

    // повторение
    readonly TextBlock front = new() { FontSize = 24, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Text) };
    readonly TextBox answer = new() { Watermark = "Твой ответ (можно не писать — просто вспомни)", FontSize = 16 };
    readonly Button showButton = new() { Content = "Показать ответ", Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 8) };
    readonly SelectableTextBlock back = new() { FontSize = 22, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Green) };
    readonly TextBlock match = Card.Text("", 13.5, Palette.Muted);
    readonly TextBlock note = Card.Text("", 13.5, Palette.Muted);
    readonly Grid grades = new() { ColumnDefinitions = new ColumnDefinitions("*,8,*,8,*,8,*"), Height = 54, Margin = new Thickness(0, 6, 0, 0) };
    readonly StackPanel answerPart = new() { Spacing = 8 };
    readonly StackPanel donePart = new() { Spacing = 10, IsVisible = false };
    readonly StackPanel cardPart = new() { Spacing = 12 };

    // список
    readonly TextBox newFront = new() { Watermark = "Вопрос (например: «я пошёл в кино»)", MaxLength = 300 };
    readonly TextBox newBack = new() { Watermark = "Ответ (je suis allé au cinéma)", MaxLength = 300 };
    readonly TextBox newNote = new() { Watermark = "Пояснение (необязательно)", MaxLength = 300 };
    readonly TextBox search = new() { Watermark = "Поиск…" };
    readonly StackPanel rows = new() { Spacing = 6 };

    /// <summary>Окно закрыто: сколько карточек повторено и сколько минут занимался.</summary>
    public event Action<int, int>? Finished;

    public CardsWindow(CardStore store, int? minutes = null)
    {
        this.store = store;
        this.minutes = minutes;
        queue = store.Queue(DateTime.Now);

        Title = "Карточки";
        Icon = Icons.Clock(Palette.Blue);
        Width = 620;
        Height = 720;
        MinWidth = 460;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.B(Palette.Bg);

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(new StackPanel { Spacing = 2, Children = { Card.Text("Карточки", 26, Palette.Text, FontWeight.Bold), subtitle } });
        Grid.SetColumn(modeButton, 1);
        header.Children.Add(modeButton);
        modeButton.Click += (_, _) => SetMode(body.Content == reviewPane ? listPane : reviewPane);

        timerBar = new Border
        {
            Background = Palette.B(Palette.Card), BorderBrush = Palette.B(Palette.Blue), BorderThickness = new Thickness(4, 0, 0, 0),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 8), Margin = new Thickness(0, 14, 0, 0),
            IsVisible = minutes != null, Child = timerText
        };

        reviewPane = BuildReview();
        listPane = BuildList();

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*"), Margin = new Thickness(20, 18, 20, 16) };
        root.Children.Add(header);
        Grid.SetRow(timerBar, 1);
        root.Children.Add(timerBar);
        Grid.SetRow(body, 2);
        body.Margin = new Thickness(0, 14, 0, 0);
        root.Children.Add(body);
        Content = root;

        timer.Tick += (_, _) => UpdateTimer();
        if (minutes != null) timer.Start();
        UpdateTimer();

        // 1–4 — оценки, Enter/пробел — показать ответ (когда фокус не в поле ввода)
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (body.Content != reviewPane || current == null) return;
            if (!shown && e.Key == Key.Enter) { e.Handled = true; Show(); return; }
            if (!shown && e.Key == Key.Space && !answer.IsFocused) { e.Handled = true; Show(); return; }
            if (shown && e.Key is >= Key.D1 and <= Key.D4) { e.Handled = true; Rate((Grade)(e.Key - Key.D1)); }
            if (shown && e.Key is >= Key.NumPad1 and <= Key.NumPad4) { e.Handled = true; Rate((Grade)(e.Key - Key.NumPad1)); }
        }, RoutingStrategies.Tunnel);

        store.Changed += OnStoreChanged;
        Closed += (_, _) =>
        {
            timer.Stop();
            store.Changed -= OnStoreChanged;
            Finished?.Invoke(reviewed, reviewed > 0 ? Math.Max(1, (int)Math.Round((DateTime.Now - started).TotalMinutes)) : 0);
        };

        SetMode(queue.Count > 0 || minutes != null ? reviewPane : listPane);
        NextCard();
    }

    void SetMode(Control pane)
    {
        body.Content = pane;
        modeButton.Content = pane == reviewPane ? "Все карточки" : "Повторение";
        if (pane == listPane) RebuildList();
        UpdateSubtitle();
        if (pane == reviewPane && !shown) Dispatcher.UIThread.Post(() => answer.Focus());
    }

    /// <summary>Карточки добавились или изменились (например, помощник выписал новые) — обновить окно.</summary>
    void OnStoreChanged()
    {
        if (body.Content == listPane) RebuildList();
        if (current == null && !TimeIsUp)
        {
            // на экране «Готово» / «повторять нечего» — подхватить новые карточки
            queue.AddRange(store.Queue(DateTime.Now).Where(c => !queue.Contains(c)));
            if (queue.Count > 0) NextCard();
        }
        UpdateSubtitle();
    }

    void UpdateSubtitle()
    {
        int due = store.DueCount(DateTime.Now);
        subtitle.Text = $"К повторению: {due} · всего {store.All.Count}" + (reviewed > 0 ? $" · сейчас повторено {reviewed}" : "");
    }

    void UpdateTimer()
    {
        if (minutes == null) return;
        var left = started.AddMinutes(minutes.Value) - DateTime.Now;
        timerText.Text = left > TimeSpan.Zero ? $"Повторение · осталось {Format.Ms(left)}" : "Время вышло — эта карточка последняя";
        timerText.Foreground = Palette.B(left > TimeSpan.Zero ? Palette.Text : Palette.Green);
    }

    bool TimeIsUp => minutes != null && DateTime.Now >= started.AddMinutes(minutes.Value);

    // ---------- повторение ----------

    Control BuildReview()
    {
        showButton.Click += (_, _) => Show();
        answer.KeyDown += (_, e) => { if (e.Key == Key.Enter && !shown) { e.Handled = true; Show(); } };

        var labels = new[] { ("Не помню", Palette.Red), ("Трудно", Palette.Amber), ("Помню", Palette.Green), ("Легко", Palette.Blue) };
        for (int i = 0; i < 4; i++)
        {
            var g = (Grade)i;
            var b = new Button { Classes = { "flat" }, Background = Palette.B(labels[i].Item2), Tag = g };
            AutomationProperties.SetName(b, labels[i].Item1);   // у кнопки составное содержимое — имя для экранного диктора
            b.Click += (_, _) => Rate(g);
            Grid.SetColumn(b, i * 2);
            grades.Children.Add(b);
        }

        answerPart.Children.Add(new Border { Height = 1, Background = Palette.B(Palette.Gray), Margin = new Thickness(0, 4) });
        answerPart.Children.Add(back);
        answerPart.Children.Add(match);
        answerPart.Children.Add(note);
        answerPart.Children.Add(grades);
        answerPart.Children.Add(Card.Text("Клавиши 1–4 — оценка.", 12, Palette.Muted));

        var cardBox = new Border
        {
            Background = Palette.B(Palette.Card), CornerRadius = new CornerRadius(8), Padding = new Thickness(22, 18),
            Child = new StackPanel { Spacing = 12, Children = { front, answer, showButton, answerPart } }
        };
        cardPart.Children.Add(cardBox);

        var close = new Button { Content = "Закрыть", Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(18, 8) };
        close.Click += (_, _) => Close();
        donePart.Children.Add(close);

        return new ScrollViewer { Content = new StackPanel { Spacing = 12, Children = { cardPart, donePart } } };
    }

    void NextCard()
    {
        shown = false;
        current = TimeIsUp ? null : queue.FirstOrDefault();
        if (current != null) queue.RemoveAt(0);
        cardPart.IsVisible = current != null;
        donePart.IsVisible = current == null;
        if (current == null)
        {
            ShowDone();
            return;
        }
        front.Text = current.Front;
        answer.Text = "";
        answer.IsEnabled = true;
        showButton.IsVisible = true;
        answerPart.IsVisible = false;
        UpdateSubtitle();
        if (body.Content == reviewPane) Dispatcher.UIThread.Post(() => answer.Focus());
    }

    void ShowDone()
    {
        while (donePart.Children.Count > 1) donePart.Children.RemoveAt(0);
        var now = DateTime.Now;
        var next = store.All.Where(c => c.Due > now).OrderBy(c => c.Due).FirstOrDefault();
        string title = reviewed > 0 ? "Готово!" : store.All.Count == 0 ? "Карточек пока нет" : "На сейчас повторять нечего";
        string text = reviewed > 0
            ? $"Повторено: {GreetingWindow.Plural(reviewed, "карточка", "карточки", "карточек")}."
            : store.All.Count == 0
                ? "Помощник сам выпишет карточки из разговоров и занятий: твои ошибки и новые слова. Можно попросить в чате «сделай карточки по Unité 5» или добавить свои в «Все карточки»."
                : "Все карточки повторены вовремя.";
        if (next != null && store.All.Count > 0)
            text += next.Due.Date == now.Date ? $" Следующие — сегодня в {next.Due:HH:mm}." : next.Due.Date == now.Date.AddDays(1) ? " Следующие — завтра." : $" Следующие — {next.Due:d MMMM}.";
        donePart.Children.Insert(0, Card.Text(title, 22, Palette.Text, FontWeight.Bold));
        donePart.Children.Insert(1, Card.Text(text, 14, Palette.Text));
        UpdateSubtitle();
    }

    void Show()
    {
        if (current == null || shown) return;
        shown = true;
        back.Text = current.Back;
        note.Text = current.Note;
        note.IsVisible = current.Note.Length > 0;
        string typed = (answer.Text ?? "").Trim();
        match.IsVisible = typed.Length > 0;
        if (typed.Length > 0)
        {
            var m = CardStore.Compare(typed, current.Back);
            (match.Text, match.Foreground) = m switch
            {
                AnswerMatch.Exact => ("✓ Верно", Palette.B(Palette.Green)),
                AnswerMatch.AlmostAccents => ("Почти — проверь диакритику (é, è, à, ç…)", Palette.B(Palette.Amber)),
                _ => ("Твой ответ: " + typed, Palette.B(Palette.Muted))
            };
        }
        foreach (var b in grades.Children.OfType<Button>())
        {
            var g = (Grade)b.Tag!;
            b.Content = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new TextBlock { Text = g switch { Grade.Again => "Не помню", Grade.Hard => "Трудно", Grade.Good => "Помню", _ => "Легко" }, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = Scheduler.Label(Scheduler.NextInterval(current, g)), FontSize = 11.5, FontWeight = FontWeight.Normal, Opacity = 0.85, HorizontalAlignment = HorizontalAlignment.Center }
                }
            };
        }
        answer.IsEnabled = false;
        showButton.IsVisible = false;
        answerPart.IsVisible = true;
        Focus();
    }

    void Rate(Grade g)
    {
        if (current == null || !shown) return;
        store.Review(current, g, DateTime.Now);
        reviewed++;
        if (g == Grade.Again) queue.Add(current);   // забытое — ещё раз в конце этого же захода
        NextCard();
    }

    // ---------- список ----------

    Control BuildList()
    {
        var add = new Button { Content = "Добавить", Classes = { "accent" }, HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddCard();
        foreach (var tb in new[] { newFront, newBack, newNote })
            tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; AddCard(); } };
        search.TextChanged += (_, _) => RebuildList();

        var form = new Border
        {
            Background = Palette.B(Palette.Card), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 12),
            Child = new StackPanel { Spacing = 8, Children = { Card.Text("Своя карточка", 13, Palette.Muted), newFront, newBack, newNote, add } }
        };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        grid.Children.Add(form);
        Grid.SetRow(search, 1);
        search.Margin = new Thickness(0, 12, 0, 8);
        grid.Children.Add(search);
        var scroll = new ScrollViewer { Content = rows };
        Grid.SetRow(scroll, 2);
        grid.Children.Add(scroll);
        return grid;
    }

    void AddCard()
    {
        string f = (newFront.Text ?? "").Trim(), b = (newBack.Text ?? "").Trim();
        if (f.Length == 0) { newFront.Focus(); return; }
        if (b.Length == 0) { newBack.Focus(); return; }
        if (store.Add(new[] { (f, b, newNote.Text ?? "") }, DateTime.Now) == 0)
        {
            newFront.Text = f + "  ← уже есть";
            return;
        }
        newFront.Text = newBack.Text = newNote.Text = "";
        RebuildList();
        newFront.Focus();
    }

    void RebuildList()
    {
        rows.Children.Clear();
        var now = DateTime.Now;
        string q = (search.Text ?? "").Trim();
        var items = store.All
            .Where(c => q.Length == 0 || c.Front.Contains(q, StringComparison.OrdinalIgnoreCase) || c.Back.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Due).ToList();
        if (items.Count == 0)
        {
            rows.Children.Add(Card.Text(store.All.Count == 0 ? "Карточек пока нет." : "Ничего не нашлось.", 14, Palette.Muted));
            return;
        }
        foreach (var c in items)
        {
            string due = c.IsNew ? "новая" : c.Due <= now ? "пора повторить" : c.Due.Date == now.Date ? "сегодня в " + c.Due.ToString("HH:mm")
                : "через " + Scheduler.Label((c.Due.Date - now.Date).TotalDays);
            var text = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    Card.Text(c.Front, 14.5, Palette.Text, FontWeight.SemiBold),
                    Card.Text(c.Back, 14, Palette.Green),
                    Card.Text(due + (c.Lapses > 0 ? $" · забывал {c.Lapses}" : "") + (c.Note.Length > 0 ? " · " + c.Note : ""), 12, Palette.Muted)
                }
            };
            var del = new Button
            {
                Content = "✕", Background = Brushes.Transparent, Foreground = Palette.B(Palette.Muted),
                Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Top
            };
            ToolTip.SetTip(del, "Удалить карточку");
            string id = c.Id;
            del.Click += (_, _) => { store.Remove(id); RebuildList(); };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(text);
            Grid.SetColumn(del, 1);
            row.Children.Add(del);
            rows.Children.Add(new Border
            {
                Background = Palette.B(Palette.Card), BorderBrush = Palette.B(c.Due <= now ? Palette.Blue : Palette.Gray),
                BorderThickness = new Thickness(4, 0, 0, 0), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 8), Child = row
            });
        }
    }
}
