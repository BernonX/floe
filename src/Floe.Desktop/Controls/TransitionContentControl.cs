using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Floe.Desktop.Motion;

namespace Floe.Desktop.Controls;

/// <summary>Replaces the old view immediately. Only the new view enters; commands and accessibility never wait for a fade.</summary>
public sealed class TransitionContentControl : ContentControl
{
    private readonly TranslateTransform translation = new();
    private readonly MotionValue opacity;
    private readonly MotionValue offset;
    private long contentVersion;
    public TransitionContentControl()
    {
        Focusable = false; RenderTransform = translation;
        opacity = new(this, OpacityProperty); offset = new(translation, TranslateTransform.YProperty);
        Unloaded += (_, _) => ResetMotion();
        IsVisibleChanged += (_, _) => { if (!IsVisible) ResetMotion(); };
    }
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        var version = ++contentVersion;
        ResetMotion();
        if (IsLoaded && IsVisible && oldContent is not null && SurfaceProperties.GetAnimationsEnabled(this))
        {
            opacity.MoveTo(1, MotionTiming.Page, true, from: 0.9);
            offset.MoveTo(0, MotionTiming.Page, true, from: 6);
        }
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (version != contentVersion || !IsLoaded) return;
            for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is ScrollViewer scroll) { scroll.ScrollToTop(); break; }
        }, DispatcherPriority.Loaded);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == SurfaceProperties.AnimationsEnabledProperty && !(bool)e.NewValue) ResetMotion();
    }
    private void ResetMotion()
    {
        opacity?.MoveTo(1, MotionTiming.Page, false);
        offset?.MoveTo(0, MotionTiming.Page, false);
    }
}
