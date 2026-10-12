using BreakReminder.Core;
using BreakReminder.Platform.Linux;
using BreakReminder.Platform.MacOS;
using BreakReminder.Platform.Windows;

namespace BreakReminder.Platform;

public static class PlatformFactory
{
    public static IPlatform Create()
    {
        if (OperatingSystem.IsWindows()) return new WindowsPlatform();
        if (OperatingSystem.IsMacOS()) return new MacPlatform();
        return new LinuxPlatform();
    }
}
