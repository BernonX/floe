using Floe.Application.Timing;
using Floe.Desktop.Features.Home;
using Floe.Desktop.Platform;
using Floe.Desktop.Shell;
using Floe.Desktop.Features.Tasks;
using Floe.Desktop.Features.Statistics;
using Floe.Desktop.Widgets;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Interop;
using Floe.Application.Tasks;
using Floe.Application.Widgets;
using Floe.Application.SystemIntegration;
using Floe.Desktop.Features.Settings;
using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.Desktop.Motion;

namespace Floe.Desktop.Composition;

internal sealed class DesktopSession : IDisposable
{
    private readonly HomePageViewModel home;
    private readonly TasksPageViewModel tasks;
    private readonly StatisticsPageViewModel statistics;
    private readonly FocusController pomodoro;
    private readonly SystemSleepSubscription sleepSubscription;
    private readonly WidgetManager widgets;
    private readonly WidgetController widgetController;
    private readonly SystemSettingsController systemSettings;
    private readonly SystemSettingsViewModel systemViewModel;
    private readonly TrayIcon tray;
    private readonly CompletionNotifications notifications;
    private bool exitRequested;
    private bool disposed;
    private bool presenting;
    private WindowState restoreState = WindowState.Normal;
    public MainWindow MainWindow { get; }

