using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>
/// Ряд привычек одним кликом: «💧 Вода 3/8» — щелчок +1; правая кнопка — «−1», «Не сегодня», «Настроить».
/// </summary>
public sealed class HabitStrip : UserControl
{
    readonly HabitStore store;
    readonly Action<Habit> marked;
    readonly Action openSettings;
    readonly WrapPanel panel = new();

    public HabitStrip(HabitStore store, Action<Habit> marked, Action openSettings)
    {
        this.store = store;
        this.marked = marked;
        this.openSettings = openSettings;
        Content = panel;
        store.Changed += Rebuild;
        DetachedFromVisualTree += (_, _) => store.Changed -= Rebuild;
        Rebuild();
    }

    void Rebuild()
    {
        panel.Children.Clear();
        var today = DateTime.Today;
        foreach (var h in store.All)
        {
            int n = store.Count(h.Id, today);
            bool done = n >= h.Target, skipped = !done && store.IsSkipped(h.Id, today);
            string count = h.Target > 1 ? $"{n}/{h.Target}" : done ? "✓" : "";
            var chip = new Button
            {
                Content = $"{h.Label}  {count}".TrimEnd(), FontSize = 13.5, Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 8, 8),
                Background = Palette.B(done ? Palette.Green : Palette.Gray), Foreground = Palette.B(Palette.Text),
                Opacity = skipped ? 0.5 : 1, CornerRadius = new CornerRadius(14)
            };
            int streak = store.Streak(h, today);
            ToolTip.SetTip(chip, (done ? "Сегодня выполнено" : skipped ? "Сегодня пропускаешь" : "Щелчок — отметить")
                                 + (streak > 1 ? $" · серия {GreetingWindow.Plural(streak, "день", "дня", "дней")}" : "")
                                 + "\nПравая кнопка — «−1», «Не сегодня», настройка");
            var habit = h;
            chip.Click += (_, _) =>
            {
                store.Mark(habit.Id, DateTime.Today);
                marked(habit);
            };
            var minus = new MenuItem { Header = "−1", IsEnabled = n > 0 };
            minus.Click += (_, _) => store.Mark(habit.Id, DateTime.Today, -1);
            var skip = new MenuItem { Header = "Не сегодня", IsEnabled = !done };
            skip.Click += (_, _) => store.Skip(habit.Id, DateTime.Today);
            var setup = new MenuItem { Header = "Настроить привычки…" };
            setup.Click += (_, _) => openSettings();
            chip.ContextMenu = new ContextMenu { Items = { minus, skip, setup } };
            panel.Children.Add(chip);
        }
        var add = new Button
        {
            Content = store.All.Count == 0 ? "＋ Добавить привычку (вода, прогулка, чтение…)" : "＋",
            FontSize = 13.5, Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 8, 8), CornerRadius = new CornerRadius(14),
            Background = Brushes.Transparent, Foreground = Palette.B(Palette.Muted), BorderBrush = Palette.B(Palette.Gray), BorderThickness = new Thickness(1)
        };
        ToolTip.SetTip(add, "Настроить привычки");
        add.Click += (_, _) => openSettings();
        panel.Children.Add(add);
    }
}

/// <summary>Настройка привычек: список, заготовки, добавление и изменение.</summary>
public sealed class HabitsWindow : Window
{
    readonly HabitStore store;
    readonly StackPanel list = new() { Spacing = 6 };
    readonly WrapPanel presets = new();
    readonly TextBox emoji = new() { Width = 64, MaxLength = 4, Text = "✅", HorizontalContentAlignment = HorizontalAlignment.Center };
    readonly TextBox title = new() { Watermark = "Название: вода, прогулка, чтение…", MaxLength = 60 };
    readonly NumericUpDown target = new() { Minimum = 1, Maximum = 50, Value = 1, Width = 130, FormatString = "0" };
    readonly ComboBox remind = new() { ItemsSource = new[] { "Без напоминаний", "В заданное время", "Каждые N минут в рабочий день" }, SelectedIndex = 0, Width = 290 };
    readonly TimePicker at = new() { ClockIdentifier = "24HourClock", MinuteIncrement = 5, SelectedTime = TimeSpan.FromHours(13) };
    readonly NumericUpDown every = new() { Minimum = 10, Maximum = 480, Increment = 15, Value = 90, Width = 130, FormatString = "0" };
    readonly Button save = new() { Content = "Добавить", Classes = { "accent" } };
    readonly Button cancelEdit = new() { Content = "Отмена", IsVisible = false };
    readonly TextBlock formTitle = Card.Text("Новая привычка", 13, Palette.Muted);
    Habit? editing;

