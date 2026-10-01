using Floe.Application.Storage;

namespace Floe.Application.SystemIntegration;

public sealed record SystemSettings(bool NotificationsEnabled = true, bool SoundEnabled = false,
    bool CloseToTray = true, bool MinimizeToTray = false);

/// <summary>Local preferences are independent of appearance and widget preferences.</summary>
public sealed class SystemSettingsController(IAppStore? store = null, SystemSettings? restored = null)
{
    private readonly object gate = new();
    private SystemSettings settings = restored ?? new();
    public SystemSettings Settings { get { lock (gate) return settings; } }
    public event EventHandler<SystemSettings>? Changed;
    public void Update(Func<SystemSettings, SystemSettings> change)
    {
        SystemSettings next;
        lock (gate)
        {
            next = change(settings);
            if (next == settings) return;
            store?.SaveSystemSettings(next);
            settings = next;
        }
        Changed?.Invoke(this, next);
    }
}

/// <summary>Windows registration is the source of truth, rather than a second database flag.</summary>
public interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public sealed class SystemIntegrationException(string message, Exception? inner = null) : Exception(message, inner);
