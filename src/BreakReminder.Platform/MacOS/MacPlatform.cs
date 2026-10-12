using System.Diagnostics;
using System.Runtime.InteropServices;
using BreakReminder.Core;
using BreakReminder.Platform.Shared;

namespace BreakReminder.Platform.MacOS;

/// <summary>
/// macOS: простой — CGEventSourceSecondsSinceLastEventType; блокировка — CGSessionCopyCurrentDictionary;
/// созвон — CoreAudio «устройство ввода где-то используется»; горячая клавиша — Carbon RegisterEventHotKey
/// (⌃⌥P, работает без разрешения «Универсальный доступ»); звук — afplay; автозапуск — LaunchAgent.
/// Определения полноэкранной программы нет.
/// </summary>
public sealed class MacPlatform : IPlatform
{
    public string Name => "macOS";

    readonly Cached<bool> locked, call;
    readonly ProcessSoundPlayer sound = new(path => Cmd.Start("/usr/bin/afplay", Cmd.Quote(path)));

    public MacPlatform()
    {
        locked = new Cached<bool>(TimeSpan.FromSeconds(2), ReadLocked);
        call = new Cached<bool>(TimeSpan.FromSeconds(2), ReadMicInUse);
    }

    public void Initialize() { }

    // ---------- IEnvironmentProbe ----------

    public TimeSpan IdleTime()
    {
        try
        {
            // kCGEventSourceStateCombinedSessionState = 0, kCGAnyInputEventType = ~0
            double sec = Mac.CGEventSourceSecondsSinceLastEventType(0, uint.MaxValue);
            return sec > 0 ? TimeSpan.FromSeconds(sec) : TimeSpan.Zero;
        }
        catch { return TimeSpan.Zero; }
    }

    public bool IsScreenLocked() => locked.Value;
    public bool IsCallActive() => call.Value;
    public bool IsFullscreenAppActive() => false;
    public bool SupportsFullscreenDetection => false;

    static bool ReadLocked()
    {
        IntPtr dict = IntPtr.Zero, key = IntPtr.Zero;
        try
        {
            dict = Mac.CGSessionCopyCurrentDictionary();
            if (dict == IntPtr.Zero) return false;
            key = Mac.CFStringCreateWithCString(IntPtr.Zero, "CGSSessionScreenIsLocked", Mac.kCFStringEncodingUTF8);
            IntPtr value = Mac.CFDictionaryGetValue(dict, key);
            return value != IntPtr.Zero && Mac.CFBooleanGetValue(value);
        }
        catch { return false; }
        finally
        {
            if (key != IntPtr.Zero) Mac.CFRelease(key);
            if (dict != IntPtr.Zero) Mac.CFRelease(dict);
        }
    }

    static bool ReadMicInUse()
    {
        try
        {
            // Устройство ввода по умолчанию
            var addr = new Mac.AudioObjectPropertyAddress(Mac.kAudioHardwarePropertyDefaultInputDevice, Mac.kAudioObjectPropertyScopeGlobal, 0);
            uint size = sizeof(uint);
            if (Mac.AudioObjectGetPropertyData(Mac.kAudioObjectSystemObject, ref addr, 0, IntPtr.Zero, ref size, out uint device) != 0 || device == 0)
                return false;
            // Использует ли его сейчас хоть одна программа
            addr = new Mac.AudioObjectPropertyAddress(Mac.kAudioDevicePropertyDeviceIsRunningSomewhere, Mac.kAudioObjectPropertyScopeGlobal, 0);
            size = sizeof(uint);
            return Mac.AudioObjectGetPropertyData(device, ref addr, 0, IntPtr.Zero, ref size, out uint running) == 0 && running != 0;
        }
        catch { return false; }
    }

    // ---------- автозапуск ----------

    const string AgentLabel = "com.breakreminder.app";

    static string AgentFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", AgentLabel + ".plist");

    public bool IsAutostartEnabled() => File.Exists(AgentFile);

    public void SetAutostart(bool enabled)
    {
        try
        {
            if (!enabled)
            {
                File.Delete(AgentFile);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(AgentFile)!);
            string exe = System.Security.SecurityElement.Escape(Environment.ProcessPath ?? "");
            File.WriteAllText(AgentFile,
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                "<plist version=\"1.0\"><dict>\n" +
                $"  <key>Label</key><string>{AgentLabel}</string>\n" +
                $"  <key>ProgramArguments</key><array><string>{exe}</string><string>--tray</string></array>\n" +
                "  <key>RunAtLoad</key><true/>\n" +
                "</dict></plist>\n");
        }
        catch { }
    }

    // ---------- звук ----------

    public void PlayWav(byte[] wav) => sound.PlayWav(wav);
    public void PlayFile(string path) => sound.PlayFile(path);
    public void PlaySystemSound() => sound.PlayFile("/System/Library/Sounds/Glass.aiff");
    public void StopSound() => sound.Stop();