    public HabitsWindow(HabitStore store)
    {
        this.store = store;
        Title = "Привычки";
        Icon = Icons.Clock(Palette.Green);
        Width = 620;
        Height = 700;
        MinWidth = 520;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.B(Palette.Bg);

        remind.SelectionChanged += (_, _) => UpdateForm();
        save.Click += (_, _) => Save();
        cancelEdit.Click += (_, _) => ResetForm();
        title.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Save(); } };

        Grid Row(string label, Control c)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), Margin = new Thickness(0, 3) };
            g.Children.Add(Card.Text(label, 13.5, Palette.Text).WithMargin(0, 6, 0, 0));
            g.Children.Add(c.WithColumn(1));
            return g;
        }
        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        nameRow.Children.Add(emoji);
        title.Margin = new Thickness(8, 0, 0, 0);
        nameRow.Children.Add(title.WithColumn(1));
        target.HorizontalAlignment = every.HorizontalAlignment = at.HorizontalAlignment = HorizontalAlignment.Left;
        var form = new Border
        {
            Background = Palette.B(Palette.Card), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 12),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    formTitle, nameRow,
                    Row("Раз в день", target),
                    Row("Напоминать", remind),
                    Row("Во сколько", at),
                    Row("Каждые, мин", every),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0), Children = { save, cancelEdit } }
                }
            }
        };

        var root = new StackPanel { Margin = new Thickness(20, 18, 20, 16), Spacing = 12 };
        root.Children.Add(Card.Text("Привычки", 26, Palette.Text, FontWeight.Bold));
        root.Children.Add(Card.Text("Отмечай одним щелчком в окне «Сегодня». Напоминаю только в удобный момент: не на встрече и не во время созвона.", 13, Palette.Muted));
        root.Children.Add(Card.Section("Быстро добавить", presets));
        root.Children.Add(list);
        root.Children.Add(form);
        Content = new ScrollViewer { Content = root };

        store.Changed += Rebuild;
        Closed += (_, _) => store.Changed -= Rebuild;
        ResetForm();
        Rebuild();
    }

    void Rebuild()
    {
        presets.Children.Clear();
        foreach (var p in HabitStore.Presets().Where(p => store.All.All(h => !h.Title.Equals(p.Title, StringComparison.OrdinalIgnoreCase))))
        {
            var b = new Button { Content = "＋ " + p.Label, Margin = new Thickness(0, 4, 8, 0) };
            ToolTip.SetTip(b, Describe(p));
            var preset = p;
            b.Click += (_, _) => store.Add(new Habit
            {
                Emoji = preset.Emoji, Title = preset.Title, Target = preset.Target, Remind = preset.Remind,
                RemindAt = preset.RemindAt, EveryMin = preset.EveryMin, Created = DateTime.Now
            });
            presets.Children.Add(b);
        }
        if (presets.Children.Count == 0) presets.Children.Add(Card.Text("Все заготовки уже добавлены.", 13, Palette.Muted));

        list.Children.Clear();
        foreach (var h in store.All)
        {
            int streak = store.Streak(h, DateTime.Today);
            var text = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    Card.Text(h.Label, 15, Palette.Text, FontWeight.SemiBold),
                    Card.Text(Describe(h) + (streak > 1 ? $" · серия {streak} дн." : ""), 12.5, Palette.Muted)
                }
            };
            var edit = new Button { Content = "Изменить", VerticalAlignment = VerticalAlignment.Center };
            var habit = h;
            edit.Click += (_, _) => Edit(habit);
            var del = new Button { Content = "✕", Background = Brushes.Transparent, Foreground = Palette.B(Palette.Muted), Padding = new Thickness(8, 0), VerticalAlignment = VerticalAlignment.Center };
            ToolTip.SetTip(del, "Удалить привычку (вместе с отметками)");
            del.Click += async (_, _) => { if (await ConfirmDialog.Ask(this, $"Удалить «{habit.Label}» вместе со всеми отметками?")) store.Remove(habit.Id); };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
            row.Children.Add(text);
            row.Children.Add(edit.WithColumn(1));
            row.Children.Add(del.WithColumn(2));
            list.Children.Add(new Border
            {
                Background = Palette.B(Palette.Card), BorderBrush = Palette.B(Palette.Green), BorderThickness = new Thickness(4, 0, 0, 0),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 8), Child = row
            });
        }
    }

    static string Describe(Habit h) =>
        (h.Target > 1 ? $"{h.Target} раз в день" : "раз в день") + " · " + h.Remind switch
        {
            HabitRemind.AtTime => $"напомню в {TimeSpan.FromMinutes(h.RemindAt):hh\\:mm}",
            HabitRemind.Every => $"напоминаю каждые {h.EveryMin} мин в рабочий день",
            _ => "без напоминаний"
        };

    void Edit(Habit h)
    {
        editing = h;
        formTitle.Text = "Изменить: " + h.Label;
        emoji.Text = h.Emoji;
        title.Text = h.Title;
        target.Value = h.Target;
        remind.SelectedIndex = (int)h.Remind;
        at.SelectedTime = TimeSpan.FromMinutes(h.RemindAt);
        every.Value = h.EveryMin;
        save.Content = "Сохранить";
        cancelEdit.IsVisible = true;
        UpdateForm();
        title.Focus();
    }

    void ResetForm()
    {
        editing = null;
        formTitle.Text = "Новая привычка";
        emoji.Text = "✅";
        title.Text = "";
        target.Value = 1;
        remind.SelectedIndex = 0;
        save.Content = "Добавить";
        cancelEdit.IsVisible = false;
        UpdateForm();
    }

    void UpdateForm()
    {
        at.IsEnabled = remind.SelectedIndex == (int)HabitRemind.AtTime;
        every.IsEnabled = remind.SelectedIndex == (int)HabitRemind.Every;
    }

    void Save()
    {
        var name = (title.Text ?? "").Trim();
        if (name.Length == 0) { title.Focus(); return; }
        var h = editing ?? new Habit { Created = DateTime.Now };
        h.Emoji = string.IsNullOrWhiteSpace(emoji.Text) ? "✅" : emoji.Text.Trim();
        h.Title = name;
        h.Target = target.Value is decimal t ? (int)t : 1;
        h.Remind = (HabitRemind)Math.Max(0, remind.SelectedIndex);
        h.RemindAt = at.SelectedTime is TimeSpan ts ? (int)ts.TotalMinutes : 13 * 60;
        h.EveryMin = every.Value is decimal e ? (int)e : 90;
        if (editing != null) store.Update(h); else store.Add(h);
        ResetForm();
    }
}

