using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Application.Widgets;
using Floe.Application.Storage;
using Floe.Desktop.Platform;
using Floe.Desktop.Themes;
using Floe.Domain.Widgets;

namespace Floe.Desktop.Widgets;

/// <summary>Owns optional window lifetime, layout registration and display recovery, never focus accounting.</summary>
internal sealed class WidgetManager : IDisposable
{
    private readonly WidgetController controller;
    private readonly ThemeService theme;
    private readonly Dispatcher dispatcher;
    private readonly FocusWidgetViewModel viewModel;
    private Window? mainWindow;
    private WidgetWindow? window;
    private WidgetMotion? motion;
    private WidgetSettings lastSettings;
    private bool started;
    private bool recovering;
    private bool shuttingDown;
    private bool disposed;
    public WidgetSettingsViewModel SettingsViewModel { get; }
    public event EventHandler? MainWindowRequested;

    public WidgetManager(WidgetController controller, FocusController focus, TodoController todo, ThemeService theme, Dispatcher dispatcher)
    {
        this.controller = controller; this.theme = theme; this.dispatcher = dispatcher;
        lastSettings = controller.Settings;
        SettingsViewModel = new(controller, dispatcher);
        viewModel = new(focus, todo, controller, dispatcher, OpenMain);
        controller.Changed += OnSettingsChanged;
        theme.Changed += OnThemeChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaysChanged;
    }
    public void Start(Window main)
    {
        mainWindow = main;
        started = true;
        Apply(controller.Settings, null);
    }
    private void OnSettingsChanged(object? sender, WidgetSettings settings)
    {
        if (!dispatcher.CheckAccess()) { dispatcher.BeginInvoke(() => OnSettingsChanged(sender, controller.Settings)); return; }
        var previous = lastSettings;
        lastSettings = settings;
        Apply(settings, previous);
    }
    private void Apply(WidgetSettings settings, WidgetSettings? previous)
    {
        if (!started || disposed || shuttingDown) return;
        if (!settings.IsVisible)
        {
            viewModel.Deactivate();
            motion?.SetVisible(false, false);
            return;
        }
        if (window is null)
        {
            var layout = WidgetLayouts.Resolve(settings.LayoutId);
            window = new(viewModel, controller, layout, theme);
            motion = new(window, layout);
            motion.Changed += OnMotionChanged;
            window.Closing += OnClosing;
            window.Closed += OnClosed;
            window.PositionCommitted += OnPositionCommitted;
            window.MainWindowRequested += OnMainWindowRequested;
            window.BackdropChanged += OnBackdropChanged;
        }
        window.Topmost = settings.IsTopmost;
        ApplyAppearance();
        var wasVisible = window.IsVisible;
        viewModel.Activate();
        motion!.SetExtent(settings.IsCompact, theme.AnimationsEnabled);
        motion.SetVisible(true, false);
        if (!wasVisible) RecoverPosition();
        else if (!recovering && (previous?.LeftPixels != settings.LeftPixels || previous?.TopPixels != settings.TopPixels)) RecoverPosition();
        UpdateDiagnostics();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (shuttingDown || disposed) return;
        e.Cancel = true;
        controller.SetVisible(false); // Alt+F4 hides just the widget; app lifetime remains owned by the main window.
    }
    private void OnClosed(object? sender, EventArgs e) { viewModel.Deactivate(); }
    private void OnPositionCommitted(object? sender, EventArgs e)
    {
        if (window is null) return;
        try
        {
            window.WithMovement(() => WidgetWindowPlacement.MoveIntoWorkArea(window.Handle, WidgetWindowPlacement.GetPosition(window.Handle)));
            var position = WidgetWindowPlacement.GetPosition(window.Handle);
            controller.SavePosition(position.Left, position.Top);
        }
        catch (DataStoreException) { RecoverPosition(save: false); throw; }
    }
    private void RecoverPosition(bool save = true)
    {
        if (window is null || mainWindow is null || !window.IsVisible || recovering) return;
        recovering = true;
        try
        {
            window.WithMovement(() => WidgetWindowPlacement.Restore(window.Handle, new WindowInteropHelper(mainWindow).Handle, controller.Settings));
            if (save)
            {
                var position = WidgetWindowPlacement.GetPosition(window.Handle);
                controller.SavePosition(position.Left, position.Top);
            }
        }
        finally { recovering = false; }
    }
    private void OnDisplaysChanged(object? sender, EventArgs e)
    {
        if (disposed || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(() => { if (!disposed) RecoverPosition(); }, DispatcherPriority.Background);
    }
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyAppearance();
        if (window is null || motion is null || disposed || shuttingDown) return;
        motion.SetExtent(controller.Settings.IsCompact, theme.AnimationsEnabled);
        motion.SetVisible(controller.Settings.IsVisible, false);
    }
    private void OnMotionChanged(object? sender, EventArgs e)
    {
        UpdateDiagnostics();
        if (motion?.State == "Idle" && !recovering && !shuttingDown && controller.Settings.IsVisible) RecoverPosition();
    }
    private void OnBackdropChanged(object? sender, EventArgs e) => UpdateDiagnostics();
    private void OnMainWindowRequested(object? sender, EventArgs e) => OpenMain();
    private void ApplyAppearance()
    {
        if (window is null || disposed) return;
        // Text remains opaque. Material translucency belongs to the design system, never Window.Opacity.
        window.Opacity = 1;
        UpdateDiagnostics();
    }
    private void UpdateDiagnostics()
    {
        if (window is null) return;
        AutomationProperties.SetHelpText(window, FormattableString.Invariant($"Topmost={window.Topmost};Locked={controller.Settings.IsPositionLocked};Opacity={window.Opacity:0.00};Layout={controller.Settings.LayoutId};Taskbar=False;Compact={controller.Settings.IsCompact};Motion={motion?.State};Backdrop={(window.HasAcrylic ? "Acrylic" : "Solid")}"));
    }
    private void OpenMain()
    {
        if (MainWindowRequested is not null) { MainWindowRequested.Invoke(this, EventArgs.Empty); return; }
        if (mainWindow is null) return;
        mainWindow.Show();
        if (mainWindow.WindowState == WindowState.Minimized) mainWindow.WindowState = WindowState.Normal;
        mainWindow.Activate();
    }
    public void PrepareForShutdown() { shuttingDown = true; motion?.Stop(); viewModel.Deactivate(); }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; shuttingDown = true;
        if (motion is not null) { motion.Changed -= OnMotionChanged; motion.Stop(); }
        controller.Changed -= OnSettingsChanged; theme.Changed -= OnThemeChanged; SystemEvents.DisplaySettingsChanged -= OnDisplaysChanged;
        viewModel.Dispose(); SettingsViewModel.Dispose();
        if (window is not null)
        {
            window.Closing -= OnClosing; window.Closed -= OnClosed; window.PositionCommitted -= OnPositionCommitted;
            window.BackdropChanged -= OnBackdropChanged;
            window.MainWindowRequested -= OnMainWindowRequested;
            if (PresentationSource.FromVisual(window) is not null) window.Close();
        }
    }
}
