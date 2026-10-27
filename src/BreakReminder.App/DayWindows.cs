using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

static class Card
{
    public static TextBlock Text(string text, double size, Color color, FontWeight weight = FontWeight.Normal) => new()
    {
        Text = text, FontSize = size, FontWeight = weight, Foreground = Palette.B(color), TextWrapping = TextWrapping.Wrap
    };

    /// <summary>Блок с заголовком на тёмной карточке.</summary>
    public static Border Section(string caption, params Control[] content)
    {
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(Text(caption, 12, Palette.Muted));
        panel.Children.AddRange(content);
        return new Border { Background = Palette.B(Palette.Card), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10), Child = panel };
    }

    public static readonly CultureInfo Ru = new("ru-RU");
}

/// <summary>20-20-20: «Посмотри вдаль» на N секунд в углу. Без кнопок, не забирает фокус, пропадает само.</summary>
public sealed class EyeRestWindow : Window
{
    static EyeRestWindow? current;

    public static void Show(int seconds)
    {
        current?.Close();
        current = new EyeRestWindow(seconds);
        current.Show();
    }

    EyeRestWindow(int seconds)
    {
        Title = "Отдых для глаз";
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        CanResize = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Palette.B(Palette.Blue);
        Opacity = 0;

        var count = new TextBlock { FontSize = 30, FontWeight = FontWeight.Light, Foreground = Palette.B(Palette.Text), VerticalAlignment = VerticalAlignment.Center };
        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 3, Foreground = Palette.B(Palette.Blue), Margin = new Thickness(0, 8, 0, 0) };
        var text = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Card.Text("Посмотри вдаль", 18, Palette.Text, FontWeight.Bold),
                Card.Text("На что-нибудь дальше 6 метров — окно, дальнюю стену. Глаза отдохнут.", 13, Palette.Muted)
            }
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(text);
        Grid.SetColumn(count, 1);
        count.Margin = new Thickness(16, 0, 0, 0);
        grid.Children.Add(count);

        Content = new Border
        {
            Padding = new Thickness(4),
            Background = Palette.B(Palette.Blue),
            Child = new Border
            {
                Background = Palette.B(Palette.Card), Width = 360, Padding = new Thickness(18, 12, 18, 12),
                Child = new StackPanel { Children = { grid, bar } }
            }
        };

        var start = DateTime.Now;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) =>
        {
            double left = seconds - (DateTime.Now - start).TotalSeconds;
            if (left <= 0)
            {
                timer.Stop();
                Close();
                return;
            }
            count.Text = ((int)Math.Ceiling(left)).ToString();
            bar.Value = 1 - left / seconds;
        };
        PopupDrag.Enable(this, "eyes", onClick: Close);   // щелчок — закрыть раньше, потянуть — подвинуть
        Opened += (_, _) =>
        {
            var screen = Screens.Primary;
            if (screen != null)
            {
                var wa = screen.WorkingArea;
                double k = screen.Scaling;
                Position = PopupDrag.Place(this, "eyes", new PixelPoint(wa.Right - (int)((Bounds.Width + 16) * k), wa.Bottom - (int)((Bounds.Height + 16) * k)));
            }
            Opacity = 1;
            timer.Start();
        };
        Closed += (_, _) =>
        {
            timer.Stop();
            if (current == this) current = null;
        };
    }
}

/// <summary>Приветствие в начале дня: погода, встречи, главное на сегодня.</summary>
public sealed class GreetingWindow : Window
{
    readonly StackPanel weatherPanel = new() { Spacing = 3 };

