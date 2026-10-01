using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Shell;
using Floe.Desktop.Platform;
using Floe.Desktop.Themes;

namespace Floe.Desktop.Controls;

/// <summary>Modal presentation only; callers retain the existing save/exit decision.</summary>
internal static class GlassDialog
{
    public static MessageBoxResult Show(Window? owner, ThemeService theme, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxResult defaultResult = MessageBoxResult.OK)
    {
        var result = defaultResult;
        var dialog = new Window { Title = title, Icon = owner?.Icon, Width = 460, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.SingleBorderWindow, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = owner?.IsVisible == true ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner, Background = System.Windows.Media.Brushes.Transparent,
            FontFamily = (System.Windows.Media.FontFamily)System.Windows.Application.Current.FindResource("UiFontFamily") };
        SurfaceProperties.SetMaterialsEnabled(dialog, theme.MaterialsEnabled);
        SurfaceProperties.SetAnimationsEnabled(dialog, theme.AnimationsEnabled);
        WindowChrome.SetWindowChrome(dialog, new WindowChrome { CaptionHeight = 44, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(-1), UseAeroCaptionButtons = false });
        var content = new StackPanel();
        content.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 18) });
        content.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 0, 0, 24) });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        void Add(string label, MessageBoxResult value, string id, bool cancel)
        {
            var button = new Button { Content = label, Style = (Style)dialog.FindResource(value == MessageBoxResult.Yes ? "SecondaryButton" : "PrimaryButton"),
                Margin = new Thickness(8, 0, 0, 0), IsDefault = value == defaultResult, IsCancel = cancel };
            AutomationProperties.SetAutomationId(button, id);
            button.Click += (_, _) => { result = value; dialog.DialogResult = true; };
            actions.Children.Add(button);
        }
        if (buttons == MessageBoxButton.YesNo) { Add("仍然退出", MessageBoxResult.Yes, "6", false); Add("返回并重试", MessageBoxResult.No, "7", true); }
        else Add("知道了", MessageBoxResult.OK, "1", true);
        content.Children.Add(actions);
        var surface = new GlassSurface { Content = content, Padding = new Thickness(28), CornerRadius = new CornerRadius(20) };
        surface.SetResourceReference(Control.ForegroundProperty, "PrimaryTextBrush");
        dialog.Content = surface;
        using var backdrop = new WindowBackdrop(dialog, theme);
        dialog.ShowDialog();
        return result;
    }
}
