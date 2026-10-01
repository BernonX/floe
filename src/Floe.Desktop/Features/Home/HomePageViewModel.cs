using System.Windows.Threading;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Floe.Application.Timing;
using Floe.Desktop.Shell;
using Floe.Domain.Pomodoro;
using Floe.Domain.Focus;
using Floe.Application.Tasks;
using System.Collections.ObjectModel;

namespace Floe.Desktop.Features.Home;

public sealed record FocusTaskChoice(Guid? Id, string Caption);

public sealed partial class HomePageViewModel : PageViewModel, IDisposable
{
    private readonly FocusController controller;
    private readonly TodoController todo;
    private readonly TimeZoneInfo statisticsTimeZone;
    private int completedTasksToday;
    private readonly Dispatcher dispatcher;
    private readonly object updateGate = new();
    private FocusSnapshot focusSnapshot;
    private PomodoroSnapshot snapshot => focusSnapshot.Pomodoro;
    private FocusSnapshot? pending;
    private bool dispatchQueued;
    private bool disposed;
    private bool rebuildingChoices;
    private volatile bool active;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPomodoroMode), nameof(PomodoroVisibility), nameof(FreeVisibility))]
    private bool isFreeMode;

    public bool IsPomodoroMode { get => !IsFreeMode; set { if (value) IsFreeMode = false; } }
    public Visibility PomodoroVisibility => IsFreeMode ? Visibility.Collapsed : Visibility.Visible;
    public Visibility FreeVisibility => IsFreeMode ? Visibility.Visible : Visibility.Collapsed;
    public ObservableCollection<FocusTaskChoice> TaskChoices { get; } = [];
    public bool CanChooseTask => focusSnapshot.CanChangeTask;
    public string StorageErrorText => focusSnapshot.PersistenceError ?? string.Empty;
    public Visibility StorageErrorVisibility => focusSnapshot.PersistenceError is null ? Visibility.Collapsed : Visibility.Visible;
    [RelayCommand] private void RetryStorage() => controller.RetrySave();
    private FocusTaskReference? DisplayTask => IsFreeMode
        ? focusSnapshot.Free.State != FreeFocusState.Ready ? focusSnapshot.FreeTask : focusSnapshot.SelectedTask
        : snapshot.Phase == PomodoroPhase.Focus && snapshot.State is PomodoroState.Running or PomodoroState.Paused
            ? focusSnapshot.PomodoroTask : focusSnapshot.SelectedTask;
    public FocusTaskChoice? SelectedTaskChoice
    {
        get => TaskChoices.FirstOrDefault(choice => choice.Id == DisplayTask?.Id);
        set
        {
            if (rebuildingChoices || value is null) return;
            controller.SelectTask(value.Id);
            OnPropertyChanged(nameof(SelectedTaskChoice));
        }
    }
    public string CurrentTaskText => DisplayTask is not { } reference ? "未绑定任务" :
        todo.Find(reference.Id) is { } task ? task.Title + (task.IsDeleted ? "（已删除）" : task.IsCompleted ? "（已完成）" : "") : reference.TitleAtStart;
    public string TaskBindingHint => CanChooseTask ? "开始后固定本次任务" : "本次任务已固定";
    partial void OnIsFreeModeChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedTaskChoice));
        OnPropertyChanged(nameof(CurrentTaskText));
    }
    public string TodayText => FormatDuration(focusSnapshot.Today.TotalDuration);
    public string TodayDateText => focusSnapshot.Today.Date.ToString("MM月dd日");
    public string FreeElapsedText => FormatDuration(focusSnapshot.Free.Elapsed);
    public string FreeStateText => focusSnapshot.Free.State switch
    {
        FreeFocusState.Running => "自由专注中",
        FreeFocusState.Paused => "已暂停",
        _ => "准备开始"
    };
    public string FreeActionText => focusSnapshot.Free.State switch
    {
        FreeFocusState.Running => "暂停",
        FreeFocusState.Paused => "继续专注",
        _ => "开始专注"
    };
    public string FreeHint => snapshot.State == PomodoroState.Running ? "番茄钟正在运行，请先暂停再开始自由计时。" :
        focusSnapshot.Free.PauseReason == PauseReason.AppRestart ? "已恢复上次进度，准备好后手动继续。" :
        focusSnapshot.Free.PauseReason == PauseReason.SystemSleep ? "系统睡眠后已暂停，准备好再继续。" :
        "不设终点，按自己的节奏投入。";
    public string FreeAccessibleTimer => $"{FreeStateText}，本次 {FreeElapsedText}";

    public HomePageViewModel(FocusController controller, TodoController todo, TimeZoneInfo statisticsTimeZone, Dispatcher dispatcher)
        : base("首页", "把注意力留给眼前的一件事。")
    {
        this.controller = controller;
        this.todo = todo;
        this.statisticsTimeZone = statisticsTimeZone;
        this.dispatcher = dispatcher;
        focusSnapshot = controller.Snapshot;
        RefreshCompletedTasks();
    }

    public string TimerText
    {
        get
        {
            var seconds = (int)Math.Ceiling(snapshot.Remaining.TotalSeconds);
            return seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60:00}:{seconds % 60:00}";
        }
    }

    public string PhaseCaption => snapshot.Phase switch
    {
        PomodoroPhase.Focus => "FOCUS",
        PomodoroPhase.ShortBreak => "SHORT BREAK",
        _ => "LONG BREAK"
    };
    public string PhaseTitle => PhaseName(snapshot.Phase);
    public string CycleText => $"第 {snapshot.CycleNumber} 轮 · {snapshot.StageIndex / 2 + 1} / {snapshot.CycleSettings.FocusesPerCycle}";
    public string StateText => snapshot.State switch
    {
        PomodoroState.Running => snapshot.Phase == PomodoroPhase.Focus ? "专注中" : "休息中",
        PomodoroState.Paused => "已暂停",
        PomodoroState.AwaitingNext => "阶段已结束",
        _ => "准备开始"
    };
    public string PrimaryActionText => snapshot.State switch
    {
        PomodoroState.Running => "暂停",
        PomodoroState.Paused => "继续" + PhaseTitle,
        PomodoroState.AwaitingNext => "进入" + PhaseName(snapshot.NextPhase),
        _ => "开始" + PhaseTitle
    };
    public string HintText => focusSnapshot.Free.State == FreeFocusState.Running
        ? "自由计时正在运行，请先暂停再开始番茄钟。" : snapshot.State switch
    {
        PomodoroState.Paused when snapshot.PauseReason == PauseReason.SystemSleep => "系统睡眠后已暂停，准备好再继续。",
        PomodoroState.Paused when snapshot.PauseReason == PauseReason.AppRestart => "已恢复上次进度，准备好后手动继续。",
        PomodoroState.Paused => "休整一下，已进行的时间仍然保留。",
        PomodoroState.AwaitingNext => "准备好后，进入" + PhaseName(snapshot.NextPhase) + "。",
        _ when snapshot.Phase != PomodoroPhase.Focus => "放松片刻，让注意力慢慢回来。",
        _ => "放下纷扰，专注眼前。"
    };
    public string CompletedThisRun => snapshot.CompletedFocusesThisRun.ToString();
    public string CompletedTasksToday => completedTasksToday.ToString();
    public string ConfigurationHint => snapshot.HasPendingSettings ? "新参数将在下一轮生效" : "完成后" +
        (snapshot.CycleSettings.AutoAdvance ? "进入" : "等待进入") + PhaseName(snapshot.NextPhase);
    public string AccessibleTimer => $"{PhaseTitle}，{StateText}，{TimerText}";
    public string AccessibleTimerState => PhaseTitle + "|" + StateText;

    [RelayCommand(CanExecute = nameof(CanUsePomodoro))]
    private void PrimaryAction()
    {
        var action = snapshot.State switch
        {
            PomodoroState.Ready => PomodoroAction.Start,
            PomodoroState.Running => PomodoroAction.Pause,
            PomodoroState.Paused => PomodoroAction.Resume,
            _ => PomodoroAction.EnterNext
        };
        controller.Execute(action, snapshot.StageId);
    }

    [RelayCommand(CanExecute = nameof(CanSkip))]
    private void Skip() => controller.Execute(PomodoroAction.Skip, snapshot.StageId);
    private bool CanUsePomodoro() => focusSnapshot.PersistenceError is null && focusSnapshot.Free.State != FreeFocusState.Running;
    private bool CanSkip() => CanUsePomodoro() && snapshot.State != PomodoroState.AwaitingNext;

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private void Restart() => controller.Execute(PomodoroAction.Restart, snapshot.StageId);
    private bool CanRestart() => CanUsePomodoro() && snapshot.State is PomodoroState.Running or PomodoroState.Paused;

    [RelayCommand(CanExecute = nameof(CanUseFree))]
    private void FreeAction()
    {
        var action = focusSnapshot.Free.State switch
        {
            FreeFocusState.Running => FreeFocusAction.Pause,
            FreeFocusState.Paused => FreeFocusAction.Resume,
            _ => FreeFocusAction.Start
        };
        controller.ExecuteFree(action, focusSnapshot.Free.SessionId);
    }
    private bool CanUseFree() => focusSnapshot.PersistenceError is null && snapshot.State != PomodoroState.Running;

    [RelayCommand(CanExecute = nameof(CanFinishFree))]
    private void FinishFree() => controller.ExecuteFree(FreeFocusAction.Finish, focusSnapshot.Free.SessionId);
    private bool CanFinishFree() => focusSnapshot.PersistenceError is null && focusSnapshot.Free.State != FreeFocusState.Ready;

    private static string FormatDuration(TimeSpan value) => $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";

    private void OnTimerChanged(object? sender, FocusUpdate update)
    {
        lock (updateGate)
        {
            if (disposed || !active || dispatcher.HasShutdownStarted) return;
            if (pending is null || update.Snapshot.Version > pending.Version) pending = update.Snapshot;
            if (dispatchQueued) return;
            dispatchQueued = true;
            dispatcher.BeginInvoke(ApplyPending, DispatcherPriority.DataBind);
        }
    }

    private void ApplyPending()
    {
        lock (updateGate)
        {
            dispatchQueued = false;
            if (disposed || !active || pending is null || pending.Version <= focusSnapshot.Version) return;
            var previousDate = focusSnapshot.Today.Date;
            var previousBinding = (focusSnapshot.SelectedTask, focusSnapshot.PomodoroTask, focusSnapshot.FreeTask);
            focusSnapshot = pending;
            pending = null;
            if (focusSnapshot.Today.Date != previousDate) RefreshCompletedTasks();
            if (previousBinding != (focusSnapshot.SelectedTask, focusSnapshot.PomodoroTask, focusSnapshot.FreeTask)) RefreshTaskChoices();
        }
        NotifySnapshot();
    }
    private void NotifySnapshot()
    {
        OnPropertyChanged(string.Empty);
        SkipCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        PrimaryActionCommand.NotifyCanExecuteChanged();
        FreeActionCommand.NotifyCanExecuteChanged();
        FinishFreeCommand.NotifyCanExecuteChanged();
    }

    private static string PhaseName(PomodoroPhase phase) => phase switch
    {
        PomodoroPhase.Focus => "专注",
        PomodoroPhase.ShortBreak => "短休息",
        _ => "长休息"
    };

    private void OnTasksChanged(object? sender, TodoSnapshot update)
    {
        if (!active || disposed) return;
        if (dispatcher.CheckAccess()) RefreshCompletedTasks();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(RefreshCompletedTasks, DispatcherPriority.DataBind);
    }

    private void RefreshCompletedTasks()
    {
        if (disposed) return;
        completedTasksToday = todo.CountCompletedOn(focusSnapshot.Today.Date, statisticsTimeZone);
        OnPropertyChanged(nameof(CompletedTasksToday));
        RefreshTaskChoices();
    }

    private void RefreshTaskChoices()
    {
        if (disposed) return;
        rebuildingChoices = true;
        try
        {
            TaskChoices.Clear();
            TaskChoices.Add(new(null, "不绑定任务"));
            foreach (var task in todo.Snapshot.Tasks.Where(task => !task.IsCompleted)) TaskChoices.Add(new(task.Id, task.Title));
            foreach (var reference in new[] { focusSnapshot.PomodoroTask, focusSnapshot.FreeTask }.OfType<FocusTaskReference>())
            {
                if (TaskChoices.Any(choice => choice.Id == reference.Id)) continue;
                var task = todo.Find(reference.Id);
                TaskChoices.Add(new(reference.Id, (task?.Title ?? reference.TitleAtStart) +
                    (task?.IsDeleted == true ? "（已删除）" : "（已完成）")));
            }
            OnPropertyChanged(nameof(SelectedTaskChoice));
            OnPropertyChanged(nameof(CurrentTaskText));
        }
        finally { rebuildingChoices = false; }
    }

    public void Dispose()
    {
        Deactivate();
        lock (updateGate) { disposed = true; pending = null; }
    }
    public void Activate()
    {
        dispatcher.VerifyAccess();
        lock (updateGate)
        {
            if (active || disposed) return;
            active = true;
            controller.Changed += OnTimerChanged;
            todo.Changed += OnTasksChanged;
            focusSnapshot = controller.Snapshot;
            pending = null;
        }
        RefreshCompletedTasks();
        NotifySnapshot();
    }
    public void Deactivate()
    {
        lock (updateGate)
        {
            active = false;
            controller.Changed -= OnTimerChanged;
            todo.Changed -= OnTasksChanged;
            pending = null;
        }
    }
}
