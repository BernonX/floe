using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Floe.Application.Widgets;
using Floe.Domain.Widgets;
namespace Floe.Desktop.Widgets;
public sealed partial class WidgetSettingsViewModel : ObservableObject, IDisposable
{
    private readonly WidgetController controller;
    private readonly Dispatcher dispatcher;
    private bool disposed;
    public WidgetSettingsViewModel(WidgetController controller, Dispatcher dispatcher)
    { this.controller = controller; this.dispatcher = dispatcher; controller.Changed += OnChanged; }
    public bool IsVisible { get => controller.Settings.IsVisible; set => Change(() => controller.SetVisible(value)); }
    public bool IsCompact { get => controller.Settings.IsCompact; set => Change(() => controller.SetCompact(value)); }
    public bool IsTopmost { get => controller.Settings.IsTopmost; set => Change(() => controller.SetTopmost(value)); }
    public bool IsPositionLocked { get => controller.Settings.IsPositionLocked; set => Change(() => controller.SetLocked(value)); }
    public string PositionText => controller.Settings.LeftPixels is null ? "首次显示时放在当前屏幕右下方。" : "位置已保存在本机；屏幕变化时自动移回可见区域。";
    private void Change(Action action) { try { action(); } finally { OnPropertyChanged(string.Empty); } }
    [RelayCommand] private void ResetPosition() => controller.ResetPosition();
    private void OnChanged(object? sender, WidgetSettings e)
    {
        if (dispatcher.CheckAccess()) Refresh();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(Refresh, DispatcherPriority.DataBind);
    }
    private void Refresh() { if (!disposed) OnPropertyChanged(string.Empty); }
    public void Dispose() { disposed = true; controller.Changed -= OnChanged; }
}
