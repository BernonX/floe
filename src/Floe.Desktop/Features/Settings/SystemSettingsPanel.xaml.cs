using System.Windows;
using System.Windows.Controls;

namespace Floe.Desktop.Features.Settings;

public partial class SystemSettingsPanel : UserControl
{
    public SystemSettingsPanel() => InitializeComponent();
    private void OnLoaded(object sender, RoutedEventArgs e)
    { if (DataContext is SystemSettingsViewModel viewModel) viewModel.RefreshStartup(); }
}
