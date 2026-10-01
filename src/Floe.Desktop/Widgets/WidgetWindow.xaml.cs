using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Floe.Application.Widgets;
using Floe.Desktop.Platform;
using Floe.Desktop.Motion;
using Floe.Desktop.Themes;

namespace Floe.Desktop.Widgets;

/// <summary>Generic window host: layout, state and lifecycle are provided by the widget manager.</summary>
public partial class WidgetWindow : Window
{
    private readonly WidgetController controller;
    private HwndSource? source;
    private bool allowMovement;
    private readonly WindowBackdrop backdrop;
    private bool pressing;
    private bool pointerMoved;
    private Point pressScreenPosition;
    private WidgetPosition pressWindowPosition;
    internal bool HasAcrylic => backdrop.IsAcrylic;
    internal event EventHandler BackdropChanged { add => backdrop.Applied += value; remove => backdrop.Applied -= value; }
    public event EventHandler? PositionCommitted;
    public event EventHandler? MainWindowRequested;
    public nint Handle => new WindowInteropHelper(this).Handle;
    internal bool IsLocked => controller.Settings.IsPositionLocked;
    internal FrameworkElement MotionSurface => WidgetSurface;

    public WidgetWindow(object viewModel, WidgetController controller, WidgetLayoutDefinition layout, ThemeService theme)
    {
        this.controller = controller;
        InitializeComponent();
        PreviewMouseLeftButtonDown += OnPress;
        PreviewMouseLeftButtonUp += OnRelease;
        PreviewMouseMove += OnPointerMove;
        LostMouseCapture += (_, _) => CancelPress();
        Deactivated += (_, _) => CancelPress();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelPress(); };
        PreviewKeyDown += (_, e) => { if (pressing && e.Key == Key.Escape) { CancelPress(); e.Handled = true; } };
        backdrop = new WindowBackdrop(this, theme);
        DataContext = viewModel;
        Width = layout.Width; Height = layout.Height;
        LayoutHost.Content = layout.CreateView();
        MenuMotion.Attach(ContextMenu);
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        WidgetWindowPlacement.MakeToolWindow(Handle);
        source = HwndSource.FromHwnd(Handle);
        source?.AddHook(WindowMessage);
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        source?.RemoveHook(WindowMessage);
        CancelPress();
        backdrop.Dispose();
        SourceInitialized -= OnSourceInitialized; Closed -= OnClosed;
    }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0046 && IsLocked && !allowMovement && lParam != 0) // WM_WINDOWPOSCHANGING
        {
            var position = Marshal.PtrToStructure<NativeWindowPosition>(lParam);
            position.Flags |= 0x0002; // SWP_NOMOVE also blocks system keyboard movement while locked.
            Marshal.StructureToPtr(position, lParam, false);
        }
        return 0;
    }
    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        for (var node = e.OriginalSource as DependencyObject; node is not null;
             node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ButtonBase) return;
        CancelPress();
        pressScreenPosition = PointToScreen(e.GetPosition(this));
        pressWindowPosition = WidgetWindowPlacement.GetPosition(Handle);
        pressing = CaptureMouse();
        pointerMoved = false;
        e.Handled = true;
    }
    private void OnPointerMove(object sender, MouseEventArgs e)
    {
        if (!pressing) return;
        var point = PointFromScreen(pressScreenPosition);
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - point.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(current.Y - point.Y) > SystemParameters.MinimumVerticalDragDistance) pointerMoved = true;
        if (!pointerMoved || IsLocked || e.LeftButton != MouseButtonState.Pressed) return;
        var screen = PointToScreen(current);
        var position = new WidgetPosition(pressWindowPosition.Left + (int)Math.Round(screen.X - pressScreenPosition.X),
            pressWindowPosition.Top + (int)Math.Round(screen.Y - pressScreenPosition.Y));
        CancelPress(); // Consume the release before the native drag loop; it must never open the main window.
        try
        {
            WithMovement(() =>
            {
                // Apply the first movement too, so entering DragMove never drops that pointer delta.
                WidgetWindowPlacement.Move(Handle, position);
                DragMove();
            });
        }
        catch (InvalidOperationException) when (Mouse.LeftButton != MouseButtonState.Pressed) { }
        PositionCommitted?.Invoke(this, EventArgs.Empty);
    }
    private void OnRelease(object sender, MouseButtonEventArgs e)
    {
        if (!pressing) return;
        var open = !pointerMoved;
        CancelPress();
        e.Handled = true;
        if (open) MainWindowRequested?.Invoke(this, EventArgs.Empty);
    }
    private void CancelPress()
    {
        pressing = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
    internal void WithMovement(Action action)
    {
        var previous = allowMovement;
        allowMovement = true;
        try { action(); } finally { allowMovement = previous; }
    }
    internal void MoveFromAutomation(double left, double top)
    {
        Dispatcher.VerifyAccess();
        if (IsLocked) throw new InvalidOperationException("组件位置已锁定。");
        if (!double.IsFinite(left) || !double.IsFinite(top) || left is <= int.MinValue or >= int.MaxValue || top is <= int.MinValue or >= int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(left));
        WithMovement(() => WidgetWindowPlacement.MoveIntoWorkArea(Handle, new((int)Math.Round(left), (int)Math.Round(top))));
        PositionCommitted?.Invoke(this, EventArgs.Empty);
    }
    protected override AutomationPeer OnCreateAutomationPeer() => new WidgetWindowAutomationPeer(this);
    private sealed class WidgetWindowAutomationPeer(WidgetWindow owner) : WindowAutomationPeer(owner), ITransformProvider, IInvokeProvider
    {
        public override object? GetPattern(PatternInterface pattern) => pattern is PatternInterface.Transform or PatternInterface.Invoke ? this : base.GetPattern(pattern);
        public void Invoke() => owner.Dispatcher.BeginInvoke(() => owner.MainWindowRequested?.Invoke(owner, EventArgs.Empty));
        public bool CanMove => !owner.IsLocked;
        public bool CanResize => false;
        public bool CanRotate => false;
        public void Move(double x, double y) => owner.MoveFromAutomation(x, y);
        public void Resize(double width, double height) => throw new InvalidOperationException("当前布局不支持调整尺寸。");
        public void Rotate(double degrees) => throw new InvalidOperationException("组件不支持旋转。");
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowPosition { public nint Window; public nint After; public int X; public int Y; public int Width; public int Height; public uint Flags; }
}
