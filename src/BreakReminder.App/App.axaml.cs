using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BreakReminder.App;

public partial class App : Application
{
    AppController? controller;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Окно может быть спрятано в трей — приложение живёт, пока не выберут «Выход».
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            controller = new AppController(desktop, Program.StartHidden);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
