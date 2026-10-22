using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BreakReminder.Core;
using Microsoft.Win32;

namespace BreakReminder.Platform.Windows;

/// <summary>
/// Windows: простой — GetLastInputInfo; блокировка — WTS-уведомления; созвон — реестр CapabilityAccessManager;
/// полноэкранный режим — SHQueryUserNotificationState + размер активного окна; горячая клавиша — RegisterHotKey;
/// звук — winmm PlaySound; автозапуск — HKCU\...\Run.
/// </summary>
public sealed class WindowsPlatform : IPlatform
{
    public string Name => "Windows";

    MessageWindow? window;
    Action? hotkeyCallback;
    bool hotkeyRegistered;
    volatile bool locked;
    IntPtr soundMemory;
    readonly WindowsSpeech speech = new();

    const int HotkeyId = 1;

    public void Initialize()
    {
        // Скрытое окно на UI-потоке: в него приходят WM_HOTKEY и WM_WTSSESSION_CHANGE.
        // Цикл сообщений Avalonia доставляет их как и любым другим окнам этого потока.
        window = new MessageWindow(OnMessage);
        Native.WTSRegisterSessionNotification(window.Handle, 0);
    }

    void OnMessage(uint msg, IntPtr wParam)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            hotkeyCallback?.Invoke();
        }
        else if (msg == Native.WM_WTSSESSION_CHANGE)
        {
            switch (wParam.ToInt32())
            {
                case 7: // WTS_SESSION_LOCK
                case 2: // WTS_CONSOLE_DISCONNECT
                case 4: // WTS_REMOTE_DISCONNECT
                    locked = true;
                    break;
                case 8: // WTS_SESSION_UNLOCK
                case 1: // WTS_CONSOLE_CONNECT
                case 3: // WTS_REMOTE_CONNECT
                    locked = false;
                    break;
            }
        }
    }

    // ---------- IEnvironmentProbe ----------

    public TimeSpan IdleTime()
    {
        var lii = new Native.LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<Native.LASTINPUTINFO>() };
        if (!Native.GetLastInputInfo(ref lii)) return TimeSpan.Zero;
        uint idle = unchecked((uint)Environment.TickCount - lii.dwTime);
        return TimeSpan.FromMilliseconds(idle);
    }

    public bool IsScreenLocked() => locked;

    public bool IsCallActive() => CapabilityInUse("microphone") || CapabilityInUse("webcam");

    static bool CapabilityInUse(string cap)
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + cap);
            return root != null && AnyActive(root);
        }
        catch { return false; }
    }

    // Windows пишет LastUsedTimeStart/Stop для каждой программы; Stop == 0 — устройство занято прямо сейчас.
    static bool AnyActive(RegistryKey key)
    {
        foreach (string name in key.GetSubKeyNames())
        {
            using var sub = key.OpenSubKey(name);
            if (sub == null) continue;
            if (name == "NonPackaged")
            {
                if (AnyActive(sub)) return true;
                continue;
            }
            if (sub.GetValue("LastUsedTimeStart") is long start && sub.GetValue("LastUsedTimeStop") is long stop
                && start > 0 && stop == 0)
                return true;
        }
        return false;
    }

    public bool SupportsFullscreenDetection => true;

    public bool IsFullscreenAppActive()
    {
        try
        {
            // QUNS_BUSY / RUNNING_D3D_FULL_SCREEN / PRESENTATION_MODE
            if (Native.SHQueryUserNotificationState(out int st) == 0 && st is 2 or 3 or 4) return true;

            IntPtr h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero || Native.IsZoomed(h)) return false;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;
            var sb = new StringBuilder(64);
            Native.GetClassName(h, sb, sb.Capacity);
            if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
            if (!Native.GetWindowRect(h, out var r)) return false;
            var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
            if (!Native.GetMonitorInfo(Native.MonitorFromWindow(h, 2), ref mi)) return false;
            var b = mi.rcMonitor;
            return r.L <= b.L && r.T <= b.T && r.R >= b.R && r.B >= b.B;
        }
        catch { return false; }
    }

    // ---------- автозапуск ----------

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "BreakReminder";

    public bool IsAutostartEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(RunName) != null;
        }
        catch { return false; }
    }

    public void SetAutostart(bool enabled)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k == null) return;
            if (enabled) k.SetValue(RunName, "\"" + Environment.ProcessPath + "\" --tray");
            else k.DeleteValue(RunName, false);
        }
        catch { }
    }

    // ---------- звук ----------

    public void PlayWav(byte[] wav)
    {
        StopSound();
        // PlaySound(SND_MEMORY|SND_ASYNC) читает буфер во время проигрывания — держим его до следующего звука.
        soundMemory = Marshal.AllocHGlobal(wav.Length);
        Marshal.Copy(wav, 0, soundMemory, wav.Length);
        Native.PlaySound(soundMemory, IntPtr.Zero, Native.SND_MEMORY | Native.SND_ASYNC | Native.SND_NODEFAULT);
    }

    public void PlayFile(string path)
    {
        StopSound();
        Native.PlaySound(path, IntPtr.Zero, Native.SND_FILENAME | Native.SND_ASYNC | Native.SND_NODEFAULT);
    }

    public void PlaySystemSound()
    {
        StopSound();
        Native.PlaySound("SystemAsterisk", IntPtr.Zero, Native.SND_ALIAS | Native.SND_ASYNC);
    }

    // ---------- озвучка ----------

    public string? VoiceFor(string lang) => speech.VoiceFor(lang);
    public Task<bool> SpeakAsync(string text, string lang, bool slow, CancellationToken ct = default) => speech.SpeakAsync(text, lang, slow, ct);
    public void StopSpeaking() => speech.Stop();

    public void StopSound()
    {
        Native.PlaySound(IntPtr.Zero, IntPtr.Zero, 0); // останавливает синхронно
        if (soundMemory != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(soundMemory);
            soundMemory = IntPtr.Zero;
        }
    }

    // ---------- горячая клавиша ----------

    public string? PauseHotkeyName => "Ctrl+Alt+P";

    public bool RegisterPauseHotkey(Action onPressed)
    {
        UnregisterPauseHotkey();
        if (window == null) return false;
        hotkeyCallback = onPressed;
        hotkeyRegistered = Native.RegisterHotKey(window.Handle, HotkeyId,
            Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, 0x50 /* P */);
        return hotkeyRegistered;
    }

    public void UnregisterPauseHotkey()
    {
        if (hotkeyRegistered && window != null) Native.UnregisterHotKey(window.Handle, HotkeyId);
        hotkeyRegistered = false;
        hotkeyCallback = null;
    }

    // ---------- окна ----------

    public void FlashWindow(IntPtr handle, bool on)
    {
        if (handle == IntPtr.Zero) return;
        var f = new Native.FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<Native.FLASHWINFO>(),
            hwnd = handle,
            dwFlags = on ? 3u | 12u : 0u, // FLASHW_ALL | FLASHW_TIMERNOFG  /  FLASHW_STOP
            uCount = on ? uint.MaxValue : 0,
        };
        Native.FlashWindowEx(ref f);
    }

    public void RoundCorners(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return;
        try
        {
            int pref = 2; // DWMWCP_ROUND (Windows 11)
            Native.DwmSetWindowAttribute(handle, 33, ref pref, sizeof(int));
        }
        catch { }
    }

    public void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch { }
    }

    public void Dispose()
    {
        UnregisterPauseHotkey();
        StopSound();
        speech.Dispose();
        if (window != null)
        {
            Native.WTSUnRegisterSessionNotification(window.Handle);
            window.Dispose();
            window = null;
        }
    }

    /// <summary>Невидимое окно только для приёма сообщений.</summary>
    sealed class MessageWindow : IDisposable
    {
        static readonly Native.WndProc Proc = WndProcImpl; // держим делегат, чтобы GC не собрал
        static readonly Dictionary<IntPtr, Action<uint, IntPtr>> Handlers = new();
        static bool classRegistered;
        const string ClassName = "BreakReminderMessageWindow";

        public IntPtr Handle { get; }

        public MessageWindow(Action<uint, IntPtr> handler)
        {
            IntPtr hInst = Native.GetModuleHandle(null);
            if (!classRegistered)
            {
                var wc = new Native.WNDCLASSEX
                {
                    cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEX>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(Proc),
                    hInstance = hInst,
                    lpszClassName = ClassName
                };
                Native.RegisterClassEx(ref wc);
                classRegistered = true;
            }
            // Обычное (не message-only) скрытое окно: WTS-уведомления доходят до него надёжно.
            Handle = Native.CreateWindowEx(0, ClassName, "BreakReminder", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, hInst, IntPtr.Zero);
            Handlers[Handle] = handler;
        }

        static IntPtr WndProcImpl(IntPtr h, uint msg, IntPtr w, IntPtr l)
        {
            if (Handlers.TryGetValue(h, out var handler)) handler(msg, w);
            return Native.DefWindowProc(h, msg, w, l);
        }

        public void Dispose()
        {
            Handlers.Remove(Handle);
            Native.DestroyWindow(Handle);
        }
    }
}
