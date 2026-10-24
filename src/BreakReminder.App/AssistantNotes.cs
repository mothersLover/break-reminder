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

/// <summary>Обновление заметок помощника по разговору — в фоне, ошибки молча пропускаем (попробуем в следующий раз).</summary>
static class AssistantNotes
{
    static bool running;

    public static async Task UpdateAsync(AppSettings settings, TutorMemory memory, CardStore cards)
    {
        if (running || !settings.AiOn || memory.SummaryRequest(DateTime.Now, cards.RecentFronts(60)) is not { } req) return;
        running = true;
        try
        {
            var reply = await AiClients.Create(settings).CompleteAsync(req.System, new[] { req.Request });
            var (notes, found) = CardStore.Extract(reply);   // заметки + новые карточки из разговора
            if (notes.Length > 0) memory.Notes = notes;
            cards.Add(found, DateTime.Now);
            memory.MarkSummarized(req.Count);
        }
        catch { }
        finally { running = false; }
    }
}
