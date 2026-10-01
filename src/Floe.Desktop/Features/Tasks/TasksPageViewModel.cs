using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Floe.Application.Tasks;
using Floe.Desktop.Shell;
using Floe.Domain.Tasks;
using Floe.Application.Timing;

namespace Floe.Desktop.Features.Tasks;

public enum TaskFilter { Active, Completed, All }

public sealed partial class TasksPageViewModel : PageViewModel, IDisposable
{
    private readonly TodoController controller;
    private readonly Dispatcher dispatcher;
    private readonly FocusController focus;
    private DateOnly statisticsDate;
    private readonly Dictionary<Guid, TaskItemViewModel> itemById = [];
    private readonly ObservableCollection<TaskItemViewModel> items = [];
    private TodoSnapshot snapshot;
    private Guid? editingId;
    private bool disposed;
    private bool focusVisible;
    [ObservableProperty] private string draftTitle = string.Empty;
    [ObservableProperty] private string draftEstimate = "1";
    [ObservableProperty] private string feedback = string.Empty;
    [ObservableProperty] private TaskFilter filter;

    public TasksPageViewModel(TodoController controller, FocusController focus, Dispatcher dispatcher)
        : base("待办", "为每一段专注，找到清晰的方向。")
    {
        this.controller = controller;
        this.dispatcher = dispatcher;
        this.focus = focus;
        statisticsDate = focus.Snapshot.Today.Date;
        snapshot = controller.Snapshot;
        Tasks = CollectionViewSource.GetDefaultView(items);
        Tasks.Filter = item => Filter == TaskFilter.All || ((TaskItemViewModel)item).IsCompleted == (Filter == TaskFilter.Completed);
        Tasks.SortDescriptions.Add(new(nameof(TaskItemViewModel.IsCompleted), ListSortDirection.Ascending));
        Tasks.SortDescriptions.Add(new(nameof(TaskItemViewModel.CreatedAtUtc), ListSortDirection.Descending));
        foreach (var task in snapshot.Tasks) AddItem(task);
        controller.Changed += OnChanged;
    }

    public ICollectionView Tasks { get; }
    public event EventHandler? EditorFocusRequested;
    public string EditorTitle => editingId.HasValue ? "编辑任务" : "添加一件事";
    public string SaveLabel => editingId.HasValue ? "保存修改" : "添加任务";
    public Visibility CancelVisibility => editingId.HasValue ? Visibility.Visible : Visibility.Collapsed;
    public string SummaryText => $"待办 {snapshot.Tasks.Count(task => !task.IsCompleted)} · 已完成 {snapshot.Tasks.Count(task => task.IsCompleted)}";
    public bool ShowActive { get => Filter == TaskFilter.Active; set { if (value) Filter = TaskFilter.Active; } }
    public bool ShowCompleted { get => Filter == TaskFilter.Completed; set { if (value) Filter = TaskFilter.Completed; } }
    public bool ShowAll { get => Filter == TaskFilter.All; set { if (value) Filter = TaskFilter.All; } }
    public Visibility EmptyVisibility => Tasks.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ListVisibility => Tasks.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
    public string EmptyTitle => Filter == TaskFilter.Completed ? "还没有已完成的任务" : "给下一段专注，一个方向。";
    public string EmptyHint => Filter == TaskFilter.Completed ? "完成的任务会在这里保留，也可以恢复为待办。" : "添加一个小目标，从眼前的一步开始。";
    public string UndoLabel => snapshot.LastDeletedTask is null ? string.Empty : $"已删除「{snapshot.LastDeletedTask.Title}」";
    public Visibility UndoVisibility => snapshot.LastDeletedTask is null ? Visibility.Collapsed : Visibility.Visible;
    public string ValidationMessage => TodoTask.ValidateTitle(DraftTitle) ??
        (TryEstimate(out _) ? string.Empty : "预计番茄请输入 0–99 的整数，0 表示不设预计。");
    public string ValidationText => DraftTitle.Length == 0 ? "预计数量 0 表示不设预计。" : ValidationMessage;

    partial void OnDraftTitleChanged(string value) => DraftChanged();
    partial void OnDraftEstimateChanged(string value) => DraftChanged();
    private void DraftChanged()
    {
        Feedback = string.Empty;
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(ValidationText));
        SaveTaskCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilterChanged(TaskFilter value)
    {
        Tasks.Refresh();
        OnPropertyChanged(nameof(ShowActive));
        OnPropertyChanged(nameof(ShowCompleted));
        OnPropertyChanged(nameof(ShowAll));
        NotifyListState();
    }

    private bool TryEstimate(out int estimate) => int.TryParse(DraftEstimate, NumberStyles.None,
        CultureInfo.InvariantCulture, out estimate) && estimate is >= 0 and <= TodoTask.MaximumEstimatedPomodoros;
    private bool CanSaveTask() => ValidationMessage.Length == 0;

