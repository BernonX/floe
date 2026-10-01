using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Floe.Desktop.Controls;

namespace Floe.Desktop.Motion;

/// <summary>Template feedback follows IsPressed, including keyboard activation. Subscriptions exist only while loaded.</summary>
public static class ButtonFeedback
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool),
        typeof(ButtonFeedback), new PropertyMetadata(false, OnEnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(ButtonFeedback));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static void OnEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Button button) return;
        (button.GetValue(StateProperty) as State)?.Dispose();
        button.SetValue(StateProperty, (bool)e.NewValue ? new State(button) : null);
    }
    private sealed class State : IDisposable
    {
        private readonly Button button;
        private readonly DependencyPropertyDescriptor[] descriptors;
        private MotionValue? hover;
        private MotionValue? press;
        private bool attached;
        public State(Button button)
        {
            this.button = button;
            descriptors = new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusedProperty, ButtonBase.IsPressedProperty,
                UIElement.IsEnabledProperty, SurfaceProperties.AnimationsEnabledProperty }
                .Select(property => DependencyPropertyDescriptor.FromProperty(property, typeof(Button))!).ToArray();
            button.Loaded += OnLoaded; button.Unloaded += OnUnloaded;
            button.IsVisibleChanged += OnVisibilityChanged;
            if (button.IsLoaded) Attach();
        }
        private void OnLoaded(object sender, RoutedEventArgs e) => Attach();
        private void Attach()
        {
            if (attached) return;
            button.ApplyTemplate();
            if (button.Template?.FindName("Hover", button) is FrameworkElement overlay) hover = new(overlay, UIElement.OpacityProperty);
            if (button.Template?.FindName("Root", button) is FrameworkElement root && root.RenderTransform is TranslateTransform translation)
            {
                var mutable = translation.CloneCurrentValue();
                root.RenderTransform = mutable;
                press = new(mutable, TranslateTransform.YProperty);
            }
            attached = true;
            foreach (var descriptor in descriptors) descriptor.AddValueChanged(button, OnChanged);
            Update();
        }
        private void OnChanged(object? sender, EventArgs e) => Update();
        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => Update();
        private void Update()
        {
            if (!attached) return;
            var animate = button.IsVisible && SurfaceProperties.GetAnimationsEnabled(button);
            hover?.MoveTo(button.IsEnabled ? button.IsMouseOver ? 1 : button.IsKeyboardFocused ? 0.55 : 0 : 0, MotionTiming.Feedback, animate);
            press?.MoveTo(animate && button.IsPressed ? 1 : 0, MotionTiming.Press, animate);
        }
        private void OnUnloaded(object sender, RoutedEventArgs e) => Detach();
        private void Detach()
        {
            if (!attached) return;
            attached = false;
            foreach (var descriptor in descriptors) descriptor.RemoveValueChanged(button, OnChanged);
            hover?.Stop(); press?.Stop(); hover = null; press = null;
        }
        public void Dispose()
        {
            Detach(); button.Loaded -= OnLoaded; button.Unloaded -= OnUnloaded; button.IsVisibleChanged -= OnVisibilityChanged;
        }
    }
}
