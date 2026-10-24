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

/// <summary>Окно помощника: закреплённые сообщения — панель сверху, 📌 на пузырях.</summary>
public sealed partial class AssistantWindow
{
    // ---- закреплённое ----
    readonly Expander pinsPanel = new() { IsVisible = false, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 12, 0, 0) };
    readonly StackPanel pinsList = new() { Spacing = 6 };
    readonly List<Action> pinSyncs = new();         // обновить 📌 на пузырях, когда закреплённое изменилось

    /// <summary>Панель «Закреплено»: самое новое сверху; щелчок по тексту — развернуть, ✕ — открепить.</summary>
    void RebuildPins()
    {
        foreach (var sync in pinSyncs) sync();
        pinsList.Children.Clear();
        var items = memory.Pins.Reverse().ToList();
        pinsPanel.IsVisible = items.Count > 0;
        if (items.Count == 0) return;
        string first = items[0].Text.Split('\n')[0];
        pinsPanel.Header = new TextBlock
        {
            Text = $"📌 Закреплено · {items.Count}   {first}", FontSize = 13.5, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = Palette.B(Palette.Text), MaxWidth = 500
        };
        foreach (var pin in items)
        {
            var text = new SelectableTextBlock
            {
                Text = pin.Text, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Palette.B(Palette.Text)
            };
            var more = new Button { Content = "развернуть", Classes = { "link" }, FontSize = 12 };
            more.Click += (_, _) =>
            {
                text.MaxLines = text.MaxLines == 3 ? 0 : 3;
                more.Content = text.MaxLines == 0 ? "свернуть" : "развернуть";
            };
            var unpin = new Button
            {
                Content = "✕", Background = Brushes.Transparent, Foreground = Palette.B(Palette.Muted),
                Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Top
            };
            ToolTip.SetTip(unpin, "Открепить");
            string id = pin.Id;
            unpin.Click += (_, _) => memory.Unpin(id);
            var body = new StackPanel
            {
                Spacing = 2,
                Children = { Card.Text($"{pin.At:d MMMM, HH:mm} · {(pin.FromUser ? "ты" : "помощник")}", 11.5, Palette.Muted), text }
            };
            if (pin.Text.Length > 160 || pin.Text.Count(c => c == '\n') > 2) body.Children.Add(more);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            row.Children.Add(body);
            Grid.SetColumn(unpin, 1);
            row.Children.Add(unpin);
            pinsList.Children.Add(new Border
            {
                Background = Palette.B(Palette.Card), BorderBrush = Palette.B(Palette.Amber), BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 6), Child = row
            });
        }
    }

    void TogglePin(string text, bool fromUser)
    {
        if (memory.IsPinned(text)) memory.UnpinText(text);
        else if (!memory.Pin(text, fromUser, DateTime.Now))
            list.Children.Add(ErrorBubble($"Закреплено уже {TutorMemory.MaxPins} — открепи что-нибудь ненужное."));
        if (memory.IsPinned(text)) pinsPanel.IsExpanded = true;   // показать, куда попало
    }
}
