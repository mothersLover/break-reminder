using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BreakReminder
{
    public class SettingsForm : Form
    {
        public AppSettings Result { get; private set; }
        public bool AutostartChecked { get { return chkAutostart.Checked; } }

        readonly AppSettings original;
        readonly NumericUpDown numInterval, numBreak, numSnooze, numRepeat, numLongEvery, numLongMin;
        readonly CheckBox chkIdle, chkBlink, chkSound, chkRepeat, chkTray, chkAutostart, chkLong, chkDndCalls, chkDndFull, chkHotkey;
        readonly ComboBox cmbPopup, cmbSound;
        readonly TrackBar trkVolume;
        readonly Button btnPlay, btnBrowse;
        readonly Label lblCustom;
        readonly TextBox txtTitleShort, txtTitleLong, txtTipsShort, txtTipsLong, txtBtnDone, txtBtnSnooze, txtBtnSkip;
        string customPath;

        // Раскладка: текущий контейнер (вкладка), колонка и «курсор» по вертикали.
        Control host;
        int y;
        int colX;

        static readonly int ColW = Ui.S(485);
        int L { get { return colX + Ui.S(14); } }   // левый отступ колонки
        int C { get { return colX + Ui.S(216); } }  // контролы в колонке

        public SettingsForm(AppSettings s)
        {
            original = s;
            customPath = s.CustomSoundPath;

            Text = "Настройки";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(2 * ColW + Ui.S(24), Ui.S(100));

            var tabs = new TabControl { Location = new Point(Ui.S(8), Ui.S(8)), Width = ClientSize.Width - Ui.S(16), Padding = new Point(Ui.S(14), Ui.S(5)) };
            var pageMain = new TabPage("Основные") { UseVisualStyleBackColor = true };
            var pageTexts = new TabPage("Тексты уведомлений") { UseVisualStyleBackColor = true };
            tabs.TabPages.Add(pageMain);
            tabs.TabPages.Add(pageTexts);
            Controls.Add(tabs);

            // =============== Вкладка «Основные» ===============
            host = pageMain;

            // ---- левая колонка ----
            colX = 0;
            y = Ui.S(4);
            Header("Напоминания");
            numInterval = NumRow("Напоминать каждые", s.IntervalMin, 1, 240, "мин");
            numBreak = NumRow("Длительность перерыва", s.BreakMin, 1, 60, "мин");
            numSnooze = NumRow("Кнопка «Отложить» на", s.SnoozeMin, 1, 60, "мин");
            chkIdle = Check("Если я отходил от компьютера дольше перерыва (сон,\nблокировка, нет активности) — считать это отдыхом", s.IdleDetect, 2);

            Header("Большие перерывы");
            chkLong = Check("Включить большие перерывы (как в Помодоро)", s.LongBreaks, 1);
            numLongEvery = NumRow("Большим будет каждый", s.LongEvery, 2, 10, "-й перерыв");
            numLongMin = NumRow("Длительность большого", s.LongBreakMin, 5, 120, "мин");

            Header("Не беспокоить");
            chkDndCalls = Check("Не показывать, пока идёт созвон\n(занят микрофон или камера)", s.DndCalls, 2);
            chkDndFull = Check("Не показывать поверх полноэкранных программ\n(видео, игры, презентации)", s.DndFullscreen, 2);
            int mainBottom = y;

            // ---- правая колонка ----
            colX = ColW;
            y = Ui.S(4);
            Header("Уведомление");
            cmbPopup = ComboRow("Вид", new[] { "Всплывающее окно в углу", "Окно по центру экрана", "Затемнение всего экрана" }, (int)s.Popup);
            chkBlink = Check("Мигание (рамка окна и кнопка на панели задач)", s.Blink, 1);

            Header("Звук");
            chkSound = Check("Включить звук", s.SoundOn, 1);
            cmbSound = ComboRow("Мелодия", Sounds.Names, Math.Max(0, Array.IndexOf(Sounds.Keys, s.Sound)));
            cmbSound.Width = Ui.S(190);
            btnPlay = new Button { Text = "▶", Bounds = new Rectangle(C + Ui.S(198), cmbSound.Top - 1, Ui.S(40), cmbSound.Height + 2) };
            btnPlay.Click += delegate { Sounds.Play(Collect()); };
            host.Controls.Add(btnPlay);

            btnBrowse = new Button { Text = "Выбрать файл…", Bounds = new Rectangle(C, y, Ui.S(120), Ui.S(28)) };
            lblCustom = new Label { AutoSize = false, AutoEllipsis = true, ForeColor = SystemColors.GrayText, Bounds = new Rectangle(C + Ui.S(128), y + Ui.S(5), Ui.S(120), Ui.S(20)) };
            btnBrowse.Click += OnBrowse;
            host.Controls.Add(btnBrowse);
            host.Controls.Add(lblCustom);
            y += Ui.S(34);

            AddLabel("Громкость (встроенные)", y + Ui.S(6));
            trkVolume = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, Value = s.Volume, Bounds = new Rectangle(C - Ui.S(6), y, Ui.S(250), Ui.S(34)) };
            trkVolume.BackColor = SystemColors.Window; // фон вкладки с визуальными стилями — белый
            host.Controls.Add(trkVolume);
            y += Ui.S(40);

            chkRepeat = new CheckBox { Text = "Повторять звук каждые", Checked = s.RepeatSound, AutoSize = true, Location = new Point(L, y + Ui.S(3)) };
            numRepeat = new NumericUpDown { Minimum = 10, Maximum = 600, Increment = 10, Value = Clamp(s.RepeatSec, 10, 600), Bounds = new Rectangle(C, y, Ui.S(70), Ui.S(26)) };
            host.Controls.Add(chkRepeat);
            host.Controls.Add(numRepeat);
            host.Controls.Add(new Label { Text = "сек, пока не отвечу", AutoSize = true, Location = new Point(C + Ui.S(78), y + Ui.S(4)) });
            y += Ui.S(34);

            Header("Общие");
            chkTray = Check("Крестик сворачивает в трей, пока идёт рабочий день", s.CloseToTray, 1);
            chkAutostart = Check("Запускать вместе с Windows", Autostart.Get(), 1);
            chkHotkey = Check("Пауза / продолжить по Ctrl+Alt+P (из любой программы)", s.PauseHotkey, 1);
            mainBottom = Math.Max(mainBottom, y);

            // =============== Вкладка «Тексты уведомлений» ===============
            host = pageTexts;
            int tipsH = Ui.S(150);

            colX = 0;
            y = Ui.S(4);
            Header("Обычный перерыв");
            txtTitleShort = TextRow("Заголовок", s.TitleShort);
            txtTipsShort = TipsBox("Советы — по одному на строку, показывается случайный:", s.TipsShort, tipsH);

            colX = ColW;
            y = Ui.S(4);
            Header("Большой перерыв");
            txtTitleLong = TextRow("Заголовок", s.TitleLong);
            txtTipsLong = TipsBox("Советы — по одному на строку, показывается случайный:", s.TipsLong, tipsH);

            colX = 0;
            Header("Кнопки");
            txtBtnDone = TextRow("«Сделал»", s.BtnDone);
            txtBtnSnooze = TextRow("«Отложить»  ({N} — минуты)", s.BtnSnooze);
            txtBtnSkip = TextRow("«Пропустить»", s.BtnSkip);
            int textsBottom = y;

            colX = ColW;
            y = txtBtnDone.Top - Ui.S(2);
            var btnPreviewShort = new Button { Text = "Показать обычное", Bounds = new Rectangle(L, y, Ui.S(150), Ui.S(30)) };
            var btnPreviewLong = new Button { Text = "Показать большое", Bounds = new Rectangle(L + Ui.S(158), y, Ui.S(150), Ui.S(30)) };
            var btnResetTexts = new Button { Text = "Стандартные тексты", Bounds = new Rectangle(L, y + Ui.S(38), Ui.S(150), Ui.S(30)) };
            var lnkFolder = new LinkLabel { Text = "Папка с файлами советов", AutoSize = true, Location = new Point(L + Ui.S(160), y + Ui.S(44)) };
            btnPreviewShort.Click += delegate { new ReminderForm(Collect(), TimeSpan.FromMinutes(52), true, false).Show(); };
            btnPreviewLong.Click += delegate { new ReminderForm(Collect(), TimeSpan.FromMinutes(52), true, true).Show(); };
            btnResetTexts.Click += delegate { ResetTexts(); };
            lnkFolder.LinkClicked += delegate
            {
                Directory.CreateDirectory(AppSettings.DataDir);
                Process.Start("explorer.exe", "\"" + AppSettings.DataDir + "\"");
            };
            host.Controls.AddRange(new Control[] { btnPreviewShort, btnPreviewLong, btnResetTexts, lnkFolder });
            host.Controls.Add(new Label
            {
                Text = "Новые тексты применяются сразу после «Сохранить» —\nк следующему уведомлению, перезапуск не нужен.",
                ForeColor = SystemColors.GrayText,
                AutoSize = true,
                Location = new Point(L, y + Ui.S(78))
            });

            // =============== низ окна ===============
            int pageH = Math.Max(mainBottom, textsBottom) + Ui.S(10);
            tabs.Height = pageH + Ui.S(36); // + заголовки вкладок и рамка
            y = tabs.Bottom + Ui.S(10);

            var btnTest = new Button { Text = "Проверить уведомление", Bounds = new Rectangle(Ui.S(10), y, Ui.S(180), Ui.S(32)) };
            var btnOk = new Button { Text = "Сохранить", Bounds = new Rectangle(ClientSize.Width - Ui.S(202), y, Ui.S(95), Ui.S(32)) };
            var btnCancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(ClientSize.Width - Ui.S(99), y, Ui.S(89), Ui.S(32)) };
            btnTest.Click += delegate { new ReminderForm(Collect(), TimeSpan.FromMinutes(52), true, false).Show(); };
            btnOk.Click += delegate
            {
                Result = Collect();
                DialogResult = DialogResult.OK;
            };
            Controls.Add(btnTest);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            ClientSize = new Size(ClientSize.Width, y + Ui.S(44));

            chkSound.CheckedChanged += delegate { UpdateEnabled(); };
            chkRepeat.CheckedChanged += delegate { UpdateEnabled(); };
            cmbSound.SelectedIndexChanged += delegate { UpdateEnabled(); };
            chkLong.CheckedChanged += delegate { UpdateEnabled(); };
            UpdateEnabled();
        }

        static decimal Clamp(int v, int min, int max)
        {
            return Math.Max(min, Math.Min(max, v));
        }

        void Header(string text)
        {
            y += Ui.S(6);
            var l = new Label
            {
                Text = text,
                Font = new Font("Segoe UI Semibold", 11f),
                ForeColor = Color.FromArgb(40, 110, 200),
                AutoSize = true,
                Location = new Point(L - Ui.S(4), y)
            };
            host.Controls.Add(l);
            y += Ui.S(30);
        }

        void AddLabel(string text, int top)
        {
            host.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(L, top) });
        }

        NumericUpDown NumRow(string label, int value, int min, int max, string unit)
        {
            AddLabel(label, y + Ui.S(4));
            var n = new NumericUpDown { Minimum = min, Maximum = max, Value = Clamp(value, min, max), Bounds = new Rectangle(C, y, Ui.S(70), Ui.S(26)) };
            host.Controls.Add(n);
            host.Controls.Add(new Label { Text = unit, AutoSize = true, Location = new Point(C + Ui.S(78), y + Ui.S(4)) });
            y += Ui.S(34);
            return n;
        }

        ComboBox ComboRow(string label, string[] items, int index)
        {
            AddLabel(label, y + Ui.S(4));
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(C, y, Ui.S(240), Ui.S(26)) };
            c.Items.AddRange(items);
            c.SelectedIndex = Math.Max(0, Math.Min(items.Length - 1, index));
            host.Controls.Add(c);
            y += Ui.S(34);
            return c;
        }

        CheckBox Check(string text, bool value, int lines)
        {
            var c = new CheckBox { Text = text, Checked = value, AutoSize = true, Location = new Point(L, y) };
            host.Controls.Add(c);
            y += Ui.S(lines == 1 ? 30 : 48);
            return c;
        }

        TextBox TextRow(string label, string value)
        {
            AddLabel(label, y + Ui.S(4));
            var t = new TextBox { Text = value, Bounds = new Rectangle(C, y, ColW - (C - colX) - Ui.S(14), Ui.S(26)) };
            host.Controls.Add(t);
            y += Ui.S(34);
            return t;
        }

        TextBox TipsBox(string label, string[] tips, int height)
        {
            AddLabel(label, y + Ui.S(2));
            y += Ui.S(24);
            var t = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = false,
                Text = string.Join(Environment.NewLine, tips),
                Bounds = new Rectangle(L, y, ColW - Ui.S(28), height)
            };
            host.Controls.Add(t);
            y += height + Ui.S(8);
            return t;
        }

        void ResetTexts()
        {
            txtTitleShort.Text = Texts.TitleShort;
            txtTitleLong.Text = Texts.TitleLong;
            txtTipsShort.Text = string.Join(Environment.NewLine, Texts.Tips);
            txtTipsLong.Text = string.Join(Environment.NewLine, Texts.LongTips);
            txtBtnDone.Text = Texts.BtnDone;
            txtBtnSnooze.Text = Texts.BtnSnooze;
            txtBtnSkip.Text = Texts.BtnSkip;
        }

        void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "Звук WAV (*.wav)|*.wav", Title = "Выберите мелодию" })
            {
                if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                    dlg.InitialDirectory = Path.GetDirectoryName(customPath);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    customPath = dlg.FileName;
                    cmbSound.SelectedIndex = Array.IndexOf(Sounds.Keys, "custom");
                    UpdateEnabled();
                }
            }
        }

        void UpdateEnabled()
        {
            bool snd = chkSound.Checked;
            bool custom = Sounds.Keys[cmbSound.SelectedIndex] == "custom";
            cmbSound.Enabled = btnPlay.Enabled = chkRepeat.Enabled = snd;
            btnBrowse.Enabled = lblCustom.Enabled = snd && custom;
            trkVolume.Enabled = snd && cmbSound.SelectedIndex < 3; // первые три — встроенные мелодии
            numRepeat.Enabled = snd && chkRepeat.Checked;
            numLongEvery.Enabled = numLongMin.Enabled = chkLong.Checked;
            lblCustom.Text = string.IsNullOrEmpty(customPath) ? "файл не выбран" : Path.GetFileName(customPath);
        }

        AppSettings Collect()
        {
            var s = original.Clone();
            s.IntervalMin = (int)numInterval.Value;
            s.BreakMin = (int)numBreak.Value;
            s.SnoozeMin = (int)numSnooze.Value;
            s.IdleDetect = chkIdle.Checked;
            s.LongBreaks = chkLong.Checked;
            s.LongEvery = (int)numLongEvery.Value;
            s.LongBreakMin = (int)numLongMin.Value;
            s.DndCalls = chkDndCalls.Checked;
            s.DndFullscreen = chkDndFull.Checked;
            s.PauseHotkey = chkHotkey.Checked;
            s.Popup = (PopupMode)cmbPopup.SelectedIndex;
            s.Blink = chkBlink.Checked;
            s.SoundOn = chkSound.Checked;
            s.Sound = Sounds.Keys[cmbSound.SelectedIndex];
            s.CustomSoundPath = customPath ?? "";
            s.Volume = trkVolume.Value;
            s.RepeatSound = chkRepeat.Checked;
            s.RepeatSec = (int)numRepeat.Value;
            s.CloseToTray = chkTray.Checked;

            s.TitleShort = OrDefault(txtTitleShort.Text, Texts.TitleShort);
            s.TitleLong = OrDefault(txtTitleLong.Text, Texts.TitleLong);
            s.TipsShort = Texts.ParseLines(txtTipsShort.Text, Texts.Tips);
            s.TipsLong = Texts.ParseLines(txtTipsLong.Text, Texts.LongTips);
            s.BtnDone = OrDefault(txtBtnDone.Text, Texts.BtnDone);
            s.BtnSnooze = OrDefault(txtBtnSnooze.Text, Texts.BtnSnooze);
            s.BtnSkip = OrDefault(txtBtnSkip.Text, Texts.BtnSkip);
            return s;
        }

        static string OrDefault(string v, string def)
        {
            v = (v ?? "").Trim();
            return v.Length > 0 ? v : def;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Sounds.Stop();
            base.OnFormClosed(e);
        }
    }
}
