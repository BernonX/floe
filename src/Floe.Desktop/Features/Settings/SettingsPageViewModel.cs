using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Floe.Desktop.Shell;
using Floe.Desktop.Themes;
using Floe.Application.Storage;
using Floe.Desktop.Widgets;

namespace Floe.Desktop.Features.Settings;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly ThemeService theme;
    private bool restoringAppearance;

    public SettingsPageViewModel(ThemeService theme, PomodoroSettingsViewModel pomodoro, string dataPath, WidgetSettingsViewModel widgets, SystemSettingsViewModel system) : base("设置", "让空间与习惯，都恰到好处。")
    {
        this.theme = theme;
        Pomodoro = pomodoro;
        DataPath = dataPath;
        Widgets = widgets;
        System = system;
        selectedTheme = theme.Preference;
        reduceTransparency = theme.ReduceTransparency;
        reduceMotion = theme.ReduceMotion;
    }

    public PomodoroSettingsViewModel Pomodoro { get; }
    public string DataPath { get; }
    public WidgetSettingsViewModel Widgets { get; }
    public SystemSettingsViewModel System { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSystemTheme), nameof(IsLightTheme), nameof(IsDarkTheme))]
    private ThemePreference selectedTheme;

    [ObservableProperty]
    private bool reduceTransparency;

    [ObservableProperty]
    private bool reduceMotion;

    public bool IsSystemTheme { get => SelectedTheme == ThemePreference.System; set { if (value) SelectedTheme = ThemePreference.System; } }
    public bool IsLightTheme { get => SelectedTheme == ThemePreference.Light; set { if (value) SelectedTheme = ThemePreference.Light; } }
    public bool IsDarkTheme { get => SelectedTheme == ThemePreference.Dark; set { if (value) SelectedTheme = ThemePreference.Dark; } }

    partial void OnSelectedThemeChanged(ThemePreference value) => Apply();
    partial void OnReduceTransparencyChanged(bool value) => Apply();
    partial void OnReduceMotionChanged(bool value) => Apply();

    private void Apply()
    {
        if (restoringAppearance) return;
        try { theme.SetAppearance(SelectedTheme, ReduceTransparency, ReduceMotion); }
        catch (DataStoreException)
        {
            restoringAppearance = true;
            try
            {
                SelectedTheme = theme.Preference;
                ReduceTransparency = theme.ReduceTransparency;
                ReduceMotion = theme.ReduceMotion;
            }
            finally { restoringAppearance = false; }
            throw;
        }
    }

    [RelayCommand]
    private void ResetAppearance()
    {
        SelectedTheme = ThemePreference.System;
        ReduceTransparency = false;
        ReduceMotion = false;
    }
}
