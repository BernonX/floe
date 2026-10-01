using Floe.Application.Storage;
using Floe.Domain.Widgets;

namespace Floe.Application.Widgets;

/// <summary>Persists before publishing. Widget settings have their own store operation, separate from appearance.</summary>
public sealed class WidgetController
{
    private readonly IAppStore? store;
    private readonly object gate = new();
    private WidgetSettings settings;
    public WidgetController(IAppStore? store = null, WidgetSettings? restored = null)
    {
        this.store = store;
        settings = restored ?? new();
        settings.Validate();
    }
    public WidgetSettings Settings { get { lock (gate) return settings; } }
    public event EventHandler<WidgetSettings>? Changed;
    public void SetVisible(bool value) => Update(current => current with { IsVisible = value });
    public void SetCompact(bool value) => Update(current => current with { IsCompact = value });
    public void SetTopmost(bool value) => Update(current => current with { IsTopmost = value });
    public void SetLocked(bool value) => Update(current => current with { IsPositionLocked = value });
    public void SetOpacity(double value) => Update(current => current with { Opacity = value });
    public void SavePosition(int left, int top) => Update(current => current with { LeftPixels = left, TopPixels = top });
    public void ResetPosition() => Update(current => current with { LeftPixels = null, TopPixels = null });
    private void Update(Func<WidgetSettings, WidgetSettings> change)
    {
        WidgetSettings next;
        lock (gate)
        {
            next = change(settings);
            next.Validate();
            if (next == settings) return;
            store?.SaveWidgetSettings(next);
            settings = next;
        }
        Changed?.Invoke(this, next);
    }
}

public readonly record struct PixelWorkArea(int Left, int Top, int Right, int Bottom);
public readonly record struct WidgetPosition(int Left, int Top);

public static class WidgetPlacement
{
    // A smaller work area must still leave the widget's drag strip accessible.
    public static WidgetPosition Fit(WidgetPosition requested, int width, int height, PixelWorkArea workArea)
    {
        if (width <= 0 || height <= 0 || workArea.Right <= workArea.Left || workArea.Bottom <= workArea.Top)
            throw new ArgumentOutOfRangeException(nameof(workArea));
        return new((int)Math.Clamp((long)requested.Left, workArea.Left, Math.Max((long)workArea.Left, (long)workArea.Right - width)),
            (int)Math.Clamp((long)requested.Top, workArea.Top, Math.Max((long)workArea.Top, (long)workArea.Bottom - height)));
    }
}
