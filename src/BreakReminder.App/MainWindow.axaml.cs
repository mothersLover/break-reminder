using Avalonia.Automation;
using Avalonia.Controls;
using BreakReminder.Core;

namespace BreakReminder.App;

public partial class MainWindow : Window
{
    public event Action? MainClicked, PauseClicked, StatsClicked, SettingsClicked, AgendaClicked, AssistantClicked;

    /// <summary>Сколько осталось на сегодня (встречи + невыполненные задачи) — в подписи ссылки.</summary>
    public void SetAgendaCount(int n) =>
        AgendaLink.Content = n > 0 ? $"Все задачи на сегодня ({n}) →" : "Все задачи на сегодня →";

    /// <summary>Ссылка «Помощник» — только когда ИИ-помощник включён в настройках.</summary>
    public void SetAssistantVisible(bool on, bool offline = false)
    {
        AssistantLink.IsVisible = AssistantSep.IsVisible = on;
        AssistantLink.Content = offline ? "Помощник (нет связи)" : "Помощник →";
        AssistantLink.Opacity = offline ? 0.6 : 1;
        ToolTip.SetTip(AssistantLink, offline
            ? "Прогноз погоды недоступен — считаю, что связи нет, и к ИИ не обращаюсь. Карточки работают."
            : "ИИ-помощник: вопросы, проверка заданий, тренировка пройденного");
    }

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
        AgendaLink.Click += (_, _) => AgendaClicked?.Invoke();
        AssistantLink.Click += (_, _) => AssistantClicked?.Invoke();
        ToolTip.SetTip(AssistantLink, "ИИ-помощник: вопросы, проверка заданий, тренировка пройденного");
        ToolTip.SetTip(StatsButton, "Статистика по неделям");
        // у круглых кнопок внутри значки — имена для экранного диктора
        AutomationProperties.SetName(MainButton, "Начать или закончить рабочий день");
        AutomationProperties.SetName(PauseButton, "Пауза");
        AutomationProperties.SetName(StatsButton, "Статистика");
        AutomationProperties.SetName(SettingsButton, "Настройки");
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
        else if (e.OnBreak) next = (e.ReminderIsLong ? "Большой перерыв" : "Перерыв") + " · осталось " + Format.Ms(e.BreakUntil - now);
        else if (e.ReminderOpen) next = e.ReminderIsLong ? "Большой перерыв!" : "Пора отдохнуть!";
        else if (e.DndReason != null) next = "Перерыв ждёт: " + e.DndReason;
        else if (e.Away) next = "Отдыхаешь…";
        else next = (e.NextIsLong ? "До большого перерыва  " : "До перерыва  ") + Format.Ms(e.Remaining);
        NextText.Text = next;
        NextText.Foreground = Palette.B(
            e.OnBreak ? Palette.Green
            : e.ReminderOpen || e.DndReason != null ? Palette.Amber
            : e.NextIsLong && !idle ? Palette.PurpleLight
            : idle ? Palette.Muted
            : Palette.Text);
        NextText.FontSize = idle ? 15 : 19;

        StatsText.Text = idle && e.Done + e.Skipped + e.Snoozed == 0
            ? ""
            : $"Перерывы:  сделано {e.Done}  ·  пропущено {e.Skipped}  ·  отложено {e.Snoozed}";

        // календарь: текущая или ближайшая встреча сегодня
        var cur = e.CurrentMeeting(now);
        var nxt = cur == null ? e.NextMeeting(now) : null;
        MeetingText.Text = cur != null ? $"Сейчас встреча: {cur.Title} до {cur.End:HH:mm}"
            : nxt != null && nxt.Start.Date == now.Date ? $"Встреча в {nxt.Start:HH:mm}: {nxt.Title}"
            : "";
    }
}
