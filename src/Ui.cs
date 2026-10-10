using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BreakReminder
{
    static class Ui
    {
        public static readonly Color Bg = Color.FromArgb(30, 31, 38);
        public static readonly Color Card = Color.FromArgb(42, 44, 56);
        public static readonly Color Text = Color.FromArgb(236, 239, 244);
        public static readonly Color Muted = Color.FromArgb(140, 146, 165);
        public static readonly Color Green = Color.FromArgb(46, 204, 113);
        public static readonly Color Red = Color.FromArgb(231, 76, 60);
        public static readonly Color Amber = Color.FromArgb(243, 156, 18);
        public static readonly Color Purple = Color.FromArgb(155, 89, 182);
        public static readonly Color Blue = Color.FromArgb(52, 152, 219);
        public static readonly Color Gray =Color.FromArgb(72, 76, 94);

        static float scale;

        public static float Scale
        {
            get
            {
                if (scale == 0)
                {
                    using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                }
                return scale;
            }
        }

        /// <summary>Пиксели при 100% масштабе -> реальные пиксели.</summary>
        public static int S(int v)
        {
            return (int)Math.Round(v * Scale);
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Lighten(Color c, float t) { return Mix(c, Color.White, t); }
        public static Color Darken(Color c, float t) { return Mix(c, Color.Black, t); }

        public static string Hms(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds);
        }

        public static string Ms(TimeSpan t)
        {
            int sec = (int)Math.Ceiling(Math.Max(0, t.TotalSeconds));
            return string.Format("{0:00}:{1:00}", sec / 60, sec % 60);
        }

        public static string Minutes(TimeSpan t)
        {
            int m = (int)Math.Round(t.TotalMinutes);
            if (m >= 60) return string.Format("{0} ч {1} мин", m / 60, m % 60);
            return m + " мин";
        }

        public static void SetText(Control c, string text)
        {
            if (c.Text != text) c.Text = text;
        }

        public static Button FlatButton(string text, Color back)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = back,
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 10.5f),
                TabStop = false,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Lighten(back, 0.12f);
            b.FlatAppearance.MouseDownBackColor = Darken(back, 0.12f);
            return b;
        }

        /// <summary>Иконка-«часики» нужного цвета (для окна и трея).</summary>
        public static Icon MakeIcon(Color c)
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var b = new SolidBrush(c)) g.FillEllipse(b, 1, 1, 29, 29);
                    using (var p = new Pen(Color.White, 3.2f))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawLine(p, 16, 16, 16, 7);
                        g.DrawLine(p, 16, 16, 23, 19);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }
}