    // ---------- горячая клавиша (Carbon) ----------

    static Mac.EventHandlerProc? handlerProc; // держим делегат от GC
    static Action? hotkeyCallback;
    IntPtr hotKeyRef, handlerRef;

    public string? PauseHotkeyName => "⌃⌥P";

    public bool RegisterPauseHotkey(Action onPressed)
    {
        UnregisterPauseHotkey();
        try
        {
            hotkeyCallback = onPressed;
            handlerProc ??= (_, _, _) =>
            {
                hotkeyCallback?.Invoke();
                return 0;
            };
            var spec = new[] { new Mac.EventTypeSpec { eventClass = Mac.kEventClassKeyboard, eventKind = Mac.kEventHotKeyPressed } };
            IntPtr target = Mac.GetApplicationEventTarget();
            if (Mac.InstallEventHandler(target, handlerProc, 1, spec, IntPtr.Zero, out handlerRef) != 0) return false;
            var id = new Mac.EventHotKeyID { signature = 0x42524B52 /* 'BRKR' */, id = 1 };
            // kVK_ANSI_P = 35; controlKey | optionKey
            return Mac.RegisterEventHotKey(35, Mac.controlKey | Mac.optionKey, id, target, 0, out hotKeyRef) == 0;
        }
        catch { return false; }
    }

    public void UnregisterPauseHotkey()
    {
        try
        {
            if (hotKeyRef != IntPtr.Zero) Mac.UnregisterEventHotKey(hotKeyRef);
            if (handlerRef != IntPtr.Zero) Mac.RemoveEventHandler(handlerRef);
        }
        catch { }
        hotKeyRef = handlerRef = IntPtr.Zero;
        hotkeyCallback = null;
    }

    // ---------- окна ----------

    public void FlashWindow(IntPtr handle, bool on) { }
    public void RoundCorners(IntPtr handle) { }

    public void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo("/usr/bin/open", Cmd.Quote(path)) { UseShellExecute = false }); } catch { }
    }

    public void Dispose()
    {
        UnregisterPauseHotkey();
        sound.Cleanup();
    }
}

static class Mac
{
    const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    const string CoreAudio = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    public const uint kCFStringEncodingUTF8 = 0x08000100;

    [DllImport(CoreGraphics)] public static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);
    [DllImport(CoreGraphics)] public static extern IntPtr CGSessionCopyCurrentDictionary();

    [DllImport(CoreFoundation)] public static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, uint encoding);
    [DllImport(CoreFoundation)] public static extern IntPtr CFDictionaryGetValue(IntPtr dict, IntPtr key);
    [DllImport(CoreFoundation)] [return: MarshalAs(UnmanagedType.I1)] public static extern bool CFBooleanGetValue(IntPtr boolean);
    [DllImport(CoreFoundation)] public static extern void CFRelease(IntPtr obj);

    // ---- CoreAudio ----
    public const uint kAudioObjectSystemObject = 1;
    public const uint kAudioHardwarePropertyDefaultInputDevice = 0x64496E20;     // 'dIn '
    public const uint kAudioDevicePropertyDeviceIsRunningSomewhere = 0x676F6E65;  // 'gone'
    public const uint kAudioObjectPropertyScopeGlobal = 0x676C6F62;               // 'glob'

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioObjectPropertyAddress
    {
        public uint mSelector, mScope, mElement;
        public AudioObjectPropertyAddress(uint selector, uint scope, uint element)
        {
            mSelector = selector;
            mScope = scope;
            mElement = element;
        }
    }

    [DllImport(CoreAudio)]
    public static extern int AudioObjectGetPropertyData(uint objectId, ref AudioObjectPropertyAddress address,
        uint qualifierSize, IntPtr qualifier, ref uint dataSize, out uint data);

    // ---- Carbon hot keys ----
    public const uint kEventClassKeyboard = 0x6B657962; // 'keyb'
    public const uint kEventHotKeyPressed = 5;
    public const uint controlKey = 4096, optionKey = 2048;

    [StructLayout(LayoutKind.Sequential)] public struct EventTypeSpec { public uint eventClass; public uint eventKind; }
    [StructLayout(LayoutKind.Sequential)] public struct EventHotKeyID { public uint signature; public uint id; }

    public delegate int EventHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    [DllImport(Carbon)] public static extern IntPtr GetApplicationEventTarget();
    [DllImport(Carbon)]
    public static extern int InstallEventHandler(IntPtr target, EventHandlerProc handler, uint numTypes,
        EventTypeSpec[] list, IntPtr userData, out IntPtr handlerRef);
    [DllImport(Carbon)] public static extern int RemoveEventHandler(IntPtr handlerRef);
    [DllImport(Carbon)]
    public static extern int RegisterEventHotKey(uint keyCode, uint modifiers, EventHotKeyID id, IntPtr target,
        uint options, out IntPtr hotKeyRef);
    [DllImport(Carbon)] public static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
}
