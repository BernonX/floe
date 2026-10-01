using System.ComponentModel;
using System.Runtime.InteropServices;
using Floe.Application.Widgets;
using Floe.Domain.Widgets;

namespace Floe.Desktop.Platform;

/// <summary>Native desktop pixels avoid mixing WPF DIPs from monitors with different DPI scales.</summary>
internal static class WidgetWindowPlacement
{
    public static void MakeToolWindow(nint handle)
    {
        var style = nint.Size == 8 ? GetWindowLongPtr(handle, -20).ToInt64() : GetWindowLong(handle, -20);
        var updated = (style | 0x80) & ~0x40000; // TOOLWINDOW, without APPWINDOW.
        if (nint.Size == 8) SetWindowLongPtr(handle, -20, new nint(updated));
        else SetWindowLong(handle, -20, (int)updated);
    }

    public static WidgetPosition GetPosition(nint handle)
    {
        var rect = ReadRect(handle);
        return new(rect.Left, rect.Top);
    }

    public static void Restore(nint handle, nint mainHandle, WidgetSettings settings)
    {
        var rect = ReadRect(handle);
        var requested = new NativeRect(settings.LeftPixels ?? rect.Left, settings.TopPixels ?? rect.Top,
            (settings.LeftPixels ?? rect.Left) + 1, (settings.TopPixels ?? rect.Top) + 1);
        var monitor = settings.LeftPixels.HasValue ? MonitorFromRect(ref requested, 2) : MonitorFromWindow(mainHandle, 2);
        var area = WorkArea(monitor);
        var position = settings.LeftPixels.HasValue ? new WidgetPosition(settings.LeftPixels.Value, settings.TopPixels!.Value)
            : new(area.Right - (rect.Right - rect.Left) - 24, area.Bottom - (rect.Bottom - rect.Top) - 24);
        Move(handle, position);
        // Moving can synchronously change DPI and therefore the physical window size.
        rect = ReadRect(handle);
        Move(handle, WidgetPlacement.Fit(position, rect.Right - rect.Left, rect.Bottom - rect.Top, area));
    }

    public static void Move(nint handle, WidgetPosition position)
    {
        if (!SetWindowPos(handle, 0, position.Left, position.Top, 0, 0, 0x0015)) // NOSIZE | NOZORDER | NOACTIVATE
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public static void MoveIntoWorkArea(nint handle, WidgetPosition requested)
    {
        var point = new NativeRect(requested.Left, requested.Top, requested.Left + 1, requested.Top + 1);
        var area = WorkArea(MonitorFromRect(ref point, 2));
        Move(handle, requested);
        var rect = ReadRect(handle);
        Move(handle, WidgetPlacement.Fit(requested, rect.Right - rect.Left, rect.Bottom - rect.Top, area));
    }

    private static NativeRect ReadRect(nint handle)
    {
        if (!GetWindowRect(handle, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return rect;
    }
    private static PixelWorkArea WorkArea(nint monitor)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(info.Work.Left, info.Work.Top, info.Work.Right, info.Work.Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect(int left, int top, int right, int bottom)
    { public int Left = left; public int Top = top; public int Right = right; public int Bottom = bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor; public NativeRect Work; public int Flags; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint handle, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref NativeRect rect, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint handle, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint handle, int index, int value);
}
