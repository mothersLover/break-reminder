using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>
/// Вкладка «Учёба» в статистике: минуты по дням, серия, карточки, частые ошибки, цели со сроком, итоги недели.
/// </summary>
public sealed class LearningPanel : UserControl
{
    readonly Func<LearningStats> stats;
    readonly GoalStore goals;
    readonly WeeklyReviewStore reviews;
    readonly Func<Task<WeeklyReview?>>? makeReview;   // null — помощник выключен
    readonly Action<WeeklyReview> showReview;

    readonly StackPanel body = new() { Spacing = 12, Margin = new Thickness(0, 4, 8, 4) };
    readonly TextBox goalTitle = new() { Watermark = "Цель, например: закончить Unité 6", MaxLength = 200 };
    readonly CalendarDatePicker goalDate = new() { SelectedDate = DateTime.Today.AddDays(30), Width = 150 };
    readonly TextBlock reviewStatus = Card.Text("", 12.5, Palette.Muted);

    readonly string? hobby;

    /// <param name="hobby">Название хобби, по которому статистика (карточки, цели, итоги — у каждого хобби свои).</param>
    public LearningPanel(Func<LearningStats> stats, GoalStore goals, WeeklyReviewStore reviews,
        Func<Task<WeeklyReview?>>? makeReview, Action<WeeklyReview> showReview, string? hobby = null)
    {
        this.hobby = hobby;
        this.stats = stats;
        this.goals = goals;
        this.reviews = reviews;
        this.makeReview = makeReview;
        this.showReview = showReview;
        Content = new ScrollViewer { Content = body };
        goals.Changed += Rebuild;
        DetachedFromVisualTree += (_, _) => goals.Changed -= Rebuild;
        goalTitle.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; AddGoal(); } };
        Rebuild();
    }

    void Rebuild()
    {
        var s = stats();
        body.Children.Clear();
        if (hobby != null)
            body.Children.Add(Card.Text($"Хобби: {hobby} — карточки, цели и итоги этого хобби; минуты и серия — по всем. " +
                                        "Хобби переключается в окне помощника.", 12.5, Palette.Muted));

        // ---- плитки ----
        var tiles = new UniformGrid { Columns = 4 };
        tiles.Children.Add(Tile("Занятия за неделю", Minutes(s.WeekMinutes), $"неделей раньше — {Minutes(s.PrevWeekMinutes)}", new Thickness(0, 0, 5, 0)));
        tiles.Children.Add(Tile("Серия", GreetingWindow.Plural(s.Streak, "день", "дня", "дней"), s.BestStreak > 0 ? $"лучшая — {s.BestStreak}" : null, new Thickness(5, 0)));
        tiles.Children.Add(Tile("Выучено карточек", $"{s.CardsLearned} из {s.CardsTotal}", s.CardsNew > 0 ? $"новых — {s.CardsNew}" : null, new Thickness(5, 0)));
        tiles.Children.Add(Tile("К повторению", s.CardsDue.ToString(), null, new Thickness(5, 0, 0, 0)));
        body.Children.Add(tiles);

        // ---- минуты по дням ----
        body.Children.Add(Card.Section($"Минуты занятий · {LearningStats.Days / 7} недели", Chart(s.MinutesByDay)));

        // ---- цели ----
        var goalList = new StackPanel { Spacing = 6 };
        var today = DateTime.Today;
        foreach (var g in goals.All.OrderBy(g => g.Done).ThenBy(g => g.Deadline))
            goalList.Children.Add(GoalRow(g, today));
        if (goals.All.Count == 0)
            goalList.Children.Add(Card.Text("Целей пока нет. Цель со сроком помогает: помощник видит, сколько осталось, и подстраивает занятия.", 13, Palette.Muted));
        var add = new Button { Content = "Добавить", Classes = { "accent" }, VerticalAlignment = VerticalAlignment.Center };
        add.Click += (_, _) => AddGoal();
        // поле и дата живут между перерисовками (чтобы не терять набранное) — отцепить от прежней строки
        (goalTitle.Parent as Panel)?.Children.Remove(goalTitle);
        (goalDate.Parent as Panel)?.Children.Remove(goalDate);
        var addRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(0, 6, 0, 0) };
        addRow.Children.Add(goalTitle);
        goalDate.Margin = new Thickness(8, 0);
        addRow.Children.Add(goalDate.WithColumn(1));
        addRow.Children.Add(add.WithColumn(2));
        goalList.Children.Add(addRow);
        body.Children.Add(Card.Section("Цели", goalList));

        // ---- частые ошибки ----
        if (s.TopMistakes.Count > 0)
        {
            var list = new StackPanel { Spacing = 3 };
            foreach (var c in s.TopMistakes)
                list.Children.Add(Card.Text($"{c.Front} → {c.Back}   · забывал {c.Lapses}", 13.5, Palette.Text));
            body.Children.Add(Card.Section("Чаще всего забываю", list));
        }

        // ---- итоги недели ----
        var weekly = new StackPanel { Spacing = 6 };
        var latest = reviews.Latest;
        weekly.Children.Add(Card.Text(latest != null
            ? $"Последние — за неделю {latest.WeekStart:dd.MM} – {latest.WeekStart.AddDays(6):dd.MM} (составлены {latest.CreatedAt:d MMMM, HH:mm})."
            : "Помощник подводит итоги по воскресеньям вечером: что получилось, где ошибки, как идут цели, и план на следующую неделю.",
            13, Palette.Text));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (latest != null)
        {
            var open = new Button { Content = "Открыть" };
            open.Click += (_, _) => showReview(latest);
            buttons.Children.Add(open);
        }
        if (makeReview != null)
        {
            var now = new Button { Content = "Подвести итоги сейчас" };
            now.Click += async (_, _) =>
            {
                now.IsEnabled = false;
                reviewStatus.Text = "Помощник подводит итоги…";
                var r = await makeReview();
                reviewStatus.Text = r == null ? "Не получилось — попробуй позже (нет связи или помощник недоступен)." : "";
                now.IsEnabled = true;
                if (r != null) { Rebuild(); showReview(r); }
            };
            buttons.Children.Add(now);
        }
        weekly.Children.Add(buttons);
        (reviewStatus.Parent as Panel)?.Children.Remove(reviewStatus);
        weekly.Children.Add(reviewStatus);
        body.Children.Add(Card.Section("Итоги недели", weekly));
    }

    void AddGoal()
    {
        if (goals.Add(goalTitle.Text ?? "", goalDate.SelectedDate ?? DateTime.Today.AddDays(30), DateTime.Now) == null)
        {
            goalTitle.Focus();
            return;
        }
        goalTitle.Text = "";
    }

    Control GoalRow(Goal g, DateTime today)
    {
        var check = new CheckBox { IsChecked = g.Done, VerticalAlignment = VerticalAlignment.Center };
        string id = g.Id;
        check.IsCheckedChanged += (_, _) => goals.SetDone(id, check.IsChecked == true);
        int left = g.DaysLeft(today);
        string when = g.Done ? "готово" : left > 0 ? $"до {g.Deadline:d MMMM} · осталось {GreetingWindow.Plural(left, "день", "дня", "дней")}"
            : left == 0 ? "срок сегодня" : $"срок прошёл {g.Deadline:d MMMM}";
        var title = Card.Text(g.Title, 14.5, g.Done ? Palette.Muted : Palette.Text, FontWeight.SemiBold);
        if (g.Done) title.TextDecorations = TextDecorations.Strikethrough;
        var text = new StackPanel
        {
            Spacing = 1,
            Children = { title, Card.Text(when, 12, !g.Done && left < 0 ? Palette.Amber : Palette.Muted) }
        };
        var del = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = Palette.B(Palette.Muted), Padding = new Thickness(6, 0) };
        ToolTip.SetTip(del, "Удалить цель");
        del.Click += (_, _) => goals.Remove(id);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        row.Children.Add(check);
        row.Children.Add(text.WithColumn(1));
        row.Children.Add(del.WithColumn(2));
        return row;
    }

    /// <summary>Столбики минут по дням; под понедельниками — даты.</summary>
    static Control Chart(IReadOnlyDictionary<DateTime, int> byDay)
    {
        var days = byDay.OrderBy(kv => kv.Key).ToList();
        int max = Math.Max(15, days.Max(kv => kv.Value));
        var grid = new Grid { Height = 130, Margin = new Thickness(0, 6, 0, 0) };
        for (int i = 0; i < days.Count; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        grid.RowDefinitions = new RowDefinitions("*,Auto");
        for (int i = 0; i < days.Count; i++)
        {
            var (day, min) = (days[i].Key, days[i].Value);
            bool today = day == DateTime.Today;
            var bar = new Border
            {
                Height = min == 0 ? 2 : Math.Max(4, 100.0 * min / max), VerticalAlignment = VerticalAlignment.Bottom,
                Background = Palette.B(min == 0 ? Palette.Gray : today ? Palette.Green : Palette.Blue),
                CornerRadius = new CornerRadius(2), Margin = new Thickness(1.5, 0)
            };
            ToolTip.SetTip(bar, $"{day:ddd, d MMMM}: {(min == 0 ? "не занимался" : Minutes(min))}");
            grid.Children.Add(bar.WithColumn(i));
            if (day.DayOfWeek == DayOfWeek.Monday || today)
            {
                var label = Card.Text(today ? "сегодня" : day.ToString("dd.MM"), 10.5, Palette.Muted);
                label.TextWrapping = TextWrapping.NoWrap;
                Grid.SetRow(label, 1);
                // «сегодня» — у правого края, выровнено вправо, чтобы не обрезалось
                int col = today ? Math.Max(0, i - 3) : i;
                Grid.SetColumnSpan(label, today ? i - col + 1 : Math.Min(4, days.Count - i));
                if (today) label.HorizontalAlignment = HorizontalAlignment.Right;
                grid.Children.Add(label.WithColumn(col));
            }
        }
        return grid;
    }

    static Border Tile(string caption, string value, string? sub, Thickness margin)
    {
        var b = Card.Section(caption, Card.Text(value, 18, Palette.Text, FontWeight.SemiBold));
        if (sub != null) ((StackPanel)b.Child!).Children.Add(Card.Text(sub, 11.5, Palette.Muted));
        b.Margin = margin;
        return b;
    }

    static string Minutes(int m) => m < 60 ? $"{m} мин" : $"{m / 60} ч {m % 60:00} мин";
}

