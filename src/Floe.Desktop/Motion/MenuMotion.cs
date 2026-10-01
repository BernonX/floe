using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Floe.Desktop.Controls;

namespace Floe.Desktop.Motion;

internal static class MenuMotion
{
    public static void Attach(ContextMenu menu)
    {
        menu.SetResourceReference(SurfaceProperties.AnimationsEnabledProperty, "AnimationsEnabled");
        menu.Opened += (_, _) =>
        {
            if (SurfaceProperties.GetAnimationsEnabled(menu)) return;
            if (LogicalTreeHelper.GetParent(menu) is Popup popup) popup.SetCurrentValue(Popup.PopupAnimationProperty, PopupAnimation.None);
            // The framework starts its default fade before Opened. Public visual/animation APIs finish that first fade too.
            if (PresentationSource.FromVisual(menu)?.RootVisual is UIElement root)
            {
                root.BeginAnimation(UIElement.OpacityProperty, null);
                root.SetCurrentValue(UIElement.OpacityProperty, 1d);
            }
        };
    }
}
