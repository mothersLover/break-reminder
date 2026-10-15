using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>«Сегодня»: встречи из календаря и свои задачи одним списком + добавление задач.</summary>
public sealed class AgendaWindow : Window
{
    readonly TaskStore store;
    readonly Func<IReadOnlyList<Meeting>> meetings;
    readonly Func<string> calendarStatus;
    readonly IPlatform platform;
    readonly bool calendarOn;

    readonly StackPanel list = new() { Spacing = 8 };
    readonly CheckBox showPast = new() { Content = "Показать прошедшие" };
    readonly TextBlock footer = Card.Text("", 12, Palette.Muted);
    readonly TextBox titleBox = new() { Watermark = "Новая задача…", MaxLength = 200 };
    readonly TextBox noteBox = new() { Watermark = "Заметка (необязательно)", MaxLength = 500 };
    readonly CheckBox timedCheck = new() { Content = "Ко времени", VerticalAlignment = VerticalAlignment.Center };
    readonly TimePicker timePicker = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, IsEnabled = false };
    readonly HashSet<string> expanded = new();   // раскрытые описания встреч
    readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMinutes(1) };

    public AgendaWindow(TaskStore store, Func<IReadOnlyList<Meeting>> meetings, bool calendarOn, Func<string> calendarStatus, IPlatform platform)
    {
        this.store = store;
        this.meetings = meetings;
        this.calendarOn = calendarOn;
        this.calendarStatus = calendarStatus;
        this.platform = platform;

        Title = "Сегодня";
        Icon = Icons.Clock(Palette.Green);
        Width = 600;
        Height = 720;
        MinWidth = 480;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.B(Palette.Bg);

        var now = DateTime.Now;
        string date = now.ToString("dddd, d MMMM", Card.Ru);
        var header = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Card.Text("Сегодня", 26, Palette.Text, FontWeight.Bold),
                Card.Text(char.ToUpper(date[0]) + date[1..], 14, Palette.Muted)
            }
        };

        // ---- добавление задачи ----
        var next = now.Date.AddMinutes(Math.Ceiling(now.TimeOfDay.TotalMinutes / 30) * 30 + 30);
        timePicker.SelectedTime = next.TimeOfDay;
        timedCheck.IsCheckedChanged += (_, _) => timePicker.IsEnabled = timedCheck.IsChecked == true;
        var addButton = new Button { Content = "Добавить", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center };
        addButton.Click += (_, _) => AddTask();
        titleBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; AddTask(); } };
        noteBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; AddTask(); } };

        // название / заметка / [ко времени + время ... Добавить]
        noteBox.Margin = new Thickness(0, 8, 0, 0);
        var row3 = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
        row3.Children.Add(timedCheck);
        Grid.SetColumn(timePicker, 1);
        timePicker.Margin = new Thickness(8, 0, 0, 0);
        row3.Children.Add(timePicker);
        Grid.SetColumn(addButton, 3);
        row3.Children.Add(addButton);

        var addCard = new Border
        {
            Background = Palette.B(Palette.Card), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 12),
            Child = new StackPanel { Children = { titleBox, noteBox, row3 } }
        };

        showPast.IsCheckedChanged += (_, _) => Rebuild();
        var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 8, 0, 0) };
        bottom.Children.Add(showPast);
        Grid.SetColumn(footer, 1);
        footer.HorizontalAlignment = HorizontalAlignment.Right;
        footer.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(footer);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(20, 18, 20, 14) };
        root.Children.Add(header);
        Grid.SetRow(addCard, 1);
        addCard.Margin = new Thickness(0, 12, 0, 12);
        root.Children.Add(addCard);
        var scroll = new ScrollViewer { Content = list };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);
        Grid.SetRow(bottom, 3);
        root.Children.Add(bottom);
        Content = root;

        store.Changed += Rebuild;
        refresh.Tick += (_, _) => Rebuild();   // прошедшее тускнеет, текущая встреча подсвечивается
        refresh.Start();
        Closed += (_, _) =>
        {
            store.Changed -= Rebuild;
            refresh.Stop();
        };
        Opened += (_, _) => titleBox.Focus();
        Rebuild();
    }

    void AddTask()
    {
        var title = (titleBox.Text ?? "").Trim();
        if (title.Length == 0) { titleBox.Focus(); return; }
        TimeSpan? time = timedCheck.IsChecked == true ? timePicker.SelectedTime : null;
        store.Add(DateTime.Today, title, time, noteBox.Text ?? "");
        titleBox.Text = noteBox.Text = "";
        titleBox.Focus();
    }

    /// <summary>Перерисовать список.</summary>
    public void Rebuild()
    {
        var now = DateTime.Now;
        list.Children.Clear();

        var todayMeetings = calendarOn
            ? meetings().Where(m => m.Start.Date == now.Date || (m.Start < now.Date.AddDays(1) && m.End > now.Date)).ToList()
            : new List<Meeting>();
        int hiddenPast = todayMeetings.Count(m => m.End <= now);
        if (showPast.IsChecked != true) todayMeetings = todayMeetings.Where(m => m.End > now).ToList();

        var tasks = store.ForDay(now);
        // единый список по времени: встречи и задачи со временем
        var timed = todayMeetings.Select(m => (At: m.Start, Row: (Control)MeetingRow(m, now)))
            .Concat(tasks.Where(t => t.At != null).Select(t => (At: t.At!.Value, Row: (Control)TaskRow(t, now))))
            .OrderBy(x => x.At)
            .ToList();
        foreach (var (_, row) in timed) list.Children.Add(row);

        var untimed = tasks.Where(t => t.At == null).ToList();
        if (untimed.Count > 0)
        {
            list.Children.Add(Card.Text("Без времени", 13, Palette.Muted));
            foreach (var t in untimed) list.Children.Add(TaskRow(t, now));
        }

        if (list.Children.Count == 0)
            list.Children.Add(Card.Text(hiddenPast > 0
                ? "Больше на сегодня ничего нет. Можно добавить задачу выше."
                : "На сегодня ничего нет. Добавь задачу выше — напомню, если укажешь время.", 14, Palette.Muted));

        showPast.IsVisible = calendarOn && (hiddenPast > 0 || showPast.IsChecked == true);
        showPast.Content = hiddenPast > 0 ? $"Показать прошедшие ({hiddenPast})" : "Показать прошедшие";
        footer.Text = calendarOn ? "Календарь: " + (calendarStatus() is { Length: > 0 } st ? st : "загружается…") : "Календарь не подключён";
    }

    // ---------- строки ----------

    static Border RowFrame(Color accent, Control content, double opacity) => new()
    {
        Background = Palette.B(Palette.Card),
        BorderBrush = Palette.B(accent),
        BorderThickness = new Thickness(4, 0, 0, 0),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(12, 10),
        Opacity = opacity,
        Child = content
    };

    static Grid TwoColumns(Control left, Control right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("92,*") };
        g.Children.Add(left);
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        return g;
    }

    Control MeetingRow(Meeting m, DateTime now)
    {
        bool past = m.End <= now, current = m.Start <= now && now < m.End;
        var time = new StackPanel
        {
            Children =
            {
                Card.Text(m.Start.ToString("HH:mm"), 16, Palette.Text, FontWeight.SemiBold),
                Card.Text(m.End.ToString("HH:mm"), 12.5, Palette.Muted)
            }
        };
        var body = new StackPanel { Spacing = 3 };
        body.Children.Add(Card.Text(m.Title, 15, Palette.Text, FontWeight.SemiBold));
        var tags = current ? "идёт сейчас · до " + m.End.ToString("HH:mm") : "встреча";
        if (!string.IsNullOrEmpty(m.Location) && !m.Location.Contains("://")) tags += " · " + m.Location;
        body.Children.Add(Card.Text(tags, 12, current ? Palette.Green : Palette.Muted));

        if (!string.IsNullOrWhiteSpace(m.Description))
        {
            bool open = expanded.Contains(m.Key);
            bool longText = m.Description.Length > 160 || m.Description.Count(c => c == '\n') >= 3;
            var desc = new SelectableTextBlock
            {
                Text = m.Description, FontSize = 13, Foreground = Palette.B(Palette.Muted), TextWrapping = TextWrapping.Wrap,
                MaxLines = open || !longText ? 0 : 3, TextTrimming = TextTrimming.CharacterEllipsis
            };
            body.Children.Add(desc);
            if (longText)
            {
                var more = LinkButton(open ? "Свернуть" : "Подробнее");
                more.Click += (_, _) =>
                {
                    if (!expanded.Add(m.Key)) expanded.Remove(m.Key);
                    Rebuild();
                };
                body.Children.Add(more);
            }
        }

        if (m.JoinUrl != null && !past)
        {
            var join = new Button
            {
                Content = "Подключиться · " + MeetingLinks.ServiceName(m.JoinUrl), Classes = { "flat" },
                Background = Palette.B(Palette.Green), HorizontalAlignment = HorizontalAlignment.Left,
                Height = 32, Padding = new Thickness(14, 0), Margin = new Thickness(0, 4, 0, 0)
            };
            join.Click += (_, _) => platform.OpenPath(m.JoinUrl);
            ToolTip.SetTip(join, m.JoinUrl);
            body.Children.Add(join);
        }
        return RowFrame(current ? Palette.Green : Palette.Blue, TwoColumns(time, body), past ? 0.5 : 1);
    }

    Control TaskRow(LocalTask t, DateTime now)
    {
        var check = new CheckBox { IsChecked = t.Done, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -4, 0, 0) };
        check.IsCheckedChanged += (_, _) => store.SetDone(t.Id, check.IsChecked == true);

        var title = Card.Text(t.Title, 15, t.Done ? Palette.Muted : Palette.Text, FontWeight.SemiBold);
        if (t.Done) title.TextDecorations = TextDecorations.Strikethrough;
        var body = new StackPanel { Spacing = 3 };
        body.Children.Add(title);
        string tag = t.Date < now.Date ? $"задача · перенесена с {t.Date:dd.MM}" : "задача";
        bool overdue = !t.Done && t.At is { } at && at < now;
        if (overdue) tag += " · время прошло";
        body.Children.Add(Card.Text(tag, 12, overdue ? Palette.Amber : Palette.Muted));
        if (!string.IsNullOrWhiteSpace(t.Note)) body.Children.Add(Card.Text(t.Note, 13, Palette.Muted));

        var delete = new Button
        {
            Content = "✕", Background = Brushes.Transparent, Foreground = Palette.B(Palette.Muted),
            Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Top
        };
        ToolTip.SetTip(delete, "Удалить задачу");
        delete.Click += (_, _) => store.Remove(t.Id);

        var right = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        right.Children.Add(check);
        Grid.SetColumn(body, 1);
        right.Children.Add(body);
        Grid.SetColumn(delete, 2);
        right.Children.Add(delete);

        Control left = t.At is { } when
            ? Card.Text(when.ToString("HH:mm"), 16, t.Done ? Palette.Muted : Palette.Text, FontWeight.SemiBold)
            : Card.Text("—", 16, Palette.Muted);
        return RowFrame(Palette.Purple, TwoColumns(left, right), t.Done ? 0.6 : 1);
    }

    static Button LinkButton(string text) => new()
    {
        Content = text, Classes = { "link" }, HorizontalAlignment = HorizontalAlignment.Left
    };
}

