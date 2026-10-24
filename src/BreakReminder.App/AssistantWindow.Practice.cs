using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>Окно помощника: занятия по расписанию и «Сел за домашку» — таймер или «идёт N мин» сверху, итог в конце.</summary>
public sealed partial class AssistantWindow
{
    // ---- занятие по расписанию ----
    PracticeKind? practice;
    int practiceMinutes;
    DateTime practiceStart, practiceEnd;
    bool practiceAnswered, practiceFinishing;
    readonly TextBlock bannerText = Card.Text("", 14, Palette.Text, FontWeight.SemiBold);
    readonly Button bannerButton = new() { Content = "Закончить", VerticalAlignment = VerticalAlignment.Center };
    readonly Border banner;
    readonly DispatcherTimer practiceTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    /// <summary>Занятие закончилось: вид и сколько минут длилось (0 — так и не начал отвечать).</summary>
    public event Action<PracticeKind, int>? PracticeEnded;

    public bool InPractice => practice != null;

    readonly Border homeworkRow;

    // ---------- занятие ----------

    /// <summary>Начать занятие: таймер сверху, помощник сам даёт первое задание.</summary>
    public async Task StartPractice(PracticeKind kind, int minutes)
    {
        if (practice != null) return;
        if (!await AiAvailable())
        {
            PracticeEnded?.Invoke(kind, 0);   // занятие не состоялось — предложим позже
            return;
        }
        practice = kind;
        practiceMinutes = minutes;
        practiceStart = DateTime.Now;
        practiceEnd = practiceStart.AddMinutes(minutes);
        practiceAnswered = practiceFinishing = false;
        banner.IsVisible = true;
        UpdateBanner();
        practiceTimer.Start();
        if (kind == PracticeKind.Homework)
        {
            // фото, которые уже приложены, уходят вместе с началом домашки
            var images = pending.ToList();
            pending.Clear();
            RefreshPending();
            practiceAnswered = images.Count > 0;
            await SendText("📚 Сел за домашнее задание", "Начни: коротко попроси прислать задание и свои ответы (если их ещё нет).", images);
        }
        else
            await SendText($"▶ {PracticeText.Title(kind)} · {minutes} мин", "Начни занятие сразу с первого задания.");
        input.Focus();
    }

    void UpdateBanner()
    {
        if (practice is not { } kind) return;
        homeworkRow.IsVisible = false;
        if (practiceMinutes == 0)
        {
            bannerText.Text = $"{PracticeText.Title(kind)} · идёт {Format.Ms(DateTime.Now - practiceStart)}";
            bannerButton.Content = "Закончить и подвести итог";
            return;
        }
        var left = practiceEnd - DateTime.Now;
        if (left > TimeSpan.Zero)
        {
            bannerText.Text = $"{PracticeText.Title(kind)} · осталось {Format.Ms(left)}";
            bannerText.Foreground = Palette.B(Palette.Text);
            bannerButton.Content = "Закончить";
            bannerButton.Classes.Remove("accent");
        }
        else
        {
            bannerText.Text = "Время вышло — подведём итог?";
            bannerText.Foreground = Palette.B(Palette.Green);
            bannerButton.Content = "Подвести итог";
            if (!bannerButton.Classes.Contains("accent")) bannerButton.Classes.Add("accent");
        }
    }

    /// <summary>«Закончить» / «Подвести итог»: помощник подводит итог, занятие засчитывается.</summary>
    async Task FinishPractice()
    {
        if (practice == null || practiceFinishing) return;
        practiceFinishing = true;
        bannerButton.IsEnabled = false;
        if (practiceAnswered)
            await SendText("Стоп, подведи итог занятия.", "Сейчас подведи итог занятия: 2–3 строки.");
        bannerButton.IsEnabled = true;
        EndPractice();
    }

    void EndPractice()
    {
        if (practice is not { } kind) return;
        practice = null;
        practiceTimer.Stop();
        banner.IsVisible = false;
        homeworkRow.IsVisible = true;
        var elapsed = DateTime.Now - practiceStart;
        int cap = practiceMinutes > 0 ? practiceMinutes * 3 : 240;
        int minutes = practiceAnswered ? Math.Clamp((int)Math.Round(elapsed.TotalMinutes), 1, cap) : 0;
        PracticeEnded?.Invoke(kind, minutes);
    }
}
