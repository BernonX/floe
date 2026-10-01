using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Floe.Desktop.Shell;

public sealed partial class ShellViewModel : ObservableObject
{
    public ShellViewModel(IEnumerable<NavigationItemViewModel> navigationItems)
    {
        ArgumentNullException.ThrowIfNull(navigationItems);
        NavigationItems = Array.AsReadOnly(navigationItems.ToArray());

        if (NavigationItems.Count == 0)
        {
            throw new ArgumentException("The shell requires at least one page.", nameof(navigationItems));
        }

        selectedItem = NavigationItems[0];
    }

    public IReadOnlyList<NavigationItemViewModel> NavigationItems { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage))]
    private NavigationItemViewModel? selectedItem;

    public PageViewModel CurrentPage => SelectedItem?.Page ?? NavigationItems[0].Page;

    [RelayCommand]
    private void OpenAppearance() => SelectedItem = NavigationItems.First(item => item.Page is Features.Settings.SettingsPageViewModel);

    [RelayCommand]
    private void Navigate(string index)
    {
        if (int.TryParse(index, out var position) && position >= 0 && position < NavigationItems.Count) SelectedItem = NavigationItems[position];
    }
}
