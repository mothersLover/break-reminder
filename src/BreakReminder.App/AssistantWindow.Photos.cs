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

/// <summary>Окно помощника: фото к сообщению — 📎, перетаскивание, миниатюры до отправки.</summary>
public sealed partial class AssistantWindow
{
    // ---- фото к сообщению ----
    readonly List<string> pending = new();          // подготовленные фото, ещё не отправленные
    readonly WrapPanel pendingStrip = new() { Margin = new Thickness(0, 0, 0, 8), IsVisible = false };
    readonly Button attach = new() { Content = "📎", FontSize = 18, VerticalAlignment = VerticalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(12, 0) };

    // ---------- фото ----------

    async Task PickImages()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Фото задания",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Фото") { Patterns = AiImages.Extensions.Select(e => "*" + e).ToArray() } }
        });
        AddImages(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToList());
    }

    const int MaxImages = 4;

    void AddImages(IReadOnlyList<string> paths)
    {
        var errors = new List<string>();
        foreach (var path in paths)
        {
            if (pending.Count >= MaxImages) { errors.Add($"Не больше {MaxImages} фото за раз."); break; }
            try { pending.Add(ImagePrep.Prepare(path, memory.ImagesDir)); }
            catch (Exception e) { errors.Add(e.Message); }
        }
        RefreshPending();
        if (errors.Count > 0) list.Children.Add(ErrorBubble(string.Join("\n", errors.Distinct())));
        ScrollToEnd();
        input.Focus();
    }

    void RefreshPending()
    {
        pendingStrip.Children.Clear();
        foreach (var path in pending)
        {
            var remove = new Button
            {
                Content = "✕", FontSize = 11, Padding = new Thickness(5, 1), Background = Palette.B(Palette.Gray),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top
            };
            ToolTip.SetTip(remove, "Убрать фото");
            string p = path;
            remove.Click += (_, _) =>
            {
                pending.Remove(p);
                try { File.Delete(p); } catch { }
                RefreshPending();
            };
            pendingStrip.Children.Add(new Panel { Margin = new Thickness(0, 0, 8, 0), Children = { Thumb(path, 64), remove } });
        }
        pendingStrip.IsVisible = pending.Count > 0;
        input.Watermark = pending.Count > 0
            ? "Добавь пояснение к фото (необязательно) и нажми Enter"
            : "Напиши вопрос или своё решение…  (Enter — отправить, Shift+Enter — новая строка)";
    }
}
