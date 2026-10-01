using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;

namespace Floe.Desktop.Platform;

/// <summary>Custom visuals with native resize/caption hit testing, including Windows 11 Snap layouts.</summary>
internal sealed class IntegratedWindowChrome : IDisposable
{
    private readonly Window window;
    private readonly Button maximize;
    private HwndSource? source;
    private bool pressed;
    public IntegratedWindowChrome(Window window, Button maximize)
    { this.window = window; this.maximize = maximize; window.SourceInitialized += OnInitialized; }
    private void OnInitialized(object? sender, EventArgs e)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        WindowChrome.SetWindowChrome(window, new WindowChrome
        { CaptionHeight = 44, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false });
        // HwndSource prepends hooks; intercept HTMAXBUTTON before WindowChrome's general caption handler.
        source?.AddHook(Messages);
    }
    private bool OverMaximize(nint coordinates)
    {
        var x = unchecked((short)(coordinates.ToInt64() & 0xffff));
        var y = unchecked((short)((coordinates.ToInt64() >> 16) & 0xffff));
        var point = maximize.PointFromScreen(new Point(x, y));
        return point.X >= 0 && point.Y >= 0 && point.X < maximize.ActualWidth && point.Y < maximize.ActualHeight;
    }
    private nint Messages(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0084 && window.IsVisible && OverMaximize(lParam)) // WM_NCHITTEST
        { handled = true; return 9; } // HTMAXBUTTON enables the native Snap flyout.
        if (message == 0x00A0 && wParam == 9)
        {
            maximize.Opacity = pressed ? 0.55 : 0.8;
            var tracking = new MouseTracking { Size = (uint)Marshal.SizeOf<MouseTracking>(), Flags = 0x12, Window = hwnd };
            TrackMouseEvent(ref tracking); // LEAVE | NONCLIENT; no polling.
        }
        if (message == 0x00A1 && wParam == 9) { pressed = true; maximize.Opacity = 0.55; handled = true; }
        if (message == 0x00A2 && wParam == 9)
        {
            if (pressed && OverMaximize(lParam)) ToggleMaximize(window);
            pressed = false; maximize.Opacity = 1; handled = true;
        }
        if (message == 0x02A2) { pressed = false; maximize.Opacity = 1; }
        return 0;
    }
    public static void ToggleMaximize(Window window)
    { if (window.WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(window); else SystemCommands.MaximizeWindow(window); }
    public void Dispose() { window.SourceInitialized -= OnInitialized; source?.RemoveHook(Messages); }
    [StructLayout(LayoutKind.Sequential)] private struct MouseTracking { public uint Size, Flags; public nint Window; public uint HoverTime; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool TrackMouseEvent(ref MouseTracking tracking);
}
