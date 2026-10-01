using System.Windows.Controls;
using System.Windows;
using System.Windows.Threading;

namespace Floe.Desktop.Features.Tasks;

public partial class TasksPage : UserControl
{
    private TasksPageViewModel? viewModel;
    public TasksPage()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => viewModel?.SetFocusVisible(IsLoaded && IsVisible);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => AttachViewModel();
    private void OnUnloaded(object sender, RoutedEventArgs e) => DetachViewModel();
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded) AttachViewModel();
    }

    private void AttachViewModel()
    {
        DetachViewModel();
        viewModel = DataContext as TasksPageViewModel;
        if (viewModel is not null) { viewModel.EditorFocusRequested += OnEditorFocusRequested; viewModel.SetFocusVisible(IsVisible); }
    }

    private void DetachViewModel()
    {
        if (viewModel is not null) { viewModel.EditorFocusRequested -= OnEditorFocusRequested; viewModel.SetFocusVisible(false); }
        viewModel = null;
    }

    private void OnEditorFocusRequested(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (!IsLoaded) return;
        TitleInput.BringIntoView();
        TitleInput.Focus();
        TitleInput.SelectAll();
    }, DispatcherPriority.Input);
}