    public GreetingWindow(AppSettings s, IReadOnlyList<Meeting> meetings, string? plan, string dayInfo,
        IReadOnlyList<LocalTask>? tasks = null, Flashcard? word = null, int cardsDue = 0)
    {
        var now = DateTime.Now;
        Title = "Доброе утро";
        Icon = Icons.Clock(Palette.Green);
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.B(Palette.Bg);
        Topmost = true;

        string hello = now.Hour switch
        {
            >= 5 and < 12 => "Доброе утро",
            >= 12 and < 17 => "Добрый день",
            >= 17 and < 23 => "Добрый вечер",
            _ => "Доброй ночи"
        };
        if (!string.IsNullOrWhiteSpace(s.UserName)) hello += ", " + s.UserName.Trim();
        Title = hello;

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Spacing = 10 };
        body.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Card.Text(hello + "!", 26, Palette.Text, FontWeight.Bold),
                Card.Text(Card.Ru.TextInfo.ToUpper(now.ToString("dddd, d MMMM", Card.Ru)[0]) + now.ToString("dddd, d MMMM", Card.Ru)[1..], 14, Palette.Muted)
            }
        });

        if (s.Weather)
        {
            weatherPanel.Children.Add(Card.Text(s.HasWeatherPlace || !string.IsNullOrWhiteSpace(s.WeatherCity) ? "Узнаю погоду…" : "Город не выбран — укажи его в настройках.", 14, Palette.Text));
            body.Children.Add(Card.Section("Погода" + (string.IsNullOrWhiteSpace(s.WeatherCity) ? "" : " · " + s.WeatherCity), weatherPanel));
        }

        if (s.CalendarOn)
        {
            var all = meetings.Where(m => m.Start.Date == now.Date).ToList();
            var today = all.Where(m => m.End > now).OrderBy(m => m.Start).ToList();   // ещё не закончились
            var lines = new StackPanel { Spacing = 2 };
            if (today.Count == 0)
                lines.Children.Add(Card.Text(all.Count == 0 ? "Встреч сегодня нет — хороший день для фокуса." : "Все встречи на сегодня уже позади.", 14, Palette.Text));
            else
            {
                string total = Format.Minutes(TimeSpan.FromMinutes(today.Sum(m => (m.End - m.Start).TotalMinutes)));
                lines.Children.Add(Card.Text(today.Count == all.Count
                    ? $"{Plural(today.Count, "встреча", "встречи", "встреч")}, всего {total}"
                    : $"Осталось {today.Count} из {all.Count}, всего {total}", 14, Palette.Text));
                foreach (var m in today.Take(3))
                    lines.Children.Add(Card.Text($"{m.Start:HH:mm}  {m.Title}", 13, Palette.Muted));
                if (today.Count > 3) lines.Children.Add(Card.Text($"…и ещё {today.Count - 3}", 13, Palette.Muted));
            }
            body.Children.Add(Card.Section("Встречи сегодня", lines));
        }

        if (!string.IsNullOrWhiteSpace(plan))
            body.Children.Add(Card.Section("Главное на сегодня", Card.Text(plan, 15, Palette.Text, FontWeight.SemiBold)));

        if (tasks is { Count: > 0 })
        {
            var lines = new StackPanel { Spacing = 2 };
            foreach (var t in tasks.Take(4))
                lines.Children.Add(Card.Text((t.At is { } at ? at.ToString("HH:mm") + "  " : "•  ") + t.Title, 14, Palette.Text));
            if (tasks.Count > 4) lines.Children.Add(Card.Text($"…и ещё {tasks.Count - 4}", 13, Palette.Muted));
            body.Children.Add(Card.Section("Задачи", lines));
        }

        if (word != null)
        {
            var lines = new StackPanel { Spacing = 2 };
            lines.Children.Add(Card.Text(word.Back, 17, Palette.Text, FontWeight.SemiBold));
            lines.Children.Add(Card.Text(word.Front, 13.5, Palette.Muted));
            if (cardsDue > 0) lines.Children.Add(Card.Text($"К повторению сегодня: {Plural(cardsDue, "карточка", "карточки", "карточек")}", 13, Palette.Muted));
            body.Children.Add(Card.Section("Слово дня", lines));
        }

        body.Children.Add(Card.Text(dayInfo, 12.5, Palette.Muted));

        var go = new Button { Content = "Поехали!", Classes = { "flat" }, Background = Palette.B(Palette.Green), Height = 42, Margin = new Thickness(0, 6, 0, 0) };
        go.Click += (_, _) => Close();
        body.Children.Add(go);
        Content = body;
        KeyDown += (_, e) => { if (e.Key is Key.Escape or Key.Enter) Close(); };
    }

    /// <summary>Подставить погоду, когда она загрузится.</summary>
    public void SetWeather(WeatherToday? w, string? error)
    {
        weatherPanel.Children.Clear();
        if (w == null)
        {
            weatherPanel.Children.Add(Card.Text(error ?? "Погоду узнать не удалось.", 14, Palette.Muted));
            return;
        }
        var now = DateTime.Now;
        weatherPanel.Children.Add(Card.Text(Weather.Summary(w, now), 14, Palette.Text));
        var walk = w.BestWalk(now.Date.AddHours(Math.Max(now.Hour, 11)), now.Date.AddHours(18));
        weatherPanel.Children.Add(Card.Text(walk != null
            ? $"Лучшее время для прогулки — около {walk.Time:HH:mm} ({Weather.Temp(walk.Temp)}, {Weather.Describe(walk.Code)})."
            : "Сегодня днём на улице не очень — на большом перерыве лучше пройтись по офису.", 13, Palette.Muted));
    }

    public static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        string w = m10 == 1 && m100 != 11 ? one : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? few : many;
        return $"{n} {w}";
    }
}

/// <summary>Отчёт в конце дня + «главное на завтра».</summary>
public sealed class DayReportWindow : Window
{
    public event Action? StatsRequested;

