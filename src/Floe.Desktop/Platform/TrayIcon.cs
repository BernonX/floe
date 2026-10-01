using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Floe.Application.SystemIntegration;

namespace Floe.Desktop.Platform;

/// <summary>Public Shell API only. A hidden top-level HWND receives Explorer restart broadcasts.</summary>
internal sealed class TrayIcon : IDisposable, ICompletionNotificationSink
{
    private const uint CallbackMessage = 0x8001;
    private readonly Dispatcher dispatcher;
    private readonly SystemSettingsController settings;
    private readonly HwndSource source;
    private readonly Window host;
    private readonly IntPtr handle;
    private readonly Border menuOwner = new() { Width = 1, Height = 1, Focusable = true };
    private readonly IntPtr icon;
    private readonly uint taskbarCreated;
    private readonly Func<ContextMenu> createMenu;
    private readonly Action openMain;
    private ContextMenu? menu;
    private bool disposed;
    public bool IsAvailable { get; private set; }

    public TrayIcon(Dispatcher dispatcher, SystemSettingsController settings, Func<ContextMenu> createMenu, Action openMain)
    {
        this.dispatcher = dispatcher; this.settings = settings; this.createMenu = createMenu; this.openMain = openMain;
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        // A WPF Window gives MenuBase a real input owner, including when MainWindow is hidden.
        // A bare HwndSource cannot reliably retain keyboard focus for an open ContextMenu.
        host = new Window { Title = $"Floe.TrayHost.{Environment.ProcessId}", Width = 1, Height = 1,
            Left = -10000, Top = -10000, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, ShowActivated = false, AllowsTransparency = true,
            Background = System.Windows.Media.Brushes.Transparent, Content = menuOwner };
        handle = new WindowInteropHelper(host).EnsureHandle();
        WidgetWindowPlacement.MakeToolWindow(handle);
        source = HwndSource.FromHwnd(handle)!;
        source.AddHook(WindowProcedure);
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/Floe.ico"));
        using var stream = resource.Stream;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        var ico = buffer.ToArray();
        // Prefer the original's 32-pixel frame instead of enlarging the first 16-pixel entry.
        var entry = 6;
        for (var index = 0; index < BitConverter.ToUInt16(ico, 4); index++)
            if (ico[6 + index * 16] == 32) { entry = 6 + index * 16; break; }
        var length = BitConverter.ToInt32(ico, entry + 8); var offset = BitConverter.ToInt32(ico, entry + 12);
        icon = CreateIconFromResourceEx(ico.AsSpan(offset, length).ToArray(), (uint)length, true, 0x30000, 32, 32, 0);
        IsAvailable = icon != IntPtr.Zero && AddIcon();
    }

    private NotifyIconData Data(uint flags = 7) => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = handle, Id = 1, Flags = flags,
        Callback = CallbackMessage, Icon = icon, Tip = "Floe · 专注管理", Info = "", InfoTitle = ""
    };
    private bool AddIcon() { var data = Data(); return ShellNotifyIcon(0, ref data); }
    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (disposed) return IntPtr.Zero;
        if (taskbarCreated != 0 && (uint)message == taskbarCreated)
        {
            IsAvailable = AddIcon();
            // If the tray cannot be recreated, never leave the application inaccessible.
            if (!IsAvailable) openMain();
        }
        else if ((uint)message == CallbackMessage)
        {
            switch (lParam.ToInt32())
            {
                case 0x202: case 0x203: case 0x400: case 0x401: case 0x405: openMain(); break;
                case 0x205: case 0x7B: OpenMenu(); break;
            }
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OpenMenu()
    {
        if (menu is not null) menu.IsOpen = false;
        menu = createMenu();
        menu.PlacementTarget = menuOwner;
        menu.Placement = PlacementMode.MousePoint;
        // Shell menus need a foreground owner even when every application window is hidden.
        // This 1px tool window lives outside the desktop and is shown without activation first.
        host.Show();
        host.Activate();
        menu.Closed += (_, _) => { if (!source.IsDisposed) host.Hide(); };
        menu.Opened += (_, _) =>
        {
            if (menu.Items.Count > 0 && menu.Items[0] is MenuItem first) first.Focus();
        };
        SetForegroundWindow(handle);
        menu.IsOpen = true;
    }

    public void Deliver(CompletionNotice notice, bool showNotification, bool playSound)
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (disposed) return;
            var current = settings.Settings;
            if (showNotification && current.NotificationsEnabled && IsAvailable)
            {
                var data = Data(0x10); // NIF_INFO; Windows decides whether and how to present the reminder.
                data.InfoTitle = Trim(notice.Title, 63); data.Info = Trim(notice.Message, 255);
                data.InfoFlags = 1 | 0x10 | 0x80; // NIIF_INFO | NOSOUND | RESPECT_QUIET_TIME
                data.TimeoutOrVersion = 10000;
                ShellNotifyIcon(1, ref data);
            }
            if (playSound && current.SoundEnabled) MessageBeep(0x40);
        });
    }

    private static string Trim(string text, int limit) => text.Length <= limit ? text : text[..(limit - 1)] + "…";
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; if (menu is not null) menu.IsOpen = false;
        var data = Data(); ShellNotifyIcon(2, ref data);
        if (!source.IsDisposed) { source.RemoveHook(WindowProcedure); host.Close(); }
        if (icon != IntPtr.Zero) DestroyIcon(icon);
        IsAvailable = false;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Callback; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")] private static extern IntPtr CreateIconFromResourceEx(byte[] bytes, uint size,
        [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(IntPtr icon);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MessageBeep(uint type);
}
