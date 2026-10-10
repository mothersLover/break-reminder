using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BreakReminder
{
    enum WorkState { Idle, Running, Paused }

    public class MainForm : Form
    {
        AppSettings settings;
        WorkState state = WorkState.Idle;

        TimeSpan worked;          // время работы за день (без пауз)
        TimeSpan remaining;       // до следующего напоминания
        TimeSpan sinceLastBreak;  // сколько работаешь без настоящего перерыва
        DateTime lastTick, dayStart, pauseStart;
        bool away, exiting, trayHintShown, blinkPhase;
        TimeSpan awayIdle;        // сколько длилось текущее отсутствие
        int done, skipped, snoozed;
        int cycle;                // сделано обычных перерывов с последнего большого
        bool reminderIsLong;
        string statusText = "Нажми «Начать», когда садишься за работу";
        ReminderForm reminder;

        // «не беспокоить»: проверяем не чаще раза в 2 секунды
        DateTime lastBusyCheck;
        bool callActive, fullscreenActive;
        string dndReason;         // почему напоминание сейчас отложено (null — не отложено)
        bool hotkeyRegistered;
        const int HotkeyId = 1;

        readonly Label lblCaption, lblWork, lblStatus, lblNext, lblPause, lblStatsBtn, lblSettings, lblStats;
        readonly RoundButton btnMain, btnPause, btnStats, btnSettings;
        readonly Timer timer;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem miPause, miMain;
        readonly Icon icoIdle, icoRun, icoPause, icoAlert;

        public MainForm()
        {
            settings = AppSettings.Load();

            icoIdle = Ui.MakeIcon(Ui.Gray);
            icoRun = Ui.MakeIcon(Ui.Green);
            icoPause = Ui.MakeIcon(Ui.Amber);
            icoAlert = Ui.MakeIcon(Ui.Red);

            Text = "Отдыхалка";
            Icon = icoRun;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ui.Bg;
            ForeColor = Ui.Text;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(Ui.S(400), Ui.S(600));
            int W = ClientSize.Width;

            lblCaption = AddLabel("ВРЕМЯ РАБОТЫ", 9f, FontStyle.Bold, Ui.Muted, 22, 20);
            lblWork = AddLabel("00:00:00", 38f, FontStyle.Regular, Ui.Muted, 42, 76, "Segoe UI Light");
            lblStatus = AddLabel("", 9.5f, FontStyle.Regular, Ui.Muted, 118, 40);
            lblStatus.Padding = new Padding(Ui.S(16), 0, Ui.S(16), 0);

            btnMain = new RoundButton
            {
                Bounds = new Rectangle((W - Ui.S(260)) / 2, Ui.S(162), Ui.S(260), Ui.S(260)),
                Font = new Font("Segoe UI Semibold", 20f),
                Text = "НАЧАТЬ",
                Fill = Ui.Green
            };
            btnMain.Click += delegate { OnMainClick(); };
            Controls.Add(btnMain);

            lblNext = AddLabel("", 14f, FontStyle.Regular, Ui.Text, 428, 32);

            int small = Ui.S(64), sy = Ui.S(470);
            int pauseCx = W / 2 - Ui.S(110), statsCx = W / 2, setCx = W / 2 + Ui.S(110);
            btnPause = SmallButton(pauseCx, sy, small, Glyph.Pause);
            btnStats = SmallButton(statsCx, sy, small, Glyph.Chart);
            btnSettings = SmallButton(setCx, sy, small, Glyph.Gear);
            btnPause.Click += delegate { TogglePause(); };
            btnStats.Click += delegate { OpenStats(); };
            btnSettings.Click += delegate { OpenSettings(); };

            lblPause = SmallCaption("Пауза", pauseCx);
            lblStatsBtn = SmallCaption("Статистика", statsCx);
            lblSettings = SmallCaption("Настройки", setCx);

            lblStats = AddLabel("", 9f, FontStyle.Regular, Ui.Muted, 568, 24);

            var tip = new ToolTip();
            tip.SetToolTip(btnPause, "Пауза: останавливает и время работы, и отсчёт до перерыва (Ctrl+Alt+P)");
            tip.SetToolTip(btnStats, "Статистика по неделям");
            tip.SetToolTip(btnSettings, "Настройки");

            // Трей
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Показать окно", null, delegate { ShowMain(); }));
            miPause = new ToolStripMenuItem("Пауза", null, delegate { TogglePause(); });
            miMain = new ToolStripMenuItem("Начать день", null, delegate { OnMainClick(); });
            menu.Items.Add(miPause);
            menu.Items.Add(miMain);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Выход", null, delegate { ExitApp(); }));
            tray = new NotifyIcon { Icon = icoIdle, Text = "Отдыхалка", Visible = true, ContextMenuStrip = menu };
            tray.MouseClick += (sender, e) => { if (e.Button == MouseButtons.Left) ShowMain(); };

            timer = new Timer { Interval = 500 };
            timer.Tick += OnTick;
            lastTick = DateTime.Now;
            timer.Start();

            UpdateUi();
        }

        Label AddLabel(string text, float size, FontStyle style, Color color, int top, int height, string family = "Segoe UI")
        {
            var l = new Label
            {
                Text = text,
                Font = new Font(family, size, style),
                ForeColor = color,
                BackColor = Ui.Bg,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Bounds = new Rectangle(0, Ui.S(top), ClientSize.Width, Ui.S(height))
            };
            Controls.Add(l);
            return l;
        }

        RoundButton SmallButton(int cx, int y, int size, Glyph glyph)
        {
            var b = new RoundButton { Bounds = new Rectangle(cx - size / 2, y, size, size), Fill = Ui.Gray, Glyph = glyph };
            Controls.Add(b);
            return b;
        }

        Label SmallCaption(string text, int cx)
        {
            var l = AddLabel(text, 9f, FontStyle.Regular, Ui.Muted, 538, 20);
            l.Bounds = new Rectangle(cx - Ui.S(55), Ui.S(538), Ui.S(110), Ui.S(20));
            return l;
        }

        // ---------- горячая клавиша ----------

        void ApplyHotkey()
        {
            if (!IsHandleCreated) return;
            if (hotkeyRegistered)
            {
                Native.UnregisterHotKey(Handle, HotkeyId);
                hotkeyRegistered = false;
            }
            if (!settings.PauseHotkey) return;
            hotkeyRegistered = Native.RegisterHotKey(Handle, HotkeyId,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.P);
            if (!hotkeyRegistered)
                statusText = "Ctrl+Alt+P уже занята другой программой";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (state == WorkState.Idle) return;
                TogglePause();
                if (!Visible)
                    tray.ShowBalloonTip(1500, "Отдыхалка",
                        state == WorkState.Paused ? "Пауза" : "Продолжаем работу", ToolTipIcon.None);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hotkeyRegistered) Native.UnregisterHotKey(Handle, HotkeyId);
            hotkeyRegistered = false;
            base.OnHandleDestroyed(e);
        }

        // ---------- «не беспокоить» ----------

        void RefreshBusy(bool force)
        {
            DateTime now = DateTime.Now;
            if (!force && now - lastBusyCheck < TimeSpan.FromSeconds(2)) return;
            lastBusyCheck = now;
            callActive = Native.IsMicOrCameraInUse();
            fullscreenActive = Native.IsFullscreenAppActive();
        }

        /// <summary>Причина не показывать напоминание сейчас, или null.</summary>
        string DndReason()
        {
            RefreshBusy(false);
            if (settings.DndCalls && callActive) return "идёт созвон";
            if (settings.DndFullscreen && fullscreenActive) return "открыта полноэкранная программа";
            return null;
        }

        bool NextIsLong
        {
            get { return settings.LongBreaks && cycle >= settings.LongEvery - 1; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.DarkTitleBar(Handle);
            ApplyHotkey();
        }

        // ---------- логика ----------

        void OnTick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            TimeSpan delta = now - lastTick;
            lastTick = now;
            if (delta < TimeSpan.Zero) delta = TimeSpan.Zero; // перевели часы назад

            if (state == WorkState.Running)
            {
                worked += delta;
                sinceLastBreak += delta;

                if (reminder == null)
                {
                    TimeSpan rest = settings.BreakSpan;
                    TimeSpan idle;
                    if (delta >= rest)
                    {
                        // Между тиками прошло много времени — компьютер спал.
                        ResetAfterRest("Компьютер спал " + Ui.Minutes(delta) + " — засчитано как перерыв", delta);
                    }
                    else if (settings.IdleDetect && (idle = Native.IdleTime()) >= rest && !BusyWithoutInput())
                    {
                        // Созвон или видео на весь экран без мыши — это не отдых, поэтому BusyWithoutInput.
                        if (!away) statusText = "Ты отошёл — засчитываю это как отдых";
                        away = true;
                        awayIdle = idle;
                        remaining = settings.Interval;
                        sinceLastBreak = TimeSpan.Zero;
                        dndReason = null;
                    }
                    else
                    {
                        if (away)
                        {
                            away = false;
                            if (awayIdle >= settings.LongBreakSpan) cycle = 0;
                            statusText = "С возвращением! Следующий перерыв в " + NextAt();
                        }
                        if (remaining > TimeSpan.Zero) remaining -= delta;
                        if (remaining <= TimeSpan.Zero)
                        {
                            string why = DndReason();
                            if (why == null)
                            {
                                dndReason = null;
                                ShowReminder();
                            }
                            else if (why != dndReason)
                            {
                                dndReason = why;
                                statusText = "Не беспокою: " + why + ". Напомню, когда закончится";
                            }
                        }
                    }
                }
            }
            UpdateUi();
        }

        string NextAt()
        {
            return (DateTime.Now + remaining).ToString("HH:mm");
        }

        bool BusyWithoutInput()
        {
            RefreshBusy(false);
            return callActive || fullscreenActive;
        }

        void ResetAfterRest(string message)
        {
            remaining = settings.Interval;
            sinceLastBreak = TimeSpan.Zero;
            dndReason = null;
            statusText = message;
        }

        /// <summary>Отдых известной длины: если он не короче большого перерыва — цикл начинается заново.</summary>
        void ResetAfterRest(string message, TimeSpan restLength)
        {
            if (restLength >= settings.LongBreakSpan) cycle = 0;
            ResetAfterRest(message);
        }

        void OnMainClick()
        {
            if (state == WorkState.Idle)
            {
                StartDay();
                return;
            }
            var owner = Visible ? this : null;
            if (MessageBox.Show(owner, "Закончить рабочий день?", "Отдыхалка",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                EndDay();
        }

        void StartDay()
        {
            state = WorkState.Running;
            worked = sinceLastBreak = TimeSpan.Zero;
            remaining = settings.Interval;
            done = skipped = snoozed = cycle = 0;
            away = false;
            dndReason = null;
            dayStart = lastTick = DateTime.Now;
            statusText = "Рабочий день начат. Первый перерыв в " + NextAt();
            UpdateUi();
        }

        void EndDay()
        {
            CloseReminder();
            state = WorkState.Idle;
            SaveHistory();
            statusText = string.Format("День окончен: {0} работы\nперерывов {1}, пропущено {2}",
                Ui.Minutes(worked), done, skipped);
            UpdateUi();
        }

        void TogglePause()
        {
            if (state == WorkState.Running)
            {
                state = WorkState.Paused;
                pauseStart = DateTime.Now;
                CloseReminder(); // напоминание вернётся после паузы, если перерыв так и не сделан
                statusText = "Пауза — время и напоминания остановлены";
            }
            else if (state == WorkState.Paused)
            {
                TimeSpan p = DateTime.Now - pauseStart;
                state = WorkState.Running;
                lastTick = DateTime.Now;
                if (p >= settings.BreakSpan)
                    ResetAfterRest("Пауза " + Ui.Minutes(p) + " — засчитано как перерыв", p);
                else
                    statusText = "Продолжаем";
            }
            UpdateUi();
        }

        void ShowReminder()
        {
            if (reminder != null) return;
            reminderIsLong = NextIsLong;
            settings.ReloadTips();
            reminder = new ReminderForm(settings, sinceLastBreak, false, reminderIsLong);
            reminder.Answered += OnReminderAnswered;
            reminder.Show();
        }

        void CloseReminder()
        {
            if (reminder == null) return;
            reminder.Answered -= OnReminderAnswered;
            reminder.CloseSilently();
            reminder = null;
        }

        void OnReminderAnswered(ReminderResult r)
        {
            reminder = null;
            switch (r)
            {
                case ReminderResult.Done:
                    done++;
                    cycle = reminderIsLong ? 0 : cycle + 1;
                    ResetAfterRest("Отлично! Следующий " + (NextIsLong ? "(большой) " : "") + "перерыв в "
                        + (DateTime.Now + settings.Interval).ToString("HH:mm"));
                    break;
                case ReminderResult.Skip:
                    skipped++;
                    remaining = settings.Interval;
                    statusText = "Перерыв пропущен. Следующий в " + NextAt();
                    break;
                default:
                    snoozed++;
                    remaining = settings.Snooze;
                    statusText = "Напомню через " + settings.SnoozeMin + " мин";
                    break;
            }
            UpdateUi();
        }

        void OpenSettings()
        {
            using (var f = new SettingsForm(settings))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                settings = f.Result;
                settings.Save();
                Autostart.Set(f.AutostartChecked);
                if (remaining > settings.Interval) remaining = settings.Interval;
                ApplyHotkey();
                UpdateUi();
            }
        }

        DayStat TodaySoFar()
        {
            return new DayStat { Minutes = (int)worked.TotalMinutes, Done = done, Skipped = skipped, Snoozed = snoozed };
        }

        void OpenStats()
        {
            using (var f = new StatsForm(state == WorkState.Idle ? null : TodaySoFar()))
                f.ShowDialog(this);
        }

        void SaveHistory()
        {
            History.Append(dayStart, DateTime.Now, TodaySoFar());
        }

        // ---------- отображение ----------

        void UpdateUi()
        {
            blinkPhase = !blinkPhase;
            bool idle = state == WorkState.Idle, paused = state == WorkState.Paused;

            Ui.SetText(lblWork, Ui.Hms(worked));
            lblWork.ForeColor = idle ? Ui.Muted : paused ? (blinkPhase ? Ui.Amber : Ui.Muted) : Ui.Text;
            Ui.SetText(lblStatus, statusText);

            Ui.SetText(btnMain, idle ? "НАЧАТЬ" : "ЗАКОНЧИТЬ");
            btnMain.Fill = idle ? Ui.Green : Ui.Red;

            btnPause.Enabled = !idle;
            btnPause.Glyph = paused ? Glyph.Play : Glyph.Pause;
            btnPause.Fill = paused ? Ui.Amber : Ui.Gray;
            Ui.SetText(lblPause, paused ? "Продолжить" : "Пауза");

            string next;
            if (idle) next = "";
            else if (paused) next = "На паузе · до перерыва " + Ui.Ms(remaining);
            else if (reminder != null) next = reminderIsLong ? "Большой перерыв!" : "Пора отдохнуть!";
            else if (dndReason != null) next = "Перерыв ждёт: " + dndReason;
            else if (away) next = "Отдыхаешь…";
            else next = (NextIsLong ? "До большого перерыва  " : "До перерыва  ") + Ui.Ms(remaining);
            Ui.SetText(lblNext, next);
            lblNext.ForeColor = reminder != null || dndReason != null ? Ui.Amber : NextIsLong && !idle ? Ui.Lighten(Ui.Purple, 0.35f) : Ui.Text;

            Ui.SetText(lblStats, idle && done + skipped + snoozed == 0 ? ""
                : string.Format("Перерывы:  сделано {0}  ·  пропущено {1}  ·  отложено {2}", done, skipped, snoozed));

            // трей
            Icon ico = idle ? icoIdle : paused ? icoPause : reminder != null ? icoAlert : icoRun;
            if (tray.Icon != ico) tray.Icon = ico;
            string tt = idle ? "Отдыхалка — день не начат"
                : paused ? "Пауза · работа " + Ui.Hms(worked)
                : "Работа " + Ui.Hms(worked) + " · перерыв через " + Ui.Ms(remaining);
            if (tt.Length > 63) tt = tt.Substring(0, 63);
            if (tray.Text != tt) tray.Text = tt;

            miPause.Enabled = !idle;
            miPause.Text = paused ? "Продолжить" : "Пауза";
            miMain.Text = idle ? "Начать день" : "Закончить день";
        }

        void ShowMain()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        void ExitApp()
        {
            if (state != WorkState.Idle)
            {
                var owner = Visible ? this : null;
                if (MessageBox.Show(owner, "Рабочий день ещё идёт. Закончить его и выйти?", "Отдыхалка",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }
            exiting = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing && state != WorkState.Idle)
            {
                if (settings.CloseToTray)
                {
                    e.Cancel = true;
                    Hide();
                    if (!trayHintShown)
                    {
                        tray.ShowBalloonTip(4000, "Отдыхалка работает в трее",
                            "Рабочий день продолжается. Кликни по значку, чтобы открыть окно.", ToolTipIcon.Info);
                        trayHintShown = true;
                    }
                    return;
                }
                if (MessageBox.Show(this, "Рабочий день ещё идёт. Закончить его и выйти?", "Отдыхалка",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            if (state != WorkState.Idle) EndDay();
            timer.Stop();
            tray.Visible = false;
            tray.Dispose();
            base.OnFormClosing(e);
        }
    }
}
