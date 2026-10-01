using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Media;
using Floe.Desktop.Themes;

namespace Floe.Desktop.Platform;

/// <summary>Window-only DWM interop. Uses documented attributes and restores an opaque fallback on failure.</summary>
internal sealed class WindowBackdrop : IDisposable
{
    private readonly Window window;
    private readonly ThemeService theme;
    private HwndSource? source;
    private bool disposed;
    public bool IsAcrylic { get; private set; }
    public event EventHandler? Applied;

    public WindowBackdrop(Window window, ThemeService theme)
    {
        this.window = window;
        this.theme = theme;
        window.SourceInitialized += OnSourceInitialized;
        theme.Changed += OnThemeChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
        source?.AddHook(WindowMessage);
        Apply();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Apply();

    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // DWM may restart or change composition mode independently of WPF resources.
        if (message == 0x031E) window.Dispatcher.BeginInvoke(Apply); // WM_DWMCOMPOSITIONCHANGED
        return 0;
    }

    private void Apply()
    {
        if (disposed || source?.CompositionTarget is null) return;
        var handle = source.Handle;
        HideNativeCaptionButtons(handle);
        var backdropApplied = false;

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            SetAttribute(handle, 20, theme.IsDark && !theme.IsHighContrast ? 1 : 0);
            SetAttribute(handle, 33, 2); // DWMWCP_ROUND; maximization remains managed by Windows.
            SetAttribute(handle, 34, theme.IsHighContrast ? -1 : -2); // Suppress the native border in the integrated chrome.
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
        {
            if (theme.MaterialsEnabled && DwmIsCompositionEnabled(out var enabled) >= 0 && enabled)
            {
                var margins = new Margins(-1);
                backdropApplied = DwmExtendFrameIntoClientArea(handle, ref margins) >= 0 &&
                    SetAttribute(handle, 38, 3) >= 0; // DWMSBT_TRANSIENTWINDOW: native desktop Acrylic, no screen-capture loop.
            }
            if (!backdropApplied)
            {
                SetAttribute(handle, 38, 1); // DWMSBT_NONE
                var margins = new Margins(0);
                DwmExtendFrameIntoClientArea(handle, ref margins);
            }
        }

        source.CompositionTarget.BackgroundColor = backdropApplied ? Colors.Transparent : (Color)window.FindResource("WindowColor");
        IsAcrylic = backdropApplied;
        if (backdropApplied) window.Background = Brushes.Transparent;
        else window.SetResourceReference(Window.BackgroundProperty, "WindowBrush");

        // Readable automation diagnostics for smoke tests; no technical labels in the product UI.
        if (window is not Widgets.WidgetWindow)
            AutomationProperties.SetHelpText(window,
                $"Theme={(theme.IsHighContrast ? "HighContrast" : theme.IsDark ? "Dark" : "Light")};Backdrop={(backdropApplied ? "Acrylic" : "Solid")};Motion={theme.AnimationsEnabled}");
        Applied?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        window.SourceInitialized -= OnSourceInitialized;
        theme.Changed -= OnThemeChanged;
        source?.RemoveHook(WindowMessage);
    }

    private static int SetAttribute(nint hwnd, int attribute, int value) => DwmSetWindowAttribute(hwnd, attribute, ref value, sizeof(int));
    private static void HideNativeCaptionButtons(nint handle)
    {
        // Keep WS_CAPTION for native backdrop eligibility; WindowChrome owns its entire client area.
        // DWM otherwise paints a second set of caption buttons over the custom visuals.
        var style = nint.Size == 8 ? GetWindowLongPtr(handle, -16).ToInt64() : GetWindowLong(handle, -16);
        if ((style & 0x00080000) == 0) return; // WS_SYSMENU
        if (nint.Size == 8) SetWindowLongPtr(handle, -16, new nint(style & ~0x00080000));
        else SetWindowLong(handle, -16, (int)(style & ~0x00080000));
    }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint handle, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint handle, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong(nint handle, int index, int value);

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins(int value)
    {
        public int Left = value;
        public int Right = value;
        public int Top = value;
        public int Bottom = value;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    private static extern int DwmIsCompositionEnabled([MarshalAs(UnmanagedType.Bool)] out bool enabled);
}
