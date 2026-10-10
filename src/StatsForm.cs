using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BreakReminder
{
    public class DayStat
    {
        public int Minutes, Done, Skipped, Snoozed;

        public void Add(DayStat o)
        {
            Minutes += o.Minutes;
            Done += o.Done;
            Skipped += o.Skipped;
            Snoozed += o.Snoozed;
        }
    }

    static class History
    {
        public static string FilePath { get { return Path.Combine(AppSettings.DataDir, "history.csv"); } }

        public static void Append(DateTime start, DateTime end, DayStat d)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDir);
                var sb = new StringBuilder();
                if (!File.Exists(FilePath)) sb.AppendLine("Дата;Начало;Конец;Работа (мин);Сделано;Пропущено;Отложено");
                sb.AppendLine(string.Format("{0:yyyy-MM-dd};{0:HH:mm};{1:HH:mm};{2};{3};{4};{5}",
                    start, end, d.Minutes, d.Done, d.Skipped, d.Snoozed));
                File.AppendAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        /// <summary>Все записи, сложенные по дням (за день может быть несколько сессий).</summary>
        public static Dictionary<DateTime, DayStat> Load()
        {
            var result = new Dictionary<DateTime, DayStat>();
            try
            {
                if (!File.Exists(FilePath)) return result;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string[] p = line.Split(';');
                    DateTime date;
                    if (p.Length < 7 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                        continue; // заголовок или битая строка
                    var d = new DayStat();
                    int.TryParse(p[3], out d.Minutes);
                    int.TryParse(p[4], out d.Done);
                    int.TryParse(p[5], out d.Skipped);
                    int.TryParse(p[6], out d.Snoozed);
                    DayStat acc;
                    if (!result.TryGetValue(date, out acc)) result[date] = acc = new DayStat();
                    acc.Add(d);
                }
            }
            catch { }
            return result;
        }
    }

    /// <summary>Статистика по неделям: столбики по дням + итоги.</summary>
    public class StatsForm : Form
    {
        static readonly CultureInfo Ru = new CultureInfo("ru-RU");

        readonly Dictionary<DateTime, DayStat> data;
        DateTime weekStart;
        readonly Label lblRange, lblTotal, lblAvg, lblDone, lblSkipped;
        readonly WeekChart chart;
        readonly Button btnNext;

        public StatsForm(DayStat liveToday)
        {
            data = History.Load();
            if (liveToday != null)
            {
                DayStat acc;
                if (!data.TryGetValue(DateTime.Today, out acc)) data[DateTime.Today] = acc = new DayStat();
                acc.Add(liveToday);
            }
            weekStart = MondayOf(DateTime.Today);

            Text = "Статистика";
            Icon = Ui.MakeIcon(Ui.Blue);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(Ui.S(640), Ui.S(470));
            int W = ClientSize.Width;

            var btnPrev = NavButton("‹", Ui.S(16));
            btnNext = NavButton("›", W - Ui.S(16) - Ui.S(40));
            btnPrev.Click += delegate { weekStart = weekStart.AddDays(-7); Refresh2(); };
            btnNext.Click += delegate { weekStart = weekStart.AddDays(7); Refresh2(); };
            lblRange = new Label
            {
                AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI Semibold", 13f), ForeColor = Ui.Text,
                Bounds = new Rectangle(Ui.S(60), Ui.S(14), W - Ui.S(120), Ui.S(36))
            };
            Controls.Add(lblRange);

            int tileW = (W - Ui.S(32) - 3 * Ui.S(10)) / 4;
            lblTotal = Tile("Работа за неделю", 0, tileW);
            lblAvg = Tile("В среднем за день", 1, tileW);
            lblDone = Tile("Перерывов сделано", 2, tileW);
            lblSkipped = Tile("Пропуск / отлож.", 3, tileW);

            chart = new WeekChart { Bounds = new Rectangle(Ui.S(16), Ui.S(140), W - Ui.S(32), Ui.S(280)) };
            Controls.Add(chart);

            var link = new LinkLabel
            {
                Text = "Открыть файл истории",
                AutoSize = true,
                LinkColor = Ui.Muted, ActiveLinkColor = Ui.Text,
                Location = new Point(Ui.S(16), Ui.S(436))
            };
            link.LinkClicked += delegate
            {
                if (File.Exists(History.FilePath)) Process.Start(History.FilePath);
                else MessageBox.Show(this, "История пока пуста — она появится после первого законченного дня.", "Статистика");
            };
            Controls.Add(link);

            Refresh2();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
        }

        static DateTime MondayOf(DateTime d)
        {
            int diff = ((int)d.DayOfWeek + 6) % 7; // Пн = 0
            return d.Date.AddDays(-diff);
        }

        Button NavButton(string text, int x)
        {
            var b = Ui.FlatButton(text, Ui.Gray);
            b.Font = new Font("Segoe UI", 16f, FontStyle.Bold);
            b.Bounds = new Rectangle(x, Ui.S(14), Ui.S(40), Ui.S(36));
            Controls.Add(b);
            return b;
        }

        Label Tile(string caption, int index, int w)
        {
            int x = Ui.S(16) + index * (w + Ui.S(10)), y = Ui.S(62);
            var panel = new Panel { BackColor = Ui.Card, Bounds = new Rectangle(x, y, w, Ui.S(64)) };
            panel.Controls.Add(new Label
            {
                Text = caption, ForeColor = Ui.Muted, Font = new Font("Segoe UI", 8.5f),
                AutoSize = false, Bounds = new Rectangle(Ui.S(10), Ui.S(6), w - Ui.S(12), Ui.S(20))
            });
            var value = new Label
            {
                ForeColor = Ui.Text, Font = new Font("Segoe UI Semibold", 13f),
                AutoSize = false, Bounds = new Rectangle(Ui.S(10), Ui.S(26), w - Ui.S(12), Ui.S(32))
            };
            panel.Controls.Add(value);
            Controls.Add(panel);
            return value;
        }

        void Refresh2()
        {
            DateTime end = weekStart.AddDays(6);
            lblRange.Text = weekStart.Month == end.Month
                ? string.Format(Ru, "{0:d} – {1:d MMMM yyyy}", weekStart.Day, end)
                : string.Format(Ru, "{0:d MMMM} – {1:d MMMM yyyy}", weekStart, end);
            btnNext.Enabled = weekStart < MondayOf(DateTime.Today);

            var days = new DayStat[7];
            var week = new DayStat();
            int workedDays = 0;
            for (int i = 0; i < 7; i++)
            {
                DayStat d;
                data.TryGetValue(weekStart.AddDays(i), out d);
                days[i] = d;
                if (d != null)
                {
                    week.Add(d);
                    if (d.Minutes > 0) workedDays++;
                }
            }

            lblTotal.Text = Ui.Minutes(TimeSpan.FromMinutes(week.Minutes));
            lblAvg.Text = workedDays > 0 ? Ui.Minutes(TimeSpan.FromMinutes(week.Minutes / workedDays)) : "—";
            int asked = week.Done + week.Skipped;
            lblDone.Text = asked > 0 ? string.Format("{0}  ({1}%)", week.Done, week.Done * 100 / asked) : week.Done.ToString();
            lblSkipped.Text = week.Skipped + " / " + week.Snoozed;

            chart.SetData(weekStart, days);
        }
    }

    class WeekChart : Control
    {
        static readonly string[] DayNames = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };
        DateTime start;
        DayStat[] days = new DayStat[7];

        public WeekChart()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Ui.Card;
        }

        public void SetData(DateTime weekStart, DayStat[] d)
        {
            start = weekStart;
            days = d;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(BackColor);

            int maxMin = 8 * 60;
            foreach (var d in days) if (d != null) maxMin = Math.Max(maxMin, d.Minutes);

            int top = Ui.S(30), bottom = Height - Ui.S(58);
            float plotH = bottom - top;
            float colW = Width / 7f;
            float barW = colW * 0.46f;

            using (var font = new Font("Segoe UI", 9f))
            using (var bold = new Font("Segoe UI Semibold", 9.5f))
            using (var small = new Font("Segoe UI", 8.5f))
            using (var muted = new SolidBrush(Ui.Muted))
            using (var text = new SolidBrush(Ui.Text))
            using (var gridPen = new Pen(Color.FromArgb(60, 64, 80)) { DashStyle = DashStyle.Dot })
            using (var center = new StringFormat { Alignment = StringAlignment.Center })
            {
                // линия «8 часов»
                float y8 = bottom - plotH * (8 * 60f / maxMin);
                g.DrawLine(gridPen, 0, y8, Width, y8);
                g.DrawString("8 ч", small, muted, Ui.S(4), y8 - Ui.S(16));
                g.DrawLine(gridPen, 0, bottom, Width, bottom);

                for (int i = 0; i < 7; i++)
                {
                    DateTime date = start.AddDays(i);
                    var d = days[i];
                    float cx = colW * i + colW / 2;
                    bool today = date == DateTime.Today;

                    if (d != null && d.Minutes > 0)
                    {
                        float h = Math.Max(Ui.S(3), plotH * d.Minutes / maxMin);
                        var bar = new RectangleF(cx - barW / 2, bottom - h, barW, h);
                        Color c = today ? Ui.Green : Ui.Blue;
                        using (var b = new LinearGradientBrush(bar, Ui.Lighten(c, 0.15f), c, 90f)) g.FillRectangle(b, bar);
                        g.DrawString(string.Format("{0}:{1:00}", d.Minutes / 60, d.Minutes % 60), bold, text,
                            new RectangleF(cx - colW / 2, bar.Top - Ui.S(22), colW, Ui.S(20)), center);
                    }

                    g.DrawString(DayNames[i] + "  " + date.ToString("dd.MM"), today ? bold : font, today ? text : muted,
                        new RectangleF(cx - colW / 2, bottom + Ui.S(6), colW, Ui.S(20)), center);
                    if (d != null && d.Done + d.Skipped > 0)
                        g.DrawString("✓" + d.Done + "  ✕" + d.Skipped, small, muted,
                            new RectangleF(cx - colW / 2, bottom + Ui.S(28), colW, Ui.S(20)), center);
                }
            }
        }
    }
}
