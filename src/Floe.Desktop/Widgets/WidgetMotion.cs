using System.Windows;
using Floe.Desktop.Motion;

namespace Floe.Desktop.Widgets;

/// <summary>Presentation lifetime only. Visibility is committed separately, and rapid reversals replace existing clocks.</summary>
internal sealed class WidgetMotion
{
    private readonly WidgetWindow window;
    private readonly WidgetLayoutDefinition layout;
    private readonly MotionValue opacity;
    private readonly MotionValue height;
    private bool wantsVisible;
    public event EventHandler? Changed;
    public string State => height.IsActive ? "Resizing" : opacity.IsActive ? wantsVisible ? "Showing" : "Hiding" : window.IsVisible ? "Idle" : "Hidden";
    public WidgetMotion(WidgetWindow window, WidgetLayoutDefinition layout)
    {
        this.window = window; this.layout = layout;
        opacity = new(window.MotionSurface, UIElement.OpacityProperty);
        height = new(window, FrameworkElement.HeightProperty);
    }
    public void SetExtent(bool compact, bool animate)
    {
        var target = compact ? layout.CompactHeight ?? layout.Height : layout.Height;
        if (height.Destination == target && (!height.IsActive || animate)) return;
        height.MoveTo(target, MotionTiming.Widget, animate && window.IsVisible, Notify);
        Notify();
    }
    public void SetVisible(bool visible, bool animate)
    {
        var changed = wantsVisible != visible;
        wantsVisible = visible;
        window.IsHitTestVisible = visible;
        if (visible)
        {
            if (!window.IsVisible)
            {
                opacity.MoveTo(animate ? 0 : 1, MotionTiming.Widget, false);
                window.Show();
                changed = true;
            }
            if (changed || !animate && opacity.IsActive) opacity.MoveTo(1, MotionTiming.Widget, animate, Notify);
        }
        else
        {
            height.Stop();
            if (window.IsVisible && (changed || !animate && opacity.IsActive))
                opacity.MoveTo(0, MotionTiming.Widget, animate, () => { if (!wantsVisible) window.Hide(); Notify(); });
        }
        Notify();
    }
    private void Notify() => Changed?.Invoke(this, EventArgs.Empty);
    public void Stop() { height.Stop(); opacity.Stop(); }
}