    public DesktopSession(MainWindow window, HomePageViewModel home, TasksPageViewModel tasks,
        StatisticsPageViewModel statistics, FocusController pomodoro, SystemSleepSubscription sleepSubscription, WidgetManager widgets,
        WidgetController widgetController, TodoController todo, SystemSettingsController systemSettings, SystemSettingsViewModel systemViewModel)
    {
        MainWindow = window;
        this.home = home;
        this.tasks = tasks;
        this.statistics = statistics;
        this.pomodoro = pomodoro;
        this.sleepSubscription = sleepSubscription;
        this.widgets = widgets;
        this.widgetController = widgetController;
        this.systemSettings = systemSettings;
        this.systemViewModel = systemViewModel;
        tray = new(window.Dispatcher, systemSettings, CreateTrayMenu, OpenMain);
        notifications = new(pomodoro, todo, systemSettings, tray);
        if (!tray.IsAvailable) systemViewModel.Status = "托盘暂时不可用，关闭窗口将直接退出。";
        systemViewModel.ExitRequested += OnExitRequested;
        systemViewModel.TestReminderRequested += OnTestReminder;
        widgets.MainWindowRequested += OnOpenMain;
        window.Closing += OnClosing;
        window.Closed += OnClosed;
        window.StateChanged += OnStateChanged;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!exitRequested && systemSettings.Settings.CloseToTray && tray.IsAvailable)
        { e.Cancel = true; MainWindow.Hide(); return; }
        e.Cancel = !PrepareForExit();
        if (e.Cancel)
        {
            exitRequested = false;
            // Showing a window during its Closing event is invalid; restore it after cancellation.
            MainWindow.Dispatcher.BeginInvoke(OpenMain);
        }
    }

    public bool PrepareForExit()
    {
        if (pomodoro.PrepareForShutdown()) { widgets.PrepareForShutdown(); return true; }
        var confirmed = MainWindow.ShowSaveDialog(
            "本地保存失败。尚未保存的进度可能丢失。是否仍要退出？\n\n可以取消退出，检查磁盘后点击首页的重试保存。") == MessageBoxResult.Yes;
        if (confirmed) widgets.PrepareForShutdown();
        return confirmed;
    }

    public void Start(bool background)
    {
        if (background && tray.IsAvailable) new WindowInteropHelper(MainWindow).EnsureHandle();
        else MainWindow.Show();
        widgets.Start(MainWindow);
    }

    public void OpenMain()
    {
        if (disposed) return;
        presenting = true;
        try
        {
            MainWindow.Show();
            if (MainWindow.WindowState == WindowState.Minimized) MainWindow.WindowState = restoreState;
            MainWindow.Activate();
        }
        finally { presenting = false; }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (MainWindow.WindowState != WindowState.Minimized) restoreState = MainWindow.WindowState;
        else if (!presenting && systemSettings.Settings.MinimizeToTray && tray.IsAvailable) MainWindow.Hide();
    }
    private void OnOpenMain(object? sender, EventArgs e)
    {
        if (MainWindow.DataContext is ShellViewModel shell) shell.NavigateCommand.Execute("0");
        OpenMain();
    }
    private void OnExitRequested(object? sender, EventArgs e) => Exit();
    private void OnTestReminder(object? sender, EventArgs e) => notifications.TestReminder();
    private void OnClosed(object? sender, EventArgs e) => System.Windows.Application.Current.Shutdown();
    private void Exit() { exitRequested = true; MainWindow.Close(); }

    private ContextMenu CreateTrayMenu()
    {
        var snapshot = pomodoro.Snapshot;
        var menu = new ContextMenu { Style = (Style)MainWindow.FindResource("TrayContextMenu") };
        MenuMotion.Attach(menu);
        menu.SetResourceReference(Control.BackgroundProperty, "WindowBrush");
        menu.SetResourceReference(Control.ForegroundProperty, "PrimaryTextBrush");
        menu.SetResourceReference(Control.BorderBrushProperty, "DividerBrush");
        Add("打开主窗口", "TrayOpenMain", OpenMain);
        menu.Items.Add(new Separator { Style = (Style)MainWindow.FindResource("TraySeparator") });
        var p = snapshot.Pomodoro.State;
        Add(p switch { PomodoroState.Running => "暂停番茄钟", PomodoroState.Paused => "继续番茄钟",
            PomodoroState.AwaitingNext => "进入下一阶段", _ => "开始番茄钟" }, "TrayPomodoro", () =>
        {
            var current = pomodoro.Snapshot.Pomodoro;
            pomodoro.Execute(current.State switch { PomodoroState.Running => PomodoroAction.Pause,
                PomodoroState.Paused => PomodoroAction.Resume, PomodoroState.AwaitingNext => PomodoroAction.EnterNext,
                _ => PomodoroAction.Start }, current.StageId);
        }, snapshot.Free.State != FreeFocusState.Running && snapshot.PersistenceError is null);
        var f = snapshot.Free.State;
        Add(f switch { FreeFocusState.Running => "暂停专注计时", FreeFocusState.Paused => "继续专注计时", _ => "开始专注计时" }, "TrayFreeFocus", () =>
        {
            var current = pomodoro.Snapshot.Free;
            pomodoro.ExecuteFree(current.State switch { FreeFocusState.Running => FreeFocusAction.Pause,
                FreeFocusState.Paused => FreeFocusAction.Resume, _ => FreeFocusAction.Start }, current.SessionId);
        }, p != PomodoroState.Running && snapshot.PersistenceError is null);
        var widgetItem = Add("显示桌面组件", "TrayWidget", () => widgetController.SetVisible(!widgetController.Settings.IsVisible));
        widgetItem.IsCheckable = true; widgetItem.IsChecked = widgetController.Settings.IsVisible;
        menu.Items.Add(new Separator { Style = (Style)MainWindow.FindResource("TraySeparator") });
        Add("退出 Floe", "TrayExit", Exit);
        return menu;

        MenuItem Add(string title, string id, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = title, IsEnabled = enabled, Style = (Style)MainWindow.FindResource("TrayMenuItem") };
            AutomationProperties.SetAutomationId(item, id);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        MainWindow.Closing -= OnClosing;
        MainWindow.Closed -= OnClosed;
        MainWindow.StateChanged -= OnStateChanged;
        widgets.MainWindowRequested -= OnOpenMain;
        systemViewModel.ExitRequested -= OnExitRequested;
        systemViewModel.TestReminderRequested -= OnTestReminder;
        notifications.Dispose();
        tray.Dispose();
        systemViewModel.Dispose();
        sleepSubscription.Dispose();
        widgets.Dispose();
        home.Dispose();
        tasks.Dispose();
        statistics.Dispose();
        pomodoro.PrepareForShutdown(); // Also covers application shutdown paths that bypass the window's close request.
        pomodoro.Dispose();
    }
}
