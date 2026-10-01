using System.Windows.Controls;
using System.Windows;

namespace Floe.Desktop.Features.Statistics;

public partial class StatisticsPage : UserControl
{
    private StatisticsPageViewModel? viewModel;
    public StatisticsPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => { if (IsVisible && IsLoaded) viewModel?.Activate(); else viewModel?.Deactivate(); };
    }
    private void OnLoaded(object sender, RoutedEventArgs e) => Attach();
    private void OnUnloaded(object sender, RoutedEventArgs e) { viewModel?.Deactivate(); viewModel = null; }
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { if (IsLoaded) Attach(); }
    private void Attach()
    {
        viewModel?.Deactivate();
        viewModel = DataContext as StatisticsPageViewModel;
        if (IsVisible) viewModel?.Activate();
    }
}
