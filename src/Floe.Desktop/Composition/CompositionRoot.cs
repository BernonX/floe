using Floe.Desktop.Features.Home;
using Floe.Desktop.Features.Settings;
using Floe.Desktop.Features.Statistics;
using Floe.Desktop.Features.Tasks;
using Floe.Desktop.Shell;
using Floe.Desktop.Themes;
using Floe.Application.Timing;
using Floe.Domain.Pomodoro;
using Floe.Infrastructure.Time;
using Floe.Desktop.Platform;
using Floe.Application.Tasks;
using Floe.Application.Storage;
using Floe.Application.Statistics;
using Floe.Application.Widgets;
using Floe.Desktop.Widgets;
using Floe.Application.SystemIntegration;
using Floe.Infrastructure.Platform;

namespace Floe.Desktop.Composition;

internal static class CompositionRoot
{
    public static DesktopSession CreateSession(ThemeService theme, IAppStore store, StoredAppState restored, string dataPath)
    {
        // Explicit constructor injection keeps the initial object graph small and reviewable.
        // Future application services will be shared here by the shell, tray, and widgets.
        var clock = new WindowsAwakeTimeProvider();
        var todo = new TodoController(clock, store, restored);
        var controller = new FocusController(clock, restored.PomodoroSettings, todo: todo, store: store, restored: restored);
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var home = new HomePageViewModel(controller, todo,
            TimeZoneInfo.FindSystemTimeZoneById(controller.Snapshot.StatisticsTimeZoneId), dispatcher);
        var tasks = new TasksPageViewModel(todo, controller, dispatcher);
        var statistics = new StatisticsPageViewModel(new StatisticsService(controller, todo), controller, todo, dispatcher);
        var widgetController = new WidgetController(store, restored.WidgetSettings);
        var widgets = new WidgetManager(widgetController, controller, todo, theme, dispatcher);
        var systemSettings = new SystemSettingsController(store, restored.SystemSettings);
        var systemViewModel = new SystemSettingsViewModel(systemSettings, WindowsStartupRegistration.ForCurrentProcess(), dispatcher);
        var sleep = new SystemSleepSubscription(controller.Suspend, controller.ResumeFromSleep, controller.Refresh);
        NavigationItemViewModel[] navigationItems =
        [
            new("首页", "M3,10 L12,3 21,10 M5,9 L5,21 10,21 10,14 14,14 14,21 19,21 19,9", home),
            new("待办", "M4,6 L6,8 9,4 M12,6 L21,6 M4,13 L6,15 9,11 M12,13 L21,13 M4,20 L8,20 M12,20 L21,20", tasks),
            new("统计", "M4,20 L4,12 M12,20 L12,4 M20,20 L20,8", statistics),
            new("设置", "M3,6 L21,6 M3,12 L21,12 M3,18 L21,18 M8,3 L8,9 M16,9 L16,15 M9,15 L9,21", new SettingsPageViewModel(theme, new PomodoroSettingsViewModel(controller), dataPath, widgets.SettingsViewModel, systemViewModel))
        ];

        return new DesktopSession(new MainWindow(new ShellViewModel(navigationItems), theme), home, tasks, statistics, controller, sleep, widgets,
            widgetController, todo, systemSettings, systemViewModel);
    }
}
