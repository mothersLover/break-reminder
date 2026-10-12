using System;
using System.Drawing;
using System.Windows.Forms;

namespace BreakReminder
{
    public enum ReminderResult { Done, Snooze, Skip }

    /// <summary>Окно «Время отдохнуть!»: в углу, по центру или на весь экран.</summary>
    public class ReminderForm : Form
    {
        static readonly Random Rnd = new Random();
        static Icon alertIcon;

        public event Action<ReminderResult> Answered;

        readonly AppSettings s;
        readonly Color accent;
        readonly Panel frame;
        readonly Label lblTitle;
        readonly Timer blinkTimer, soundTimer, animTimer;
        bool answered, phase;
        int animStep;
        const int AnimSteps = 14;
        Point animFrom, animTo;
        double targetOpacity = 1.0;

        public ReminderForm(AppSettings settings, TimeSpan withoutBreak, bool preview, bool isLong)
        {
            s = settings;
            accent = isLong ? Ui.Purple : Ui.Amber;
            if (alertIcon == null) alertIcon = Ui.MakeIcon(Ui.Amber);
            string title = isLong ? s.TitleLong : s.TitleShort;
            string[] tips = isLong ? s.TipsLong : s.TipsShort;
            if (tips == null || tips.Length == 0) tips = isLong ? Texts.LongTips : Texts.Tips;

            Text = title;
            Icon = alertIcon;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            TopMost = true;
            KeyPreview = true;
            BackColor = Ui.Bg;
            Font = new Font("Segoe UI", 9.5f);

            int bw = Ui.S(5);
            var card = new Panel { BackColor = Ui.Card, Size = new Size(Ui.S(460), Ui.S(240)), Location = new Point(bw, bw) };
            frame = new Panel { BackColor = accent, Size = new Size(card.Width + 2 * bw, card.Height + 2 * bw) };
            frame.Controls.Add(card);

            int pad = Ui.S(24), w = card.Width - 2 * pad;
            lblTitle = new Label
            {
                Text = title,
                Font = new Font("Segoe UI", 20f, FontStyle.Bold),
                ForeColor = Ui.Text,
                AutoSize = false,
                Bounds = new Rectangle(pad, Ui.S(16), w, Ui.S(46))
            };
            var lblTip = new Label
            {
                Text = tips[Rnd.Next(tips.Length)],
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 12f),
                ForeColor = Ui.Text,
                AutoSize = false,
                Bounds = new Rectangle(pad, Ui.S(66), w, Ui.S(54))
            };
            var lblInfo = new Label
            {
                Text = preview ? "Это тестовое уведомление."
                     : (isLong ? "Перерыв " + s.LongBreakMin + " мин" : "Перерыв " + s.BreakMin + " мин")
                       + (withoutBreak.TotalMinutes >= 1 ? "  ·  без перерыва " + Ui.Minutes(withoutBreak) : ""),
                ForeColor = Ui.Muted,
                AutoSize = false,
                Bounds = new Rectangle(pad, Ui.S(122), w, Ui.S(24))
            };

            int gap = Ui.S(10), bh = Ui.S(46), by = Ui.S(168);
            int btnW = (w - 2 * gap) / 3;
            var btnDone = Ui.FlatButton(s.BtnDone, Ui.Green);
            var btnSnooze = Ui.FlatButton(s.BtnSnooze.Replace("{N}", s.SnoozeMin.ToString()), Ui.Gray);
            var btnSkip = Ui.FlatButton(s.BtnSkip, Ui.Gray);
            btnDone.Bounds = new Rectangle(pad, by, btnW, bh);
            btnSnooze.Bounds = new Rectangle(pad + btnW + gap, by, btnW, bh);
            btnSkip.Bounds = new Rectangle(pad + 2 * (btnW + gap), by, btnW, bh);
            btnDone.Click += delegate { Answer(ReminderResult.Done); };
            btnSnooze.Click += delegate { Answer(ReminderResult.Snooze); };
            btnSkip.Click += delegate { Answer(ReminderResult.Skip); };

