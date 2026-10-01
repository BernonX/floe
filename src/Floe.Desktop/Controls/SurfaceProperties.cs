using System.Windows;

namespace Floe.Desktop.Controls;

// Attached flags let templates respond to system accessibility preferences without view-model coupling.
public static class SurfaceProperties
{
    public static readonly DependencyProperty MaterialsEnabledProperty = DependencyProperty.RegisterAttached(
        "MaterialsEnabled", typeof(bool), typeof(SurfaceProperties), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty AnimationsEnabledProperty = DependencyProperty.RegisterAttached(
        "AnimationsEnabled", typeof(bool), typeof(SurfaceProperties), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetMaterialsEnabled(DependencyObject target) => (bool)target.GetValue(MaterialsEnabledProperty);
    public static void SetMaterialsEnabled(DependencyObject target, bool value) => target.SetValue(MaterialsEnabledProperty, value);
    public static bool GetAnimationsEnabled(DependencyObject target) => (bool)target.GetValue(AnimationsEnabledProperty);
    public static void SetAnimationsEnabled(DependencyObject target, bool value) => target.SetValue(AnimationsEnabledProperty, value);
}
