using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BreakReminder.Core;

namespace BreakReminder.App;

/// <summary>Новое хобби или изменение: название, значок, язык озвучки; для существующего — «Удалить».</summary>
public sealed class HobbyDialog : Window
{
    /// <summary>Результат: null — отмена.</summary>
    public sealed record Result(string Name, string Emoji, string Lang, bool Delete);

    public HobbyDialog(Hobby? hobby, bool canDelete)
    {
        Title = hobby == null ? "Новое хобби" : "Хобби";
        Icon = Icons.Clock(Palette.Green);
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Palette.B(Palette.Bg);

        var emoji = new TextBox { Text = hobby?.Emoji ?? "🎸", Width = 64, MaxLength = 4, HorizontalContentAlignment = HorizontalAlignment.Center };
        var name = new TextBox { Text = hobby?.Name ?? "", Watermark = "Например: гитара, английский, шахматы", MaxLength = 40 };
        var lang = new ComboBox { ItemsSource = SpeechText.Languages.Select(l => l.Name).ToList(), HorizontalAlignment = HorizontalAlignment.Stretch };
        lang.SelectedIndex = Math.Max(0, Array.FindIndex(SpeechText.Languages, l => l.Code == (hobby?.Lang ?? "")));

        var save = new Button { Content = hobby == null ? "Добавить" : "Сохранить", Classes = { "accent" }, IsDefault = true };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var delete = new Button { Content = "Удалить хобби", IsVisible = hobby != null && canDelete };
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { name.Focus(); return; }
            Close(new Result(name.Text.Trim(), emoji.Text ?? "", SpeechText.Languages[Math.Max(0, lang.SelectedIndex)].Code, false));
        };
        cancel.Click += (_, _) => Close(null);
        delete.Click += async (_, _) =>
        {
            if (await ConfirmDialog.Ask(this, $"Удалить «{hobby!.Label}» вместе с перепиской, заметками, карточками и целями?"))
                Close(new Result(hobby.Name, hobby.Emoji, hobby.Lang, true));
        };

        var nameRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        nameRow.Children.Add(emoji);
        name.Margin = new Thickness(8, 0, 0, 0);
        nameRow.Children.Add(name.WithColumn(1));
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(delete);
        buttons.Children.Add(save.WithColumn(2));
        buttons.Children.Add(cancel.WithColumn(3));

        Content = new StackPanel
        {
            Margin = new Thickness(22, 18),
            Spacing = 8,
            Children =
            {
                Card.Text(hobby == null ? "Новое хобби" : "Хобби", 20, Palette.Text, Avalonia.Media.FontWeight.Bold),
                Card.Text("У каждого хобби своя переписка с помощником, заметки, карточки и цели.", 13, Palette.Muted),
                nameRow,
                Card.Text("Озвучка (карточки, «Послушай и повтори»)", 13, Palette.Muted).WithMargin(0, 6, 0, 0),
                lang,
                buttons
            }
        };
        Opened += (_, _) => name.Focus();
    }
}
