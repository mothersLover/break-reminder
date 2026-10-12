using System.Diagnostics;

namespace BreakReminder.Platform.Shared;

/// <summary>Запуск внешних утилит (gdbus, pactl, afplay…) — для Linux и macOS.</summary>
static class Cmd
{
    /// <summary>Выполнить и вернуть stdout, или null (нет утилиты, ошибка, таймаут).</summary>
    public static string? Run(string file, string args, int timeoutMs = 1500)
    {
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(); } catch { }
                return null;
            }
            return p.ExitCode == 0 ? output.Result : null;
        }
        catch { return null; }
    }

    /// <summary>Запустить фоном (для проигрывания звука). Возвращает процесс, чтобы его можно было остановить.</summary>
    public static Process? Start(string file, string args)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        }
        catch { return null; }
    }

    public static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    public static bool Exists(string tool) => Run("sh", "-c " + Quote("command -v " + tool), 1000) is { Length: > 0 };
}

/// <summary>Значение, которое дорого получать: обновляется не чаще, чем раз в period.</summary>
sealed class Cached<T>
{
    readonly Func<T> get;
    readonly TimeSpan period;
    DateTime at = DateTime.MinValue;
    T value = default!;

    public Cached(TimeSpan period, Func<T> get)
    {
        this.period = period;
        this.get = get;
    }

    public T Value
    {
        get
        {
            var now = DateTime.UtcNow;
            if (now - at >= period || now < at)
            {
                value = get();
                at = now;
            }
            return value;
        }
    }
}

/// <summary>Проигрывание звука через внешнюю утилиту (afplay / paplay / aplay) с временным WAV-файлом.</summary>
sealed class ProcessSoundPlayer
{
    readonly Func<string, Process?> play;
    Process? current;
    readonly string tempWav = Path.Combine(Path.GetTempPath(), "break-reminder-" + Environment.ProcessId + ".wav");

    public ProcessSoundPlayer(Func<string, Process?> play) { this.play = play; }

    public void PlayWav(byte[] wav)
    {
        Stop();
        try { File.WriteAllBytes(tempWav, wav); } catch { return; }
        current = play(tempWav);
    }

    public void PlayFile(string path)
    {
        Stop();
        current = play(path);
    }

    public void Stop()
    {
        try { if (current is { HasExited: false }) current.Kill(); } catch { }
        current?.Dispose();
        current = null;
    }

    public void Cleanup()
    {
        Stop();
        try { File.Delete(tempWav); } catch { }
    }
}
