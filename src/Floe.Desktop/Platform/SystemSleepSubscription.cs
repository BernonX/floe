using Microsoft.Win32;

namespace Floe.Desktop.Platform;

internal sealed class SystemSleepSubscription : IDisposable
{
    private readonly Action suspend;
    private readonly Action resume;
    private readonly Action timeChanged;

    public SystemSleepSubscription(Action suspend, Action resume, Action timeChanged)
    {
        this.suspend = suspend;
        this.resume = resume;
        this.timeChanged = timeChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemEvents.TimeChanged += OnTimeChanged;
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) suspend();
        else if (e.Mode == PowerModes.Resume) resume();
    }

    private void OnTimeChanged(object? sender, EventArgs e) => timeChanged();

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.TimeChanged -= OnTimeChanged;
    }
}
