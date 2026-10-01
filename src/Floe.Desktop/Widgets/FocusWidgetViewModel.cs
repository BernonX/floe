using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Application.Widgets;
using Floe.Domain.Focus;
using Floe.Domain.Pomodoro;
using Floe.Domain.Widgets;
using System.Windows;

namespace Floe.Desktop.Widgets;

/// <summary>Visible widgets consume the same coordinator as the main page; there is no widget timer or ledger.</summary>
public sealed partial class FocusWidgetViewModel : ObservableObject, IDisposable
{
    private readonly FocusController focus;
    private readonly TodoController todo;
    private readonly WidgetController widgets;
    private readonly Dispatcher dispatcher;
    private readonly Action openMain;
    private FocusSnapshot snapshot;
    private int pending;
    private bool active;
    private bool disposed;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPomodoroMode))]
    private bool isFreeMode;

    public FocusWidgetViewModel(FocusController focus, TodoController todo, WidgetController widgets, Dispatcher dispatcher, Action openMain)
    {
        this.focus = focus; this.todo = todo; this.widgets = widgets; this.dispatcher = dispatcher; this.openMain = openMain;
        snapshot = focus.Snapshot;
        isFreeMode = snapshot.Free.State != FreeFocusState.Ready && snapshot.Pomodoro.State is not (PomodoroState.Running or PomodoroState.Paused);
    }
    public bool IsPomodoroMode { get => !IsFreeMode; set { if (value) IsFreeMode = false; } }
    public bool IsCompact => widgets.Settings.IsCompact;
    public Visibility ExpandedVisibility => IsCompact ? Visibility.Collapsed : Visibility.Visible;
    public string ToggleCompactText => IsCompact ? "展开" : "收起";
    public string MotionStateKey => Phase + "|" + StateText;
    public bool CanChoosePomodoro => snapshot.Free.State != FreeFocusState.Running;
    public bool CanChooseFree => snapshot.Pomodoro.State != PomodoroState.Running;
    public bool IsTopmost { get => widgets.Settings.IsTopmost; set { try { widgets.SetTopmost(value); } finally { OnPropertyChanged(nameof(IsTopmost)); } } }
    public bool IsLocked { get => widgets.Settings.IsPositionLocked; set { try { widgets.SetLocked(value); } finally { OnPropertyChanged(nameof(IsLocked)); } } }
    public string LockHint => IsLocked ? "点击返回首页 · 位置已锁定" : "点击返回首页 · 按住拖动";
    public string Phase => IsFreeMode ? "自由专注" : snapshot.Pomodoro.Phase switch { PomodoroPhase.Focus => "专注", PomodoroPhase.ShortBreak => "短休息", _ => "长休息" };
    public string TimerText => IsFreeMode ? Duration(snapshot.Free.Elapsed) : Countdown(snapshot.Pomodoro.Remaining);
    public string TodayText => "今日 " + Duration(snapshot.Today.TotalDuration);
    public string StateText => snapshot.PersistenceError is not null ? "保存失败 · 请打开主窗口重试" : IsFreeMode
        ? snapshot.Free.State switch { FreeFocusState.Running => "进行中", FreeFocusState.Paused => "已暂停", _ => "准备开始" }
        : snapshot.Pomodoro.State switch { PomodoroState.Running => "进行中", PomodoroState.Paused => "已暂停", PomodoroState.AwaitingNext => "阶段已结束", _ => "准备开始" };
    public string TaskText
    {
        get
        {
            var task = IsFreeMode ? snapshot.Free.State != FreeFocusState.Ready ? snapshot.FreeTask : snapshot.SelectedTask
                : snapshot.Pomodoro.Phase == PomodoroPhase.Focus && snapshot.Pomodoro.State is PomodoroState.Running or PomodoroState.Paused ? snapshot.PomodoroTask : snapshot.SelectedTask;
            if (task is null) return "未绑定任务";
            var current = todo.Find(task.Id);
            return (current?.Title ?? task.TitleAtStart) + (current?.IsDeleted == true ? "（已删除）" : current?.IsCompleted == true ? "（已完成）" : string.Empty);
        }
    }
    public string PrimaryText => IsFreeMode ? snapshot.Free.State switch { FreeFocusState.Running => "暂停", FreeFocusState.Paused => "继续", _ => "开始" }
        : snapshot.Pomodoro.State switch { PomodoroState.Running => "暂停", PomodoroState.Paused => "继续", PomodoroState.AwaitingNext => "下一段", _ => "开始" };
    public string SecondaryText => IsFreeMode ? "结束本次" : "跳过";
    partial void OnIsFreeModeChanged(bool value) => Notify();
    private bool CanPrimary() => snapshot.PersistenceError is null && (IsFreeMode ? snapshot.Pomodoro.State != PomodoroState.Running : snapshot.Free.State != FreeFocusState.Running);
    private bool CanSecondary() => snapshot.PersistenceError is null && (IsFreeMode ? snapshot.Free.State != FreeFocusState.Ready : CanPrimary() && snapshot.Pomodoro.State != PomodoroState.AwaitingNext);
    [RelayCommand(CanExecute = nameof(CanPrimary))]
    private void Primary()
    {
        if (IsFreeMode) focus.ExecuteFree(snapshot.Free.State switch { FreeFocusState.Running => FreeFocusAction.Pause, FreeFocusState.Paused => FreeFocusAction.Resume, _ => FreeFocusAction.Start }, snapshot.Free.SessionId);
        else focus.Execute(snapshot.Pomodoro.State switch { PomodoroState.Running => PomodoroAction.Pause, PomodoroState.Paused => PomodoroAction.Resume, PomodoroState.AwaitingNext => PomodoroAction.EnterNext, _ => PomodoroAction.Start }, snapshot.Pomodoro.StageId);
    }
    [RelayCommand(CanExecute = nameof(CanSecondary))]
    private void Secondary()
    {
        if (IsFreeMode) focus.ExecuteFree(FreeFocusAction.Finish, snapshot.Free.SessionId);
        else focus.Execute(PomodoroAction.Skip, snapshot.Pomodoro.StageId);
    }
    [RelayCommand] private void Hide() => widgets.SetVisible(false);
    [RelayCommand] private void ToggleCompact() => widgets.SetCompact(!IsCompact);
    [RelayCommand] private void OpenMain() => openMain();
    public void Activate()
    {
        if (active || disposed) return;
        active = true; focus.Changed += OnFocus; todo.Changed += OnTodo; widgets.Changed += OnSettings;
        Refresh();
    }
    public void Deactivate()
    { active = false; focus.Changed -= OnFocus; todo.Changed -= OnTodo; widgets.Changed -= OnSettings; }
    private void OnFocus(object? sender, FocusUpdate e) => Queue();
    private void OnTodo(object? sender, TodoSnapshot e) => Queue();
    private void OnSettings(object? sender, WidgetSettings e) => Queue();
    private void Queue()
    {
        if (dispatcher.HasShutdownStarted || Interlocked.Exchange(ref pending, 1) != 0) return;
        dispatcher.BeginInvoke(() => { Interlocked.Exchange(ref pending, 0); if (active && !disposed) Refresh(); }, DispatcherPriority.DataBind);
    }
    private void Refresh()
    {
        snapshot = focus.Snapshot;
        if (snapshot.Free.State == FreeFocusState.Running) IsFreeMode = true;
        else if (snapshot.Pomodoro.State == PomodoroState.Running) IsFreeMode = false;
        else if (snapshot.Free.State == FreeFocusState.Paused && snapshot.Pomodoro.State is not PomodoroState.Paused) IsFreeMode = true;
        Notify();
    }
    private void Notify() { OnPropertyChanged(string.Empty); PrimaryCommand.NotifyCanExecuteChanged(); SecondaryCommand.NotifyCanExecuteChanged(); }
    private static string Duration(TimeSpan value) => $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    private static string Countdown(TimeSpan value)
    {
        var seconds = (long)Math.Ceiling(value.TotalSeconds);
        return seconds >= 3600 ? $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
    }
    public void Dispose() { disposed = true; Deactivate(); }
}
