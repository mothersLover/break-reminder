using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>Статистика по неделям: столбики по дням + итоги.</summary>
public partial class StatsWindow : Window
{
    static readonly CultureInfo Ru = new("ru-RU");

    readonly Dictionary<DateTime, DayStat> data;
    DateTime weekStart;

    public StatsWindow() : this(new Dictionary<DateTime, DayStat>(), null, null!, "") { }

    /// <param name="learning">Вкладка «Учёба» (null — без неё); openLearning — открыть сразу её.</param>
    public StatsWindow(Dictionary<DateTime, DayStat> history, DayStat? liveToday, IPlatform platform, string historyFile,
        Control? learning = null, bool openLearning = false)
    {
        InitializeComponent();
        LearningTab.IsVisible = learning != null;
        LearningTab.Content = learning;
        if (learning != null && openLearning) Tabs.SelectedItem = LearningTab;
        Icon = Icons.Clock(Palette.Blue);
        data = history;
        if (liveToday != null)
        {
            if (!data.TryGetValue(DateTime.Today, out var acc)) data[DateTime.Today] = acc = new DayStat();
            acc.Add(liveToday);
        }
        weekStart = MondayOf(DateTime.Today);

        PrevButton.Click += (_, _) => { weekStart = weekStart.AddDays(-7); Refresh(); };
        NextButton.Click += (_, _) => { weekStart = weekStart.AddDays(7); Refresh(); };
        OpenFileButton.Click += async (_, _) =>
        {
            if (File.Exists(historyFile)) platform?.OpenPath(historyFile);
            else await ConfirmDialog.Info(this,"История пока пуста — она появится после первого законченного дня.");
        };
        Refresh();
    }

    static DateTime MondayOf(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));

    /// <summary>Для тестов/скриншотов: подменить данные.</summary>
    internal Dictionary<DateTime, DayStat> Data => data;

    internal void Refresh()
    {
        DateTime end = weekStart.AddDays(6);
        RangeText.Text = weekStart.Month == end.Month
            ? string.Format(Ru, "{0} – {1:d MMMM yyyy}", weekStart.Day, end)
            : string.Format(Ru, "{0:d MMMM} – {1:d MMMM yyyy}", weekStart, end);
        NextButton.IsEnabled = weekStart < MondayOf(DateTime.Today);

        var days = new DayStat?[7];
        var week = new DayStat();
        int workedDays = 0;
        for (int i = 0; i < 7; i++)
        {
            data.TryGetValue(weekStart.AddDays(i), out var d);
            days[i] = d;
            if (d == null) continue;
            week.Add(d);
            if (d.Minutes > 0) workedDays++;
        }

        TotalText.Text = Format.Minutes(TimeSpan.FromMinutes(week.Minutes));
        AvgText.Text = workedDays > 0 ? Format.Minutes(TimeSpan.FromMinutes(week.Minutes / workedDays)) : "—";
        int asked = week.Done + week.Skipped;
        DoneText.Text = asked > 0 ? $"{week.Done}  ({week.Done * 100 / asked}%)" : week.Done.ToString();
        SkippedText.Text = $"{week.Skipped} / {week.Snoozed}";

        Chart.SetData(weekStart, days);
    }
}

/// <summary>Столбики «часы работы» по дням недели.</summary>
public sealed class WeekChart : Control
{
    static readonly string[] DayNames = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
    static readonly Typeface Regular = new(FontFamily.Default);
    static readonly Typeface Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);

    DateTime start;
    DayStat?[] days = new DayStat?[7];

    public void SetData(DateTime weekStart, DayStat?[] d)
    {
        start = weekStart;
        days = d;
        InvalidateVisual();
    }

    static FormattedText Text(string s, double size, IBrush brush, bool bold = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Bold : Regular, size, brush);

    public override void Render(DrawingContext g)
    {
        double w = Bounds.Width, h = Bounds.Height;
        g.FillRectangle(Palette.B(Palette.Card), new Rect(0, 0, w, h), 4);

        int maxMin = 8 * 60;
        foreach (var d in days) if (d != null) maxMin = Math.Max(maxMin, d.Minutes);

        double top = 30, bottom = h - 56, plotH = bottom - top;
        double colW = w / 7, barW = colW * 0.46;
        var muted = Palette.B(Palette.Muted);
        var text = Palette.B(Palette.Text);
        var grid = new Pen(new SolidColorBrush(Color.Parse("#3C4050")), 1, new DashStyle(new double[] { 2, 3 }, 0));

        // линия «8 часов»
        double y8 = bottom - plotH * (8 * 60.0 / maxMin);
        g.DrawLine(grid, new Point(0, y8), new Point(w, y8));
        g.DrawText(Text("8 ч", 11, muted), new Point(6, y8 - 16));
        g.DrawLine(grid, new Point(0, bottom), new Point(w, bottom));

        for (int i = 0; i < 7; i++)
        {
            DateTime date = start.AddDays(i);
            var d = days[i];
            double cx = colW * i + colW / 2;
            bool today = date == DateTime.Today;

            if (d is { Minutes: > 0 })
            {
                double bh = Math.Max(3, plotH * d.Minutes / maxMin);
                var bar = new Rect(cx - barW / 2, bottom - bh, barW, bh);
                var c = today ? Palette.Green : Palette.Blue;
                var brush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Lighten(c, 0.15), 0), new GradientStop(c, 1) }
                };
                g.FillRectangle(brush, bar);
                var label = Text($"{d.Minutes / 60}:{d.Minutes % 60:00}", 13, text, true);
                g.DrawText(label, new Point(cx - label.Width / 2, bar.Top - 20));
            }

            var day = Text(DayNames[i] + "  " + date.ToString("dd.MM"), 12.5, today ? text : muted, today);
            g.DrawText(day, new Point(cx - day.Width / 2, bottom + 8));
            if (d != null && d.Done + d.Skipped > 0)
            {
                var marks = Text($"✓{d.Done}  ✕{d.Skipped}", 11.5, muted);
                g.DrawText(marks, new Point(cx - marks.Width / 2, bottom + 30));
            }
        }
    }

    static Color Lighten(Color c, double t) => Color.FromRgb(
        (byte)(c.R + (255 - c.R) * t), (byte)(c.G + (255 - c.G) * t), (byte)(c.B + (255 - c.B) * t));
}
