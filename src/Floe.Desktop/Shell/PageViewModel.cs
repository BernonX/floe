using CommunityToolkit.Mvvm.ComponentModel;

namespace Floe.Desktop.Shell;

public abstract class PageViewModel(string title, string description) : ObservableObject
{
    public string Title { get; } = title;

    public string Description { get; } = description;
}
