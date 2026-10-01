using System.Windows.Controls;
using System.Windows;

namespace Floe.Desktop.Features.Home;

public partial class HomePage : UserControl
{
    private HomePageViewModel? viewModel;
    public HomePage()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisibility();
        Unloaded += (_, _) => { viewModel?.Deactivate(); viewModel = null; };
        IsVisibleChanged += (_, _) => UpdateVisibility();
        DataContextChanged += (_, _) => { viewModel?.Deactivate(); viewModel = null; UpdateVisibility(); };
    }
    private void UpdateVisibility()
    {
        if (!IsLoaded) return;
        viewModel ??= DataContext as HomePageViewModel;
        if (IsVisible) viewModel?.Activate(); else viewModel?.Deactivate();
    }
}
