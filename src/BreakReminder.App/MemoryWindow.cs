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

/// <summary>«Что помощник обо мне знает»: профиль (пишет человек) и заметки (ведёт помощник). Можно править и стереть.</summary>
public sealed class MemoryWindow : Window
{
    public MemoryWindow(TutorMemory memory)
    {
        Title = "Память помощника";
        Icon = Icons.Clock(Palette.Green);
        Width = 640;
        Height = 680;
        MinWidth = 480;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Palette.B(Palette.Bg);

        var profileBox = new TextBox
        {
            Text = memory.Profile, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 3000,
            Watermark = "Например: Учу французский по учебнику Édito A2, сейчас Unité 5. Цель — к лету говорить с коллегами. " +
                        "Занимаюсь по вечерам 20–30 минут. Путаю être и avoir в passé composé."
        };
        var notesBox = new TextBox
        {
            Text = memory.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = TutorMemory.MaxNotesChars,
            Watermark = "Пока пусто — заметки появятся после первого разговора."
        };

        var save = new Button { Content = "Сохранить", Classes = { "accent" }, IsDefault = true };
        var cancel = new Button { Content = "Отмена", IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
        var forget = new Button { Content = "Забыть всё" };
        save.Click += (_, _) =>
        {
            memory.Profile = profileBox.Text ?? "";
            memory.Notes = notesBox.Text ?? "";
            Close();
        };
        cancel.Click += (_, _) => Close();
        forget.Click += async (_, _) =>
        {
            if (!await ConfirmDialog.Ask(this, "Стереть профиль, заметки, закреплённое и переписку? Помощник забудет всё, что знал о тебе.")) return;
            memory.ForgetAll();
            Close();
        };

        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(forget);
        Grid.SetColumn(save, 2);
        buttons.Children.Add(save);
        Grid.SetColumn(cancel, 3);
        buttons.Children.Add(cancel);

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,2*,Auto,Auto,3*,Auto"), Margin = new Thickness(20, 16, 20, 16) };
        void Add(Control c, int row) { Grid.SetRow(c, row); root.Children.Add(c); }
        Add(Card.Text("О себе", 16, Palette.Text, FontWeight.SemiBold), 0);
        Add(new TextBlock
        {
            Classes = { "hint" }, Margin = new Thickness(0, 2, 0, 6),
            Text = "Чем занимаешься, по какому учебнику, где остановился, чего хочешь добиться. Помощник видит это в каждом разговоре."
        }, 1);
        Add(profileBox, 2);
        Add(Card.Text("Заметки помощника", 16, Palette.Text, FontWeight.SemiBold).WithMargin(0, 14, 0, 0), 3);
        Add(new TextBlock
        {
            Classes = { "hint" }, Margin = new Thickness(0, 2, 0, 6),
            Text = "Помощник сам обновляет их после разговора: что пройдено, типичные ошибки, договорённости. Можно поправить руками."
        }, 4);
        Add(notesBox, 5);
        Add(buttons, 6);
        Content = root;
    }
}
