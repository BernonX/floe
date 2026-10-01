using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using Floe.Desktop.Motion;

namespace Floe.Desktop.Controls;

/// <summary>Presentation and accessibility only; the view model supplies the timer state.</summary>
public class TimerDisplay : Control
{
    private readonly MotionValue opacity;
    public TimerDisplay()
    {
        opacity = new(this, OpacityProperty);
        Unloaded += (_, _) => opacity.MoveTo(1, MotionTiming.Feedback, false);
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) opacity.MoveTo(1, MotionTiming.Feedback, false);
            else if (IsLoaded && SurfaceProperties.GetAnimationsEnabled(this)) opacity.MoveTo(1, MotionTiming.Feedback, true, from: 0.9);
        };
    }
    public static readonly DependencyProperty TransitionKeyProperty = DependencyProperty.Register(
        nameof(TransitionKey), typeof(string), typeof(TimerDisplay), new PropertyMetadata("", OnStateChanged));
    public string TransitionKey { get => (string)GetValue(TransitionKeyProperty); set => SetValue(TransitionKeyProperty, value); }
    private static void OnStateChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        var display = (TimerDisplay)target;
        if (display.IsLoaded && display.IsVisible && SurfaceProperties.GetAnimationsEnabled(display))
            display.opacity.MoveTo(1, MotionTiming.Feedback, true, from: 0.86);
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == SurfaceProperties.AnimationsEnabledProperty && !(bool)e.NewValue) opacity?.MoveTo(1, MotionTiming.Feedback, false);
    }
    public static readonly DependencyProperty TimeTextProperty = DependencyProperty.Register(
        nameof(TimeText), typeof(string), typeof(TimerDisplay), new PropertyMetadata("--:--"));

    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(TimerDisplay), new PropertyMetadata("FOCUS"));

    public string TimeText
    {
        get => (string)GetValue(TimeTextProperty);
        set => SetValue(TimeTextProperty, value);
    }

    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TimerDisplayAutomationPeer(this);

    private sealed class TimerDisplayAutomationPeer(TimerDisplay owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        protected override string GetClassNameCore() => nameof(TimerDisplay);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;
        public override object? GetPattern(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPattern(patternInterface);
        public bool IsReadOnly => true;
        public string Value => ((TimerDisplay)Owner).TimeText;
        public void SetValue(string value) => throw new InvalidOperationException("The timer display is read-only.");
    }
}
