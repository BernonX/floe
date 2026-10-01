using System.Windows;
using System.Windows.Media.Animation;

namespace Floe.Desktop.Motion;

internal static class MotionTiming
{
    public static readonly TimeSpan Feedback = TimeSpan.FromMilliseconds(180);
    public static readonly TimeSpan Press = TimeSpan.FromMilliseconds(150);
    public static readonly TimeSpan Page = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan Widget = TimeSpan.FromMilliseconds(180);
}

/// <summary>One replaceable clock. The final base value is set first, so removing a clock never leaves a frozen frame.</summary>
internal sealed class MotionValue(DependencyObject target, DependencyProperty property)
{
    private long generation;
    public bool IsActive { get; private set; }
    public double Destination { get; private set; } = (double)target.GetValue(property);

    public void MoveTo(double destination, TimeSpan duration, bool animate, Action? completed = null, double? from = null)
    {
        if (animate && IsActive && Destination == destination && from is null) return;
        var current = from ?? (double)target.GetValue(property);
        Stop();
        Destination = destination;
        target.SetCurrentValue(property, destination);
        if (!animate || Math.Abs(current - destination) < 0.0001) { completed?.Invoke(); return; }
        var token = generation;
        var animation = new DoubleAnimation(current, destination, duration)
        { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        animation.Completed += (_, _) =>
        {
            if (token != generation) return;
            Stop();
            completed?.Invoke();
        };
        IsActive = true;
        Apply(animation);
    }

    public void Stop()
    {
        generation++;
        Apply(null);
        IsActive = false;
    }
    private void Apply(DoubleAnimation? animation)
    {
        if (target is UIElement element) element.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        else if (target is Animatable animatable) animatable.BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
        else throw new InvalidOperationException("The motion target does not support animation.");
    }
}
