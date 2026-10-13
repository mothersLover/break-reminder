using System.IO.Pipes;

namespace BreakReminder.App;

/// <summary>
/// Одна копия программы на пользователя. Вторая копия передаёт первой команду
/// ("show" или "toggle-pause") через именованный канал и завершается.
/// </summary>
sealed class SingleInstance : IDisposable
{
    // Своя папка данных (BREAKREMINDER_DATA_DIR, разработка/тесты) — отдельная «одна копия»,
    // чтобы тестовый запуск не трогал обычную программу.
    static string Id => "BreakReminder-" + Environment.UserName
        + (Environment.GetEnvironmentVariable("BREAKREMINDER_DATA_DIR") is { Length: > 0 } dir
            ? "-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(dir)))[..12]
            : "");

    readonly Mutex? mutex;
    public bool IsFirst { get; }

    SingleInstance(Mutex? mutex, bool first)
    {
        this.mutex = mutex;
        IsFirst = first;
    }

    public static SingleInstance Acquire()
    {
        try
        {
            var m = new Mutex(true, Id, out bool created);
            return new SingleInstance(m, created);
        }
        catch
        {
            return new SingleInstance(null, true); // не смогли проверить — просто запускаемся
        }
    }

    public static void Send(string command)
    {
        // первая копия может ещё запускаться — пробуем несколько раз
        for (int i = 0; i < 5; i++)
        {
            try
            {
                using var c = new NamedPipeClientStream(".", Id, PipeDirection.Out);
                c.Connect(500);
                using var w = new StreamWriter(c);
                w.WriteLine(command);
                w.Flush();
                return;
            }
            catch
            {
                Thread.Sleep(300);
            }
        }
    }

    /// <summary>Принимать команды от следующих копий (колбэк вызывается в фоновом потоке).</summary>
    public static void Listen(Action<string> onCommand)
    {
        var t = new Thread(() =>
        {
            bool cleaned = false;
            while (true)
            {
                try
                {
                    using var s = new NamedPipeServerStream(Id, PipeDirection.In, 1);
                    s.WaitForConnection();
                    using var r = new StreamReader(s);
                    var line = r.ReadLine();
                    if (!string.IsNullOrWhiteSpace(line)) onCommand(line.Trim());
                }
                catch (IOException) when (!OperatingSystem.IsWindows() && !cleaned)
                {
                    // На Linux/macOS после аварийного выхода может остаться файл сокета.
                    cleaned = true;
                    try { File.Delete(Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + Id)); } catch { }
                }
                catch
                {
                    Thread.Sleep(1000);
                }
            }
        }) { IsBackground = true, Name = "single-instance" };
        t.Start();
    }

    public void Dispose()
    {
        if (mutex == null) return;
        try { if (IsFirst) mutex.ReleaseMutex(); } catch { }
        mutex.Dispose();
    }
}
