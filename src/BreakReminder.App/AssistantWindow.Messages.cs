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

/// <summary>Окно помощника: пузыри сообщений — текст, фото, 🔊 и 📌 при наведении, меню правой кнопки.</summary>
public sealed partial class AssistantWindow
{
    /// <summary>Произнести иностранные фразы из сообщения (русские пояснения — пропускаются).</summary>
    void SpeakForeign(string text)
    {
        var parts = SpeechText.Foreign(text);
        if (platform == null || parts.Count == 0) return;
        _ = platform.SpeakAsync(string.Join(". ", parts), settings().SpeechLang, false);
    }

    async Task CopyText(string text)
    {
        if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text);
    }

    /// <summary>Меню по правой кнопке на сообщении: закрепить/открепить, копировать.</summary>
    ContextMenu MessageMenu(string text, bool fromUser)
    {
        var pin = new MenuItem();
        var copy = new MenuItem { Header = "Копировать" };
        pin.Click += (_, _) => TogglePin(text, fromUser);
        copy.Click += async (_, _) => await CopyText(text);
        var menu = new ContextMenu { Items = { pin, copy } };
        if (platform != null && SpeechText.Foreign(text).Count > 0)
        {
            var listen = new MenuItem { Header = "🔊 Прослушать" };
            listen.Click += (_, _) => SpeakForeign(text);
            menu.Items.Insert(0, listen);
        }
        menu.Opening += (_, _) => pin.Header = memory.IsPinned(text) ? "Открепить" : "📌 Закрепить";
        return menu;
    }

    Control Bubble(string text, bool user, bool muted = false, IReadOnlyList<string>? images = null, bool pinnable = true)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (images is { Count: > 0 })
        {
            var strip = new WrapPanel();
            foreach (var path in images) strip.Children.Add(Thumb(path, 150));
            panel.Children.Add(strip);
        }
        pinnable = pinnable && !muted && text.Trim().Length > 0;
        SelectableTextBlock? body = null;
        if (text.Length > 0)
            panel.Children.Add(body = new SelectableTextBlock
            {
                Text = text, FontSize = 14, TextWrapping = TextWrapping.Wrap,
                Foreground = Palette.B(muted ? Palette.Muted : Palette.Text),
                FontStyle = muted ? FontStyle.Italic : FontStyle.Normal
            });
        var bubble = new Border
        {
            Background = Palette.B(user ? Color.Parse("#2D4A63") : Palette.Card),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10),
            MaxWidth = 470,
            HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            Child = panel
        };
        if (!pinnable) return bubble;

        // меню и на пузыре, и на тексте (у выделяемого текста своё меню перехватило бы правый щелчок)
        bubble.ContextMenu = MessageMenu(text, user);
        if (body != null) body.ContextMenu = MessageMenu(text, user);

        // в углу при наведении: 🔊 (если есть иностранные фразы) и 📌 (видна всегда, если сообщение закреплено)
        var pinButton = new Button
        {
            Content = "📌", FontSize = 12, Padding = new Thickness(5, 1), Background = Palette.B(Palette.Gray),
            IsVisible = memory.IsPinned(text)
        };
        var speakButton = new Button
        {
            Content = "🔊", FontSize = 12, Padding = new Thickness(5, 1), Background = Palette.B(Palette.Gray), IsVisible = false
        };
        bool speakable = platform != null && SpeechText.Foreign(text).Count > 0;
        ToolTip.SetTip(speakButton, "Прослушать фразы на изучаемом языке");
        speakButton.Click += (_, _) => SpeakForeign(text);
        void Sync(bool hover)
        {
            bool pinned = memory.IsPinned(text);
            pinButton.IsVisible = hover || pinned;
            pinButton.Opacity = pinned ? 1 : 0.75;
            speakButton.IsVisible = hover && speakable;
            ToolTip.SetTip(pinButton, pinned ? "Открепить" : "Закрепить сверху чата");
        }
        pinButton.Click += (_, _) => { TogglePin(text, user); Sync(true); };
        var corner = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -8, -8, 0), Children = { speakButton, pinButton }
        };
        var holder = new Panel { HorizontalAlignment = bubble.HorizontalAlignment, Children = { bubble, corner } };
        holder.PointerEntered += (_, _) => Sync(true);
        holder.PointerExited += (_, _) => Sync(false);
        pinSyncs.Add(() => Sync(holder.IsPointerOver));
        return holder;
    }

    /// <summary>Миниатюра фото (если файла уже нет — подпись).</summary>
    static Control Thumb(string path, int height)
    {
        try
        {
            using var f = File.OpenRead(path);
            return new Image
            {
                Source = Bitmap.DecodeToHeight(f, height * 2), Height = height, Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 6, 6), HorizontalAlignment = HorizontalAlignment.Left
            };
        }
        catch
        {
            return Card.Text("[фото: " + Path.GetFileName(path) + "]", 12.5, Palette.Muted);
        }
    }

    static Control ErrorBubble(string text) => new Border
    {
        Background = Palette.B(Palette.Card),
        BorderBrush = Palette.B(Palette.Red),
        BorderThickness = new Thickness(4, 0, 0, 0),
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(12, 8),
        MaxWidth = 470,
        HorizontalAlignment = HorizontalAlignment.Left,
        Child = new SelectableTextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Palette.B(Palette.Text) }
    };
}