/// <summary>Итоги недели от помощника: разбор и план (план можно закрепить в чате).</summary>
public sealed class WeeklyReviewWindow : Window
{
    public WeeklyReviewWindow(WeeklyReview r, TutorMemory memory, Action openAssistant)
    {
        Title = "Итоги недели";
        Icon = Icons.Clock(Palette.Green);
        Width = 560;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.B(Palette.Bg);

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Spacing = 10 };
        body.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Card.Text("Итоги недели", 24, Palette.Text, FontWeight.Bold),
                Card.Text($"{r.WeekStart:d MMMM} – {r.WeekStart.AddDays(6):d MMMM yyyy}", 13.5, Palette.Muted)
            }
        });
        body.Children.Add(new SelectableTextBlock { Text = r.Text, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Text) });

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,*,10,*"), Height = 42, Margin = new Thickness(0, 6, 0, 0) };
        if (r.Plan.Length > 0)
        {
            body.Children.Add(Card.Section("План на неделю",
                new SelectableTextBlock { Text = r.Plan, FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Text) }));
            var pin = new Button { Content = memory.IsPinned(PlanText(r)) ? "📌 Закреплено" : "📌 Закрепить план", Classes = { "flat" }, Background = Palette.B(Palette.Amber) };
            ToolTip.SetTip(pin, "Закрепить в чате с помощником — он будет учитывать план в каждом ответе");
            pin.Click += (_, _) =>
            {
                memory.Pin(PlanText(r), false, DateTime.Now);
                pin.Content = "📌 Закреплено";
            };
            buttons.Children.Add(pin);
        }
        var chat = new Button { Content = "Обсудить", Classes = { "flat" }, Background = Palette.B(Palette.Blue) };
        ToolTip.SetTip(chat, "Открыть чат с помощником");
        chat.Click += (_, _) => { Close(); openAssistant(); };
        var close = new Button { Content = "Закрыть", Classes = { "flat" }, Background = Palette.B(Palette.Gray) };
        close.Click += (_, _) => Close();
        buttons.Children.Add(chat.WithColumn(2));
        buttons.Children.Add(close.WithColumn(4));
        body.Children.Add(buttons);
        Content = new ScrollViewer { Content = body };
    }

    static string PlanText(WeeklyReview r) => $"План на неделю с {r.WeekStart.AddDays(7):dd.MM}:\n{r.Plan}";
}
