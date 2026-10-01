using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Floe.Desktop.Platform;
using Floe.Desktop.Themes.Materials;
using Floe.Application.Storage;

namespace Floe.Desktop.Themes;

/// <summary>Owns presentation resources; appearance preferences are saved before applying a change.</summary>
public sealed class ThemeService : IDisposable
{
    private readonly System.Windows.Application application;
    private readonly IAppStore? store;
    private ResourceDictionary? palette;
    private bool disposed;
    private bool refreshQueued;

    public ThemeService(System.Windows.Application application, IAppStore? store = null, AppSettings? settings = null)
    {
        this.application = application;
        this.store = store;
        if (settings is not null)
        {
            Preference = (ThemePreference)settings.Theme;
            ReduceTransparency = settings.ReduceTransparency;
            ReduceMotion = settings.ReduceMotion;
        }
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        SystemParameters.StaticPropertyChanged += OnSystemPropertyChanged;
        Refresh();
    }

    public ThemePreference Preference { get; private set; } = ThemePreference.System;
    public bool ReduceTransparency { get; private set; }
    public bool ReduceMotion { get; private set; }
    public bool IsDark { get; private set; }
    public bool IsHighContrast { get; private set; }
    public bool MaterialsEnabled { get; private set; }
    public bool AnimationsEnabled { get; private set; }

    public event EventHandler? Changed;

    public void SetAppearance(ThemePreference preference, bool reduceTransparency, bool reduceMotion)
    {
        application.Dispatcher.VerifyAccess();
        store?.SaveAppSettings(new((AppTheme)preference, reduceTransparency, reduceMotion));
        Preference = preference;
        ReduceTransparency = reduceTransparency;
        ReduceMotion = reduceMotion;
        Refresh();
    }

    private void Refresh()
    {
        if (disposed) return;

        IsHighContrast = SystemParameters.HighContrast;
        IsDark = Preference == ThemePreference.Dark ||
            (Preference == ThemePreference.System && WindowsAppearance.ReadAppsUseLightTheme() == false);
        MaterialsEnabled = !IsHighContrast && !ReduceTransparency &&
            WindowsAppearance.ReadTransparencyEnabled() && !SystemParameters.IsRemoteSession &&
            !WindowsAppearance.IsBatterySaverEnabled() && (RenderCapability.Tier >> 16) >= 2;
        AnimationsEnabled = !IsHighContrast && !ReduceMotion && SystemParameters.ClientAreaAnimation &&
            !SystemParameters.IsRemoteSession && (RenderCapability.Tier >> 16) >= 1;

        var name = IsHighContrast ? "HighContrast" : IsDark ? "Dark" : "Light";
        var replacement = new ResourceDictionary
        {
            Source = new Uri($"/Floe;component/Themes/Tokens/{name}.xaml", UriKind.Relative)
        };
        if (IsHighContrast) replacement["WindowColor"] = SystemColors.WindowColor;
        MaterialPalette.AddBrushes(replacement, MaterialsEnabled);
        // Load the replacement before removing the previous dictionary, avoiding missing-resource frames.
        var dictionaries = application.Resources.MergedDictionaries;
        if (palette is null) dictionaries.Add(replacement);
        else dictionaries[dictionaries.IndexOf(palette)] = replacement;
        palette = replacement;

        application.Resources["MaterialsEnabled"] = MaterialsEnabled;
        application.Resources["AnimationsEnabled"] = AnimationsEnabled;
        application.Resources["AmbientVisibility"] = MaterialsEnabled ? Visibility.Visible : Visibility.Collapsed;
        application.Resources["SurfaceShadowOpacity"] = IsHighContrast || !MaterialsEnabled ? 0.0 : IsDark ? 0.16 : 0.07;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void QueueRefresh()
    {
        // SystemEvents can arrive off the UI thread. Coalesce their resource updates on the dispatcher.
        if (disposed || application.Dispatcher.HasShutdownStarted) return;
        application.Dispatcher.BeginInvoke(() =>
        {
            if (disposed || refreshQueued) return;
            refreshQueued = true;
            application.Dispatcher.BeginInvoke(() =>
            {
                refreshQueued = false;
                Refresh();
            }, DispatcherPriority.Background);
        });
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => QueueRefresh();
    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => QueueRefresh();
    private void OnSystemPropertyChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemParameters.StaticPropertyChanged -= OnSystemPropertyChanged;
    }
}