            card.Controls.AddRange(new Control[] { lblTitle, lblTip, lblInfo, btnDone, btnSnooze, btnSkip });
            Controls.Add(frame);

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            switch (s.Popup)
            {
                case PopupMode.Fullscreen:
                    Bounds = Screen.FromPoint(Cursor.Position).Bounds;
                    BackColor = Color.FromArgb(12, 12, 16);
                    frame.Location = new Point((Width - frame.Width) / 2, (Height - frame.Height) / 2);
                    targetOpacity = 0.95;
                    animFrom = animTo = Location;
                    break;
                case PopupMode.Center:
                    ClientSize = frame.Size;
                    animTo = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
                    animFrom = new Point(animTo.X, animTo.Y + Ui.S(30));
                    break;
                default:
                    ClientSize = frame.Size;
                    animTo = new Point(wa.Right - Width - Ui.S(16), wa.Bottom - Height - Ui.S(16));
                    animFrom = new Point(animTo.X, wa.Bottom);
                    break;
            }
            Location = animFrom;
            Opacity = 0;

            blinkTimer = new Timer { Interval = 550 };
            blinkTimer.Tick += delegate
            {
                phase = !phase;
                frame.BackColor = phase ? Ui.Card : accent;
                lblTitle.ForeColor = phase ? accent : Ui.Text;
            };
            soundTimer = new Timer { Interval = Math.Max(10, s.RepeatSec) * 1000 };
            soundTimer.Tick += delegate { Sounds.Play(s); };
            animTimer = new Timer { Interval = 15 };
            animTimer.Tick += OnAnim;
        }

        // В углу и по центру окно не забирает фокус — чтобы случайно не нажать кнопку, пока печатаешь.
        protected override bool ShowWithoutActivation
        {
            get { return s.Popup != PopupMode.Fullscreen; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (s.Popup != PopupMode.Fullscreen) Native.RoundCorners(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            animTimer.Start();
            if (s.Blink)
            {
                blinkTimer.Start();
                Native.Flash(Handle, true);
            }
            if (s.SoundOn)
            {
                Sounds.Play(s);
                if (s.RepeatSound) soundTimer.Start();
            }
        }

        void OnAnim(object sender, EventArgs e)
        {
            animStep++;
            double t = Math.Min(1.0, (double)animStep / AnimSteps);
            double ease = 1 - Math.Pow(1 - t, 3);
            Location = new Point(
                (int)(animFrom.X + (animTo.X - animFrom.X) * ease),
                (int)(animFrom.Y + (animTo.Y - animFrom.Y) * ease));
            Opacity = targetOpacity * ease;
            if (t >= 1) animTimer.Stop();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Answer(ReminderResult.Snooze); return true; }
            // Глотаем Enter/Пробел, чтобы набор текста не «нажал» кнопку.
            if (keyData == Keys.Enter || keyData == Keys.Space) return true;
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void StopAll()
        {
            blinkTimer.Stop();
            soundTimer.Stop();
            animTimer.Stop();
            Sounds.Stop();
            if (IsHandleCreated) Native.Flash(Handle, false);
        }

        void Answer(ReminderResult r)
        {
            if (answered) return;
            answered = true;
            StopAll();
            var handler = Answered;
            Close();
            if (handler != null) handler(r);
        }

        /// <summary>Закрыть без ответа (пауза / конец дня).</summary>
        public void CloseSilently()
        {
            answered = true;
            StopAll();
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Alt+F4 и т.п. — считаем как «отложить».
            if (!answered)
            {
                answered = true;
                StopAll();
                var handler = Answered;
                if (handler != null) handler(ReminderResult.Snooze);
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            blinkTimer.Dispose();
            soundTimer.Dispose();
            animTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