    /// <param name="practice">Занятия с помощником за день: минуты и серия дней (null — занятия выключены).</param>
    public DayReportWindow(DayReport r, Dictionary<DateTime, DayStat> history, AppSettings s, (int Minutes, int Streak)? practice = null,
        (int Done, int Total, string? Streaks)? habits = null)
    {
        Title = "Итоги дня";
        Icon = Icons.Clock(Palette.Green);
        Width = 500;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Palette.B(Palette.Bg);
        Topmost = true;

        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Spacing = 10 };
        body.Children.Add(new StackPanel
        {
            Spacing = 2,
            Children =
            {
                Card.Text(r.Auto ? $"Рабочий день завершён в {r.End:HH:mm}" : "Рабочий день окончен", 22, Palette.Text, FontWeight.Bold),
                Card.Text($"{r.Start:HH:mm} — {r.End:HH:mm}", 13.5, Palette.Muted)
            }
        });

        body.Children.Add(Card.Section("Работа", Card.Text(Format.Minutes(r.Worked), 26, Palette.Text, FontWeight.SemiBold)));

        // показатели в два столбца
        int asked = r.Done + r.Skipped;
        var tiles = new UniformGrid { Columns = 2 };
        tiles.Children.Add(Tile("Перерывы", asked > 0 ? $"{r.Done} из {asked}" : r.Done.ToString(),
            r.Snoozed > 0 ? $"отложено {r.Snoozed}" : null, new Thickness(0, 0, 5, 5)));
        tiles.Children.Add(Tile("Отдых для глаз", s.EyeRest ? GreetingWindow.Plural(r.EyeRests, "раз", "раза", "раз") : "выключен", null, new Thickness(5, 0, 0, 5)));
        if (s.CalendarOn)
            tiles.Children.Add(Tile("Встречи", r.Meetings.ToString(), r.Meetings > 0 ? Format.Minutes(r.MeetingTime) : null, new Thickness(0, 5, 5, 0)));
        var week = history.Where(kv => kv.Key > r.End.Date.AddDays(-7) && kv.Key <= r.End.Date && kv.Value.Minutes > 0).Select(kv => kv.Value.Minutes).ToList();
        if (week.Count > 1)
            tiles.Children.Add(Tile("В среднем за неделю", Format.Minutes(TimeSpan.FromMinutes(week.Average())), null, new Thickness(5, 5, 0, 0)));
        if (practice is { } p)
        {
            bool left = tiles.Children.Count % 2 == 0;
            tiles.Children.Add(Tile("Занятия", p.Minutes > 0 ? p.Minutes + " мин" : "сегодня не было",
                p.Streak > 0 ? $"серия {GreetingWindow.Plural(p.Streak, "день", "дня", "дней")} подряд" : null,
                left ? new Thickness(0, 5, 5, 0) : new Thickness(5, 5, 0, 0)));
        }
        if (habits is { Total: > 0 } hb)
        {
            bool left = tiles.Children.Count % 2 == 0;
            tiles.Children.Add(Tile("Привычки", $"{hb.Done} из {hb.Total}", hb.Streaks,
                left ? new Thickness(0, 5, 5, 0) : new Thickness(5, 5, 0, 0)));
        }
        body.Children.Add(tiles);

        body.Children.Add(Card.Text(Verdict(r), 14, Palette.Text));

        var plan = new TextBox { Watermark = "Например: доделать интеграцию и отправить на ревью", AcceptsReturn = false, MaxLength = 200 };
        body.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Card.Text("Главное на завтра — напомню утром", 13, Palette.Muted),
                plan
            }
        });

        var stats = new Button { Content = "Статистика", Classes = { "flat" }, Background = Palette.B(Palette.Gray), Height = 42 };
        var bye = new Button { Content = "До завтра!", Classes = { "flat" }, Background = Palette.B(Palette.Green), Height = 42 };
        stats.Click += (_, _) => StatsRequested?.Invoke();
        bye.Click += (_, _) => Close();
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,10,2*"), Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(stats);
        Grid.SetColumn(bye, 2);
        buttons.Children.Add(bye);
        body.Children.Add(buttons);
        Content = body;

        Closed += (_, _) => { if (!string.IsNullOrWhiteSpace(plan.Text)) DayPlan.Save(DateTime.Today, plan.Text); };
        Opened += (_, _) => plan.Focus();
    }

    static Border Tile(string caption, string value, string? sub, Thickness margin)
    {
        var b = Card.Section(caption, Card.Text(value, 18, Palette.Text, FontWeight.SemiBold));
        if (sub != null) ((StackPanel)b.Child!).Children.Add(Card.Text(sub, 12, Palette.Muted));
        b.Margin = margin;
        return b;
    }

    static string Verdict(DayReport r)
    {
        int asked = r.Done + r.Skipped;
        if (r.Worked > TimeSpan.FromHours(9.5)) return "Длинный день. Отдохни как следует — завтра будет легче.";
        if (asked >= 2 && r.Skipped > r.Done) return "Перерывы чаще пропускались. Завтра попробуй хотя бы через один — разница заметна.";
        if (asked >= 2 && r.Done >= asked * 0.8) return "Отлично заботишься о себе — почти все перерывы сделаны.";
        if (r.Worked < TimeSpan.FromHours(2)) return "Короткий день. Хорошего вечера!";
        return "Хорошая работа. Теперь — отдыхать.";
    }
}
