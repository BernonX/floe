using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Floe.Application.Timing;
using Floe.Domain.Pomodoro;

namespace Floe.Desktop.Features.Settings;

public sealed partial class PomodoroSettingsViewModel : ObservableObject
{
    private readonly FocusController controller;
    public PomodoroSettingsViewModel(FocusController controller)
    {
        this.controller = controller;
        var settings = controller.ConfiguredSettings;
        focusMinutes = settings.FocusDuration.TotalMinutes.ToString("0", CultureInfo.InvariantCulture);
        shortBreakMinutes = settings.ShortBreakDuration.TotalMinutes.ToString("0", CultureInfo.InvariantCulture);
        longBreakMinutes = settings.LongBreakDuration.TotalMinutes.ToString("0", CultureInfo.InvariantCulture);
        focusesPerCycle = settings.FocusesPerCycle.ToString(CultureInfo.InvariantCulture);
        autoAdvance = settings.AutoAdvance;
        autoStartBreak = settings.AutoStartBreak;
        autoStartFocus = settings.AutoStartFocus;
    }
    [ObservableProperty] private string focusMinutes = "25";
    [ObservableProperty] private string shortBreakMinutes = "5";
    [ObservableProperty] private string longBreakMinutes = "15";
    [ObservableProperty] private string focusesPerCycle = "4";
    [ObservableProperty] private bool autoAdvance = true;
    [ObservableProperty] private bool autoStartBreak;
    [ObservableProperty] private bool autoStartFocus;
    [ObservableProperty] private string feedback = "尚未开始时立即生效，进行中则从下一轮使用。";

    public string ValidationMessage =>
        !TryInteger(FocusMinutes, 180, out _) ? "专注时长请输入 1–180 的整数。" :
        !TryInteger(ShortBreakMinutes, 60, out _) ? "短休息请输入 1–60 的整数。" :
        !TryInteger(LongBreakMinutes, 120, out _) ? "长休息请输入 1–120 的整数。" :
        !TryInteger(FocusesPerCycle, 12, out _) ? "每轮番茄数量请输入 1–12 的整数。" : string.Empty;

    partial void OnFocusMinutesChanged(string value) => DraftChanged();
    partial void OnShortBreakMinutesChanged(string value) => DraftChanged();
    partial void OnLongBreakMinutesChanged(string value) => DraftChanged();
    partial void OnFocusesPerCycleChanged(string value) => DraftChanged();
    partial void OnAutoAdvanceChanged(bool value) => DraftChanged();
    partial void OnAutoStartBreakChanged(bool value) => DraftChanged();
    partial void OnAutoStartFocusChanged(bool value) => DraftChanged();

    private void DraftChanged()
    {
        OnPropertyChanged(nameof(ValidationMessage));
        ApplySettingsCommand.NotifyCanExecuteChanged();
        Feedback = "修改后点击应用，参数将保存在本机。";
    }

    private bool CanApplySettings() => ValidationMessage.Length == 0;

    [RelayCommand(CanExecute = nameof(CanApplySettings))]
    private void ApplySettings()
    {
        if (!CanApplySettings()) return;
        controller.Configure(new PomodoroSettings
        {
            FocusDuration = TimeSpan.FromMinutes(int.Parse(FocusMinutes, CultureInfo.InvariantCulture)),
            ShortBreakDuration = TimeSpan.FromMinutes(int.Parse(ShortBreakMinutes, CultureInfo.InvariantCulture)),
            LongBreakDuration = TimeSpan.FromMinutes(int.Parse(LongBreakMinutes, CultureInfo.InvariantCulture)),
            FocusesPerCycle = int.Parse(FocusesPerCycle, CultureInfo.InvariantCulture),
            AutoAdvance = AutoAdvance,
            AutoStartBreak = AutoStartBreak,
            AutoStartFocus = AutoStartFocus
        });
        Feedback = controller.Snapshot.PersistenceError is not null ? "保存未完成，请在首页检查提示并重试保存。" :
            controller.Current.HasPendingSettings ? "已保存：当前轮保持不变，新参数将在下一轮生效。" : "已保存并应用到当前准备中的轮次。";
    }

    private static bool TryInteger(string value, int maximum, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number >= 1 && number <= maximum;
}
