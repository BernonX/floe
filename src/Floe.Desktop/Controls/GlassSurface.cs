using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation.Peers;
using Floe.Desktop.Motion;

namespace Floe.Desktop.Controls;

public class GlassSurface : ContentControl
{
    protected override AutomationPeer OnCreateAutomationPeer() => new SurfacePeer(this);
    private sealed class SurfacePeer(GlassSurface owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetClassNameCore() => nameof(GlassSurface);
    }
    private MotionValue? hover;
    public GlassSurface()
    {
        Unloaded += (_, _) => hover?.MoveTo(0, MotionTiming.Feedback, false);
        IsVisibleChanged += (_, _) => UpdateHover();
    }
    public override void OnApplyTemplate()
    {
        hover?.Stop(); base.OnApplyTemplate();
        hover = GetTemplateChild("HoverEdge") is FrameworkElement edge ? new(edge, OpacityProperty) : null;
        UpdateHover();
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsMouseOverProperty || e.Property == SurfaceProperties.AnimationsEnabledProperty ||
            e.Property == SurfaceProperties.MaterialsEnabledProperty) UpdateHover();
    }
    private void UpdateHover() => hover?.MoveTo(IsVisible && IsMouseOver && SurfaceProperties.GetMaterialsEnabled(this) ? 0.25 : 0,
        MotionTiming.Feedback, IsLoaded && IsVisible && SurfaceProperties.GetAnimationsEnabled(this));
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(GlassSurface), new PropertyMetadata(new CornerRadius(24)));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }
}
