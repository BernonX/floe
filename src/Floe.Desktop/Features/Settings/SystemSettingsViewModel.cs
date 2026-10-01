using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Floe.Application.Storage;
using Floe.Application.SystemIntegration;
using System.Windows.Threading;

namespace Floe.Desktop.Features.Settings;

public sealed partial class SystemSettingsViewModel : ObservableObject, IDisposable
{
    private readonly SystemSettingsController controller;
    private readonly IStartupRegistration startup;
    private readonly Dispatcher dispatcher;
    private bool startupEnabled;
    private bool disposed;
    [ObservableProperty] private string status = "";
    public event EventHandler? ExitRequested;
    public event EventHandler? TestReminderRequested;

    public SystemSettingsViewModel(SystemSettingsController controller, IStartupRegistration startup, Dispatcher dispatcher)
    {
        this.controller = controller; this.startup = startup; this.dispatcher = dispatcher;
        controller.Changed += OnChanged;
        RefreshStartup();
    }

    public bool NotificationsEnabled { get => controller.Settings.NotificationsEnabled; set => Change(() => controller.Update(s => s with { NotificationsEnabled = value })); }
    public bool SoundEnabled { get => controller.Settings.SoundEnabled; set => Change(() => controller.Update(s => s with { SoundEnabled = value })); }
    public bool CloseToTray { get => controller.Settings.CloseToTray; set => Change(() => controller.Update(s => s with { CloseToTray = value })); }
    public bool MinimizeToTray { get => controller.Settings.MinimizeToTray; set => Change(() => controller.Update(s => s with { MinimizeToTray = value })); }
    public bool StartWithWindows
    {
        get => startupEnabled;
        set
        {
            Change(() => startup.SetEnabled(value));
            RefreshStartup();
        }
    }
    public void RefreshStartup()
    {
        try { startupEnabled = startup.IsEnabled; }
        catch (SystemIntegrationException error) { Status = error.Message; }
        OnPropertyChanged(nameof(StartWithWindows));
    }
    private void Change(Action action)
    {
        try { action(); Status = "已保存"; }
        catch (Exception error) when (error is DataStoreException or SystemIntegrationException) { Status = error.Message; }
        finally { OnPropertyChanged(string.Empty); }
    }
    private void OnChanged(object? sender, SystemSettings settings)
    {
        if (dispatcher.CheckAccess()) OnPropertyChanged(string.Empty);
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(() => { if (!disposed) OnPropertyChanged(string.Empty); });
    }
    [RelayCommand] private void ExitApp() => ExitRequested?.Invoke(this, EventArgs.Empty);
    [RelayCommand] private void TestReminder()
    {
        TestReminderRequested?.Invoke(this, EventArgs.Empty);
        Status = NotificationsEnabled || SoundEnabled ? "已请求提醒。Windows 勿扰模式或通知设置可能阻止显示。" : "提醒和声音均已关闭。";
    }
    public void Dispose() { disposed = true; controller.Changed -= OnChanged; }
}