    [RelayCommand(CanExecute = nameof(CanSaveTask))]
    private void SaveTask()
    {
        if (!CanSaveTask() || !TryEstimate(out var estimate)) return;
        var editing = editingId.HasValue;
        if (editingId is Guid id) controller.Edit(id, DraftTitle, estimate);
        else { controller.Add(DraftTitle, estimate); Filter = TaskFilter.Active; }
        ResetEditor();
        Feedback = editing ? "修改已保存。" : "任务已添加。";
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand] private void NewTask()
    {
        ResetEditor();
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand] private void CancelEdit() => ResetEditor();
    [RelayCommand(CanExecute = nameof(CanUndoDelete))] private void UndoDelete()
    {
        var wasCompleted = snapshot.LastDeletedTask?.IsCompleted == true;
        if (!controller.UndoDelete()) return;
        Filter = wasCompleted ? TaskFilter.Completed : TaskFilter.Active;
        Feedback = "删除已撤销。";
    }
    private bool CanUndoDelete() => snapshot.LastDeletedTask is not null;

    public void BeginEdit(Guid id)
    {
        var task = controller.Find(id);
        if (task is null || task.IsDeleted) return;
        editingId = id;
        DraftTitle = task.Title;
        DraftEstimate = task.EstimatedPomodoros.ToString(CultureInfo.InvariantCulture);
        Feedback = string.Empty;
        NotifyEditor();
        EditorFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetCompleted(Guid id, bool completed)
    {
        controller.SetCompleted(id, completed);
        Feedback = completed ? "任务已完成。可在已完成列表中恢复。" : "已恢复为待办。";
    }

    public void DeleteTask(Guid id)
    {
        if (editingId == id) ResetEditor();
        controller.Delete(id);
        Feedback = string.Empty;
    }

    private void ResetEditor()
    {
        editingId = null;
        DraftTitle = string.Empty;
        DraftEstimate = "1";
        Feedback = string.Empty;
        NotifyEditor();
    }

    private void NotifyEditor()
    {
        OnPropertyChanged(nameof(EditorTitle));
        OnPropertyChanged(nameof(SaveLabel));
        OnPropertyChanged(nameof(CancelVisibility));
    }

    private void OnChanged(object? sender, TodoSnapshot update)
    {
        if (dispatcher.CheckAccess()) Apply(update);
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(() => Apply(update), DispatcherPriority.DataBind);
    }

    private void Apply(TodoSnapshot update)
    {
        if (disposed || update.Version <= snapshot.Version) return;
        snapshot = update;
        var byId = snapshot.Tasks.ToDictionary(task => task.Id);
        // Collection changes must remain observable while WPF updates its current item.
        // DeferRefresh is for view configuration, not mutations of this bound sorted collection.
        for (var i = items.Count - 1; i >= 0; i--)
        {
            if (byId.Remove(items[i].Id, out var value))
            {
                items[i].Apply(value);
                items[i].ApplyProgress(focus.GetTaskStatistics(value.Id));
            }
            else { itemById.Remove(items[i].Id); items.RemoveAt(i); }
        }
        foreach (var value in byId.Values) AddItem(value);
        Tasks.Refresh();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(UndoLabel));
        OnPropertyChanged(nameof(UndoVisibility));
        UndoDeleteCommand.NotifyCanExecuteChanged();
        NotifyListState();
    }

    private void NotifyListState()
    {
        OnPropertyChanged(nameof(EmptyVisibility));
        OnPropertyChanged(nameof(ListVisibility));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyHint));
    }

    private void AddItem(TodoTask task)
    {
        var item = new TaskItemViewModel(task, this);
        item.ApplyProgress(focus.GetTaskStatistics(task.Id));
        itemById.Add(task.Id, item);
        items.Add(item);
    }

    private void OnFocusChanged(object? sender, FocusUpdate update)
    {
        void ApplyProgress()
        {
            if (disposed || !focusVisible) return;
            var today = focus.Snapshot.Today.Date;
            // Read current totals even when event delivery is reordered; never lose a different task's update.
            var ids = today != statisticsDate ? itemById.Keys.ToArray() : update.ChangedTaskIds;
            statisticsDate = today;
            foreach (var id in ids)
                if (itemById.TryGetValue(id, out var item)) item.ApplyProgress(focus.GetTaskStatistics(id));
        }
        if (dispatcher.CheckAccess()) ApplyProgress();
        else if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(ApplyProgress, DispatcherPriority.DataBind);
    }

    public void Dispose()
    {
        disposed = true;
        controller.Changed -= OnChanged;
        focus.Changed -= OnFocusChanged;
        EditorFocusRequested = null;
    }
    public void SetFocusVisible(bool visible)
    {
        dispatcher.VerifyAccess();
        if (disposed || focusVisible == visible) return;
        focusVisible = visible;
        if (!visible) { focus.Changed -= OnFocusChanged; return; }
        focus.Changed += OnFocusChanged;
        statisticsDate = focus.Snapshot.Today.Date;
        foreach (var item in items) item.ApplyProgress(focus.GetTaskStatistics(item.Id));
    }
}
