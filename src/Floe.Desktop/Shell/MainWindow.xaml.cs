using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Floe.Desktop.Platform;
using Floe.Desktop.Themes;

namespace Floe.Desktop.Shell;

public partial class MainWindow : Window
{
    private readonly WindowBackdrop backdrop;
    private readonly IntegratedWindowChrome chrome;
    private readonly ThemeService theme;

    public MainWindow(ShellViewModel viewModel, ThemeService theme)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        this.theme = theme;
        DataContext = viewModel;
        backdrop = new WindowBackdrop(this, theme);
        chrome = new IntegratedWindowChrome(this, MaximizeButton);
        PreviewKeyDown += OnWindowKey;
    }
    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnMaximize(object sender, RoutedEventArgs e) => IntegratedWindowChrome.ToggleMaximize(this);
    private void OnClose(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void OnWindowKey(object sender, KeyEventArgs e)
    {
        if (e.SystemKey == Key.Space && Keyboard.Modifiers == ModifierKeys.Alt) { OpenWindowMenu(); e.Handled = true; }
        else if (e.SystemKey == Key.F4 && Keyboard.Modifiers == ModifierKeys.Alt) { SystemCommands.CloseWindow(this); e.Handled = true; }
    }
    private void OnWindowMenu(object sender, MouseButtonEventArgs e) { OpenWindowMenu(); e.Handled = true; }
    private void OpenWindowMenu()
    {
        var menu = new ContextMenu { Style = (Style)FindResource("TrayContextMenu"), ItemContainerStyle = (Style)FindResource("TrayMenuItem"), PlacementTarget = this };
        void Add(string title, Action action, bool enabled = true)
        { var item = new MenuItem { Header = title, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("还原", () => SystemCommands.RestoreWindow(this), WindowState == WindowState.Maximized);
        Add("最小化", () => SystemCommands.MinimizeWindow(this));
        Add("最大化", () => SystemCommands.MaximizeWindow(this), WindowState != WindowState.Maximized);
        Add("关闭", () => SystemCommands.CloseWindow(this));
        Motion.MenuMotion.Attach(menu);
        menu.IsOpen = true;
    }
    internal MessageBoxResult ShowSaveDialog(string message) => Controls.GlassDialog.Show(this, theme, message,
        "Floe · 保存未完成", MessageBoxButton.YesNo, MessageBoxResult.No);

    protected override void OnClosed(EventArgs e)
    {
        backdrop.Dispose();
        chrome.Dispose();
        base.OnClosed(e);
    }
}