public enum HabitChoice { Done, Later, Skip }

/// <summary>Напоминание о привычке в углу: «💧 Вода · 3 из 8» — «Сделал» / «Позже» / «Не сегодня». Без ответа 3 минуты — «позже».</summary>
public sealed class HabitAlertWindow : Window
{
    bool answered;

    public event Action<HabitChoice>? Answered;

    public HabitAlertWindow(Habit h, int count, int streak, AppSettings s, IPlatform platform)
    {
        PopupDrag.Enable(this, "habit");
        Title = h.Label;
        Icon = Icons.Clock(Palette.Green);
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = true;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Palette.B(Palette.Green);
        Opacity = 0;

        var done = new Button { Content = h.Target > 1 ? "+1  Сделал" : "Сделал", Classes = { "flat" }, Background = Palette.B(Palette.Green) };
        var later = new Button { Content = "Позже", Classes = { "flat" }, Background = Palette.B(Palette.Gray) };
        var skip = new Button { Content = "Не сегодня", Classes = { "flat" }, Background = Palette.B(Palette.Gray) };
        ToolTip.SetTip(later, $"Напомню через {(int)HabitCoach.SnoozeTime.TotalMinutes} минут");
        done.Click += (_, _) => Answer(HabitChoice.Done);
        later.Click += (_, _) => Answer(HabitChoice.Later);
        skip.Click += (_, _) => Answer(HabitChoice.Skip);
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("4*,8,3*,8,3*"), Height = 40, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(done);
        buttons.Children.Add(later.WithColumn(2));
        buttons.Children.Add(skip.WithColumn(4));

        string sub = (h.Target > 1 ? $"{count} из {h.Target} сегодня" : "сегодня ещё не отмечено")
                     + (streak > 1 ? $" · серия {GreetingWindow.Plural(streak, "день", "дня", "дней")}" : "");
        Content = new Border
        {
            Padding = new Thickness(5),
            Background = Palette.B(Palette.Green),
            Child = new Border
            {
                Background = Palette.B(Palette.Card), Width = 400, Padding = new Thickness(22, 14, 22, 18),
                Child = new StackPanel
                {
                    Spacing = 2,
                    Children = { Card.Text("Привычка", 13, Palette.Muted), Card.Text(h.Label, 21, Palette.Text, FontWeight.Bold), Card.Text(sub, 13.5, Palette.Text), buttons }
                }
            }
        };

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Answer(HabitChoice.Later);
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
                Position = PopupDrag.Place(this, "habit", new PixelPoint(wa.Right - (int)((Bounds.Width + 16) * k), y));
            }
            Opacity = 1;
            if (s.SoundOn) platform.PlayWav(Synth.Melody("soft", s.Volume / 100.0));
            DispatcherTimer.RunOnce(() => Answer(HabitChoice.Later), TimeSpan.FromMinutes(3));
        };
        Closed += (_, _) => Answer(HabitChoice.Later);
    }

    void Answer(HabitChoice c)
    {
        if (answered) return;
        answered = true;
        Answered?.Invoke(c);
        Close();
    }
}
