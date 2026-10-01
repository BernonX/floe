using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace Floe.Desktop.Platform;

internal static class WindowsAppearance
{
    // Registry preferences are best-effort hints; inaccessible settings use a conservative fallback.
    private const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    public static bool ReadAppsUseLightTheme() => ReadPreference("AppsUseLightTheme", true);
    public static bool ReadTransparencyEnabled() => ReadPreference("EnableTransparency", false);

    private static bool ReadPreference(string name, bool fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Personalize);
            return key?.GetValue(name) is int value ? value != 0 : fallback;
        }
        catch (Exception exception) when (exception is SecurityException or UnauthorizedAccessException or IOException)
        {
            return fallback;
        }
    }

    public static bool IsBatterySaverEnabled() => GetSystemPowerStatus(out var status) && status.SystemStatusFlag == 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte AcLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
