using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BreakReminder
{
    static class Native
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [StructLayout(LayoutKind.Sequential)]
        struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        static extern bool FlashWindowEx(ref FLASHWINFO pwfi);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>Сколько времени не было ввода с клавиатуры/мыши (во всей системе).</summary>
        public static TimeSpan IdleTime()
        {
            var lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref lii)) return TimeSpan.Zero;
            uint idle = unchecked((uint)Environment.TickCount - lii.dwTime);
            return TimeSpan.FromMilliseconds(idle);
        }

        /// <summary>Мигание кнопки окна на панели задач.</summary>
        public static void Flash(IntPtr hwnd, bool on)
        {
            var f = new FLASHWINFO();
            f.cbSize = (uint)Marshal.SizeOf(typeof(FLASHWINFO));
            f.hwnd = hwnd;
            f.dwFlags = on ? (3u | 12u) : 0u; // FLASHW_ALL | FLASHW_TIMERNOFG  /  FLASHW_STOP
            f.uCount = on ? uint.MaxValue : 0;
            f.dwTimeout = 0;
            FlashWindowEx(ref f);
        }

        // ---------- горячая клавиша ----------

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        // ---------- «не беспокоить» ----------

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int L, T, R, B; }

        [DllImport("shell32.dll")]
        static extern int SHQueryUserNotificationState(out int state);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hwnd, out RECT r);

        [DllImport("user32.dll")]
        static extern bool IsZoomed(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetClassName(IntPtr hwnd, StringBuilder sb, int max);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        /// <summary>На переднем плане полноэкранное приложение (игра, видео, презентация, F11 в браузере).</summary>
        public static bool IsFullscreenAppActive()
        {
            try
            {
                int st;
                // QUNS_BUSY / RUNNING_D3D_FULL_SCREEN / PRESENTATION_MODE
                if (SHQueryUserNotificationState(out st) == 0 && (st == 2 || st == 3 || st == 4)) return true;

                IntPtr h = GetForegroundWindow();
                if (h == IntPtr.Zero || IsZoomed(h)) return false;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == (uint)Process.GetCurrentProcess().Id) return false;
                var sb = new StringBuilder(64);
                GetClassName(h, sb, sb.Capacity);
                string cls = sb.ToString();
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd") return false;
                RECT r;
                if (!GetWindowRect(h, out r)) return false;
                Rectangle b = Screen.FromHandle(h).Bounds;
                return r.L <= b.Left && r.T <= b.Top && r.R >= b.Right && r.B >= b.Bottom;
            }
            catch { return false; }
        }

        /// <summary>Сейчас какая-то программа использует микрофон или камеру (значит, скорее всего, созвон).</summary>
        public static bool IsMicOrCameraInUse()
        {
            return CapabilityInUse("microphone") || CapabilityInUse("webcam");
        }

        static bool CapabilityInUse(string cap)
        {
            try
            {
                using (var root = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\" + cap))
                    return root != null && AnyActive(root);
            }
            catch { return false; }
        }

        // Windows пишет LastUsedTimeStart/Stop для каждой программы; Stop == 0 — устройство занято прямо сейчас.
        static bool AnyActive(RegistryKey key)
        {
            foreach (string name in key.GetSubKeyNames())
            {
                using (var sub = key.OpenSubKey(name))
                {
                    if (sub == null) continue;
                    if (name == "NonPackaged")
                    {
                        if (AnyActive(sub)) return true;
                        continue;
                    }
                    object start = sub.GetValue("LastUsedTimeStart"), stop = sub.GetValue("LastUsedTimeStop");
                    if (start is long && stop is long && (long)start > 0 && (long)stop == 0) return true;
                }
            }
            return false;
        }

        /// <summary>Тёмный заголовок окна (Windows 10 20H1+ / 11).</summary>
        public static void DarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
            }
            catch { }
        }

        /// <summary>Скруглённые углы окна в Windows 11.</summary>
        public static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(hwnd, 33, ref pref, sizeof(int));
            }
            catch { }
        }
    }
}
