using Avalonia;

namespace BreakReminder.App;

static class Program
{
    /// <summary>Запуск с --tray (автозапуск ОС): окно не показываем, сразу в трей.</summary>
    public static bool StartHidden;

    /// <summary>Программа только что обновилась (запущена старой версией с --after-update).</summary>
    public static bool AfterUpdate;

    [STAThread]
    public static void Main(string[] args)
    {
        // После обновления: дождаться выхода старой версии, иначе она ещё держит «одну копию».
        int i = Array.IndexOf(args, "--after-update");
        if (i >= 0)
        {
            AfterUpdate = true;
            if (i + 1 < args.Length && int.TryParse(args[i + 1], out int oldPid)) Updater.FinishAfterUpdate(oldPid);
        }

        // --toggle-pause: для горячей клавиши, назначенной в системе (Linux/Wayland), — передаём команду и выходим.
        string command = args.Contains("--toggle-pause") ? "toggle-pause" : "show";

        using var instance = SingleInstance.Acquire();
        if (!instance.IsFirst)
        {
            SingleInstance.Send(command); // уже запущено: показать окно / переключить паузу
            return;
        }
        if (command == "toggle-pause") return; // программа не запущена — переключать нечего

        StartHidden = args.Contains("--tray");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
