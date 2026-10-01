using System.Windows;
using System.IO;
using Floe.Desktop.Composition;
using Floe.Desktop.Themes;
using Floe.Application.Storage;
using Floe.Infrastructure.Storage;
using Floe.Desktop.Diagnostics;

namespace Floe.Desktop;

public partial class App : System.Windows.Application
{
    private ThemeService? theme;
    private DesktopSession? session;
    private SqliteAppStore? store;
    private bool reportingFatalError;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherError;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledError;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskError;
        try
        {
            var dataDirectory = Environment.GetEnvironmentVariable("FLOE_DATA_DIRECTORY");
            if (string.IsNullOrWhiteSpace(dataDirectory))
                dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Floe");
            store = new SqliteAppStore(dataDirectory);
            var restored = store.Load();
            theme = new ThemeService(this, store, restored.AppSettings);
            session = CompositionRoot.CreateSession(theme, store, restored, store.DatabasePath);
            MainWindow = session.MainWindow;
            session.Start(e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is DataStoreException or ArgumentException or TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            DiagnosticLog.Write("Startup data", error);
            if (theme is null) MessageBox.Show(error.Message, "Floe · 无法读取本地数据", MessageBoxButton.OK, MessageBoxImage.Error);
            else Controls.GlassDialog.Show(MainWindow, theme, error.Message, "Floe · 无法读取本地数据");
            Shutdown(1);
        }
        catch (Exception error)
        {
            DiagnosticLog.Write("Startup fatal", error);
            MessageBox.Show("Floe 未能启动。请保留本地数据，诊断信息已尝试写入数据目录的 logs 文件夹。", "Floe · 启动未完成", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherError(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        DiagnosticLog.Write("Dispatcher", e.Exception);
        e.Handled = true;
        if (e.Exception is not DataStoreException error)
        {
            if (!reportingFatalError)
            {
                reportingFatalError = true;
                MessageBox.Show("发生未预期的错误，Floe 将关闭。请保留本地数据与 logs 诊断文件；最近进度以成功保存的检查点为准。", "Floe · 运行中断", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(1);
            return;
        }
        if (theme is not null) Controls.GlassDialog.Show(MainWindow, theme,
            error.Message + "\n本次修改未保存，请检查磁盘空间和目录权限后重试。", "Floe · 保存未完成");
    }
    private void OnUnhandledError(object sender, UnhandledExceptionEventArgs e)
    { if (e.ExceptionObject is Exception error) DiagnosticLog.Write("Unhandled", error); }
    private void OnUnobservedTaskError(object? sender, UnobservedTaskExceptionEventArgs e)
        => DiagnosticLog.Write("Unobserved task", e.Exception);

    protected override void OnExit(ExitEventArgs e)
    {
        session?.Dispose();
        theme?.Dispose();
        store?.Dispose();
        DispatcherUnhandledException -= OnDispatcherError;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledError;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskError;
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        if (session is not null) e.Cancel = !session.PrepareForExit();
        base.OnSessionEnding(e);
    }
}
