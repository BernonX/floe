using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Floe.Domain.Tasks;
using Floe.Domain.Focus;

namespace Floe.Desktop.Features.Tasks;

public sealed partial class TaskItemViewModel(TodoTask task, TasksPageViewModel owner) : ObservableObject
{
    private TodoTask task = task;
    private TaskFocusStatistics statistics = new(task.Id, 0, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
    public Guid Id => task.Id;
    public string Title => task.Title;
    public bool IsCompleted
    {
        get => task.IsCompleted;
        set { if (value != task.IsCompleted) owner.SetCompleted(Id, value); }
    }
    public DateTimeOffset CreatedAtUtc => task.CreatedAtUtc;
    public string ProgressText => task.EstimatedPomodoros == 0
        ? $"{task.CompletedPomodoros} 番茄 · 未设预计" : $"{task.CompletedPomodoros} / {task.EstimatedPomodoros} 番茄";
    public string FocusTimeText => $"{(long)task.TotalFocusTime.TotalHours:00}:{task.TotalFocusTime.Minutes:00}:{task.TotalFocusTime.Seconds:00}";
    public string PeriodText => $"今日 {Format(statistics.TodayDuration)} · 本周 {Format(statistics.ThisWeekDuration)}";
    private static string Format(TimeSpan value) => $"{(long)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}";
    public string LifecycleText => task.CompletedAtUtc is DateTimeOffset completed
        ? "完成于 " + completed.ToLocalTime().ToString("MM月dd日 HH:mm")
        : "创建于 " + task.CreatedAtUtc.ToLocalTime().ToString("MM月dd日 HH:mm");
    public string CompletionLabel => (IsCompleted ? "恢复待办：" : "完成任务：") + Title;

    public void Apply(TodoTask value)
    {
        if (task == value) return;
        task = value.WithFocusProgress(statistics.CompletedPomodoros, statistics.TotalDuration);
        OnPropertyChanged(string.Empty);
    }

    public void ApplyProgress(TaskFocusStatistics value)
    {
        if (statistics == value) return;
        statistics = value;
        task = task.WithFocusProgress(value.CompletedPomodoros, value.TotalDuration);
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(FocusTimeText));
        OnPropertyChanged(nameof(PeriodText));
    }

    [RelayCommand] private void Edit() => owner.BeginEdit(Id);
    [RelayCommand] private void Delete() => owner.DeleteTask(Id);
}
