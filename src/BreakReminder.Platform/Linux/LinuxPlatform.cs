using System.Diagnostics;
using System.Text.RegularExpressions;
using BreakReminder.Core;
using BreakReminder.Platform.Shared;

namespace BreakReminder.Platform.Linux;

/// <summary>
/// Linux (в первую очередь Ubuntu/GNOME, X11 и Wayland). Всё через стандартные утилиты и D-Bus (gdbus):
/// простой — Mutter IdleMonitor (запасной вариант — xprintidle); блокировка — ScreenSaver.GetActive / logind;
/// созвон — активные записи с микрофона в PipeWire/PulseAudio (pactl); полноэкранный режим — только X11 (xprop);
/// звук — paplay/aplay; автозапуск — ~/.config/autostart/*.desktop.
/// Глобальных горячих клавиш Wayland не даёт — вместо них команда «BreakReminder --toggle-pause».
/// </summary>
public sealed class LinuxPlatform : IPlatform
{
    public string Name => "Linux";

    readonly Cached<TimeSpan> idle;
    readonly Cached<bool> locked, call, fullscreen;
    readonly ProcessSoundPlayer sound;

    // Озвучка: speech-dispatcher (spd-say), иначе eSpeak NG. Какие языки у них есть, заранее не узнать — считаем, что все.
    readonly Cached<string?> speechTool = new(TimeSpan.FromMinutes(5),
        () => Cmd.Exists("spd-say") ? "spd-say" : Cmd.Exists("espeak-ng") ? "espeak-ng" : null);
    readonly ProcessSpeech speech;
    readonly bool isX11 = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "x11", StringComparison.OrdinalIgnoreCase);

    public LinuxPlatform()
    {
        // Каждая проверка — запуск внешней утилиты, поэтому кэшируем на несколько секунд.
        idle = new Cached<TimeSpan>(TimeSpan.FromSeconds(3), ReadIdle);
        locked = new Cached<bool>(TimeSpan.FromSeconds(3), ReadLocked);
        call = new Cached<bool>(TimeSpan.FromSeconds(3), ReadCall);
        fullscreen = new Cached<bool>(TimeSpan.FromSeconds(3), ReadFullscreen);
        speech = new ProcessSpeech(
            lang => speechTool.Value is { } tool ? $"{tool} ({lang})" : null,
            (text, lang, slow, _) => speechTool.Value == "spd-say"
                ? ProcessSpeech.Args("spd-say", "-w", "-l", lang, "-r", slow ? "-40" : "-10", text)   // -w: дождаться конца
                : ProcessSpeech.Args("espeak-ng", "-v", lang, "-s", slow ? "120" : "160", text));
        sound = new ProcessSoundPlayer(path =>
            Cmd.Start("paplay", Cmd.Quote(path)) ?? Cmd.Start("aplay", "-q " + Cmd.Quote(path)));
    }

    public void Initialize() { }

    // ---------- IEnvironmentProbe ----------

    public TimeSpan IdleTime() => idle.Value;
    public bool IsScreenLocked() => locked.Value;
    public bool IsCallActive() => call.Value;
    public bool IsFullscreenAppActive() => fullscreen.Value;
    public bool SupportsFullscreenDetection => isX11;

    static readonly Regex Number = new(@"\d+", RegexOptions.Compiled);

    static TimeSpan ReadIdle()
    {
        // GNOME (X11 и Wayland): "(uint64 12345,)" — миллисекунды
        var o = Cmd.Run("gdbus", "call --session --dest org.gnome.Mutter.IdleMonitor " +
                                 "--object-path /org/gnome/Mutter/IdleMonitor/Core --method org.gnome.Mutter.IdleMonitor.GetIdletime");
        if (o != null)
        {
            var m = Number.Matches(o);
            if (m.Count > 1 && ulong.TryParse(m[1].Value, out ulong ms)) return TimeSpan.FromMilliseconds(ms); // m[0] — «64» из uint64
        }
        // X11 без GNOME
        o = Cmd.Run("xprintidle", "");
        if (o != null && ulong.TryParse(o.Trim(), out ulong x)) return TimeSpan.FromMilliseconds(x);
        return TimeSpan.Zero; // не умеем — считаем, что пользователь активен
    }

    static bool ReadLocked()
    {
        foreach (var (dest, path) in new[]
                 {
                     ("org.gnome.ScreenSaver", "/org/gnome/ScreenSaver"),
                     ("org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver")
                 })
        {
            var o = Cmd.Run("gdbus", $"call --session --dest {dest} --object-path {path} --method {dest}.GetActive");
            if (o != null) return o.Contains("true");
        }
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
        if (!string.IsNullOrEmpty(session))
        {
            var o = Cmd.Run("loginctl", $"show-session {session} -p LockedHint --value");
            if (o != null) return o.Trim() == "yes";
        }
        return false;
    }

    static bool ReadCall()
    {
        // Каждая строка — программа, которая сейчас пишет с микрофона.
        var o = Cmd.Run("pactl", "list short source-outputs");
        return !string.IsNullOrWhiteSpace(o);
    }

    bool ReadFullscreen()
    {
        if (!isX11) return false; // Wayland не даёт смотреть на чужие окна
        var active = Cmd.Run("xprop", "-root _NET_ACTIVE_WINDOW");
        var m = active == null ? null : Regex.Match(active, @"0x[0-9a-fA-F]+");
        if (m == null || !m.Success || m.Value == "0x0") return false;
        var state = Cmd.Run("xprop", $"-id {m.Value} _NET_WM_STATE");
        return state != null && state.Contains("_NET_WM_STATE_FULLSCREEN");
    }

    // ---------- автозапуск ----------

    static string AutostartFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
            ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "break-reminder.desktop");

    /// <summary>Путь к программе: для AppImage — сам .AppImage, иначе исполняемый файл.</summary>
    static string ExecutablePath => Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } a ? a : Environment.ProcessPath ?? "BreakReminder";

    public bool IsAutostartEnabled() => File.Exists(AutostartFile);

    public void SetAutostart(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                File.Delete(AutostartFile);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(AutostartFile)!);
            File.WriteAllText(AutostartFile,
                "[Desktop Entry]\n" +
                "Type=Application\n" +
                "Name=Отдыхалка\n" +
                "Comment=Напоминания о перерывах\n" +
                $"Exec=\"{ExecutablePath}\" --tray\n" +
                "X-GNOME-Autostart-enabled=true\n" +
                "Terminal=false\n");
        }
        catch { }
    }

    // ---------- звук ----------

    public void PlayWav(byte[] wav) => sound.PlayWav(wav);
    public void PlayFile(string path) => sound.PlayFile(path);

    public void PlaySystemSound()
    {
        const string freedesktop = "/usr/share/sounds/freedesktop/stereo/complete.oga";
        if (File.Exists(freedesktop)) sound.PlayFile(freedesktop);
        else sound.PlayWav(Synth.Melody("soft", 0.7));
    }

    public void StopSound() => sound.Stop();

    public string? VoiceFor(string lang) => speech.VoiceFor(lang);
    public Task<bool> SpeakAsync(string text, string lang, bool slow, CancellationToken ct = default) => speech.SpeakAsync(text, lang, slow, ct);
    public void StopSpeaking() => speech.Stop();

    // ---------- горячая клавиша ----------

    public string? PauseHotkeyName => null;
    public bool RegisterPauseHotkey(Action onPressed) => false;
    public void UnregisterPauseHotkey() { }

    // ---------- окна ----------

    public void FlashWindow(IntPtr handle, bool on) { }
    public void RoundCorners(IntPtr handle) { }

    public void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo("xdg-open", Cmd.Quote(path)) { UseShellExecute = false }); } catch { }
    }

    public void Dispose()
    {
        speech.Stop();
        sound.Cleanup();
    }
}