/// <summary>Напоминание о своей задаче: «Через 5 мин» / «Пора», кнопки «Готово» и «Понял».</summary>
public sealed class TaskAlertWindow : Window
{
    static TaskAlertWindow? current;

    public static void Show(LocalTask t, AppSettings s, IPlatform platform, TaskStore store)
    {
        current?.Close();
        current = new TaskAlertWindow(t, s, platform, store);
        current.Show();
    }

    TaskAlertWindow(LocalTask t, AppSettings s, IPlatform platform, TaskStore store)
    {
        Title = "Задача: " + t.Title;
        Icon = Icons.Clock(Palette.Purple);
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Palette.B(Palette.Purple);
        Opacity = 0;

        int minutes = (int)Math.Round(((t.At ?? DateTime.Now) - DateTime.Now).TotalMinutes);
        string when = minutes >= 1 ? $"Через {minutes} мин · задача" : "Пора · задача";
        var done = new Button { Content = "Готово", Classes = { "flat" }, Background = Palette.B(Palette.Green) };
        var ok = new Button { Content = "Понял", Classes = { "flat" }, Background = Palette.B(Palette.Gray) };
        done.Click += (_, _) => { store.SetDone(t.Id, true); Close(); };
        ok.Click += (_, _) => Close();
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*"), Height = 40, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(done);
        Grid.SetColumn(ok, 2);
        buttons.Children.Add(ok);

        var body = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Card.Text(when, 13, Palette.Muted),
                new TextBlock
                {
                    Text = t.Title, FontSize = 21, FontWeight = FontWeight.Bold, Foreground = Palette.B(Palette.Text),
                    TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis
                },
                Card.Text(t.At?.ToString("HH:mm") ?? "", 13, Palette.Muted)
            }
        };
        if (!string.IsNullOrWhiteSpace(t.Note)) body.Children.Add(Card.Text(t.Note, 13.5, Palette.Text));
        body.Children.Add(buttons);

        Content = new Border
        {
            Padding = new Thickness(5),
            Background = Palette.B(Palette.Purple),
            Child = new Border { Background = Palette.B(Palette.Card), Width = 400, Padding = new Thickness(22, 14, 22, 18), Child = body }
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
                // правый верхний угол; если там уже напоминание о встрече — чуть ниже
                int y = wa.Y + (int)(16 * k) + (MeetingWindow.IsOpen ? (int)(210 * k) : 0);
                Position = new PixelPoint(wa.Right - (int)((Bounds.Width + 16) * k), y);
            }
            Opacity = 1;
            if (s.SoundOn) platform.PlayWav(Synth.Melody("soft", s.Volume / 100.0));
            platform.FlashWindow(this.Handle(), true);
        };
        Closed += (_, _) =>
        {
            platform.FlashWindow(this.Handle(), false);
            if (current == this) current = null;
        };
    }
}
