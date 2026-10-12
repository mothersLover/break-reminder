using Avalonia.Controls;
using BreakReminder.Core;

namespace BreakReminder.App;

public partial class MainWindow : Window
{
    public event Action? MainClicked, PauseClicked, StatsClicked, SettingsClicked;

    public MainWindow()
    {
        InitializeComponent();
        PauseIcon.Data = Glyphs.Pause;
        StatsIcon.Data = Glyphs.Chart;
        SettingsIcon.Data = Glyphs.Gear;
        MainButton.Click += (_, _) => MainClicked?.Invoke();
        PauseButton.Click += (_, _) => PauseClicked?.Invoke();
        StatsButton.Click += (_, _) => StatsClicked?.Invoke();
        SettingsButton.Click += (_, _) => SettingsClicked?.Invoke();
        ToolTip.SetTip(StatsButton, "Статистика по неделям");
        ToolTip.SetTip(SettingsButton, "Настройки");
    }

    public void SetHotkeyName(string? name) =>
        ToolTip.SetTip(PauseButton, "Пауза: останавливает и время работы, и отсчёт до перерыва"
                                    + (name != null ? $" ({name})" : ""));

    public void Render(WorkDayEngine e, DateTime now, bool blinkPhase)
    {
        bool idle = e.State == WorkState.Idle, paused = e.State == WorkState.Paused;

        WorkText.Text = Format.Hms(e.Worked);
        WorkText.Foreground = Palette.B(idle ? Palette.Muted : paused ? (blinkPhase ? Palette.Amber : Palette.Muted) : Palette.Text);
        StatusText.Text = e.StatusText;

        MainButtonText.Text = idle ? "НАЧАТЬ" : "ЗАКОНЧИТЬ";
        MainButton.Background = Palette.B(idle ? Palette.Green : Palette.Red);

        PauseButton.IsEnabled = !idle;
        PauseIcon.Data = paused ? Glyphs.Play : Glyphs.Pause;
        PauseButton.Background = Palette.B(paused ? Palette.Amber : Palette.Gray);
        PauseCaption.Text = paused ? "Продолжить" : "Пауза";

        string next;
        if (idle) next = e.AutoStartHint(now);
        else if (paused) next = "На паузе · до перерыва " + Format.Ms(e.Remaining);
        else if (e.ReminderOpen) next = e.ReminderIsLong ? "Большой перерыв!" : "Пора отдохнуть!";
        else if (e.DndReason != null) next = "Перерыв ждёт: " + e.DndReason;
        else if (e.Away) next = "Отдыхаешь…";
        else next = (e.NextIsLong ? "До большого перерыва  " : "До перерыва  ") + Format.Ms(e.Remaining);
        NextText.Text = next;
        NextText.Foreground = Palette.B(
            e.ReminderOpen || e.DndReason != null ? Palette.Amber
            : e.NextIsLong && !idle ? Palette.PurpleLight
            : idle ? Palette.Muted
            : Palette.Text);
        NextText.FontSize = idle ? 15 : 19;

        StatsText.Text = idle && e.Done + e.Skipped + e.Snoozed == 0
            ? ""
            : $"Перерывы:  сделано {e.Done}  ·  пропущено {e.Skipped}  ·  отложено {e.Snoozed}";
    }
}
