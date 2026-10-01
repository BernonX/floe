namespace Floe.Desktop.Shell;

public sealed class NavigationItemViewModel(string title, string iconData, PageViewModel page)
{
    public string Title { get; } = title;

    public string IconData { get; } = iconData;

    public PageViewModel Page { get; } = page;
}
