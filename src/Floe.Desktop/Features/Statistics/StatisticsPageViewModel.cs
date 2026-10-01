using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Floe.Application.Statistics;
using Floe.Application.Tasks;
using Floe.Application.Timing;
using Floe.Desktop.Shell;
using Floe.Domain.Focus;

namespace Floe.Desktop.Features.Statistics;

public sealed partial class StatisticsPageViewModel : PageViewModel, IDisposable
{
    private readonly StatisticsService service;
    private readonly FocusController focus;
    private readonly TodoController todo;
    private readonly Dispatcher dispatcher;
    private bool active;
    private bool disposed;
    private int pending;
    private StatisticsSnapshot? snapshot;
    [ObservableProperty] private int dayCount = 7;
    [ObservableProperty] private StatisticsDayViewModel? selectedDay;

    public StatisticsPageViewModel(StatisticsService service, FocusController focus, TodoController todo, Dispatcher dispatcher)
        : base("统计", "看见时间的积累，也看见自己的节奏。")
    {
        this.service = service;
        this.focus = focus;
        this.todo = todo;
        this.dispatcher = dispatcher;
        Refresh();
    }

    public ObservableCollection<StatisticsDayViewModel> Days { get; } = [];
    public bool ShowWeek { get => DayCount == 7; set { if (value) DayCount = 7; } }
    public bool ShowMonth { get => DayCount == 30; set { if (value) DayCount = 30; } }
    public string TodayDuration => Format(snapshot?.Focus.Days[^1].TotalDuration ?? TimeSpan.Zero);
    public string TodayPomodoros => (snapshot?.Focus.TodayCompletedPomodoros ?? 0).ToString(CultureInfo.InvariantCulture);
    public string TodayTasks => (snapshot?.TodayCompletedTasks ?? 0).ToString(CultureInfo.InvariantCulture);
    public string PeriodTotal => Format(snapshot?.Focus.TotalDuration ?? TimeSpan.Zero);
    public string DailyAverage => Format(snapshot?.Focus.DailyAverage ?? TimeSpan.Zero);
    public string RangeText => snapshot is null ? string.Empty : $"{snapshot.Focus.Days[0].Date:yyyy/MM/dd} — {snapshot.Focus.Today:MM/dd} · 含今天";
    public string AxisStart => ShowMonth ? Days[0].Record.Date.ToString("M/d", CultureInfo.InvariantCulture) : string.Empty;
    public string AxisEnd => ShowMonth ? Days[^1].Record.Date.ToString("M/d", CultureInfo.InvariantCulture) : string.Empty;
    public string ScaleText => $"最高 {Format(Days.Count == 0 ? TimeSpan.Zero : Days.Max(day => day.Record.TotalDuration))}";
    public string EmptyText => snapshot?.Focus.TotalDuration > TimeSpan.Zero ? string.Empty : "还没有专注记录，从下一段投入开始。";
    public string SelectedDetail => SelectedDay?.Detail ?? string.Empty;
    public string ScopeText => $"番茄钟与自由计时合并统计 · 日均包含未专注的日期\n完成任务不含已删除或恢复待办的任务 · 时区：{snapshot?.Focus.TimeZoneId}";

    partial void OnDayCountChanged(int value)
    {
        OnPropertyChanged(nameof(ShowWeek));
        OnPropertyChanged(nameof(ShowMonth));
        Refresh();
    }
    partial void OnSelectedDayChanged(StatisticsDayViewModel? value) => OnPropertyChanged(nameof(SelectedDetail));

    public void Activate()
    {
        if (active || disposed) return;
        active = true;
        focus.Changed += OnFocusChanged;
        todo.Changed += OnTodoChanged;
        Refresh();
    }
    public void Deactivate()
    {
        active = false;
        focus.Changed -= OnFocusChanged;
        todo.Changed -= OnTodoChanged;
    }
    private void OnFocusChanged(object? sender, FocusUpdate e) => QueueRefresh();
    private void OnTodoChanged(object? sender, TodoSnapshot e) => QueueRefresh();
    private void QueueRefresh()
    {
        if (Interlocked.Exchange(ref pending, 1) != 0) return;
        dispatcher.BeginInvoke(() =>
        {
            Interlocked.Exchange(ref pending, 0);
            if (active && !disposed) Refresh();
        }, DispatcherPriority.DataBind);
    }
    private void Refresh()
    {
        snapshot = service.GetSnapshot(DayCount);
        var records = snapshot.Focus.Days;
        if (Days.Count != records.Count || Days[0].Record.Date != records[0].Date)
        {
            var selectedDate = SelectedDay?.Record.Date ?? snapshot.Focus.Today;
            Days.Clear();
            for (var i = 0; i < records.Count; i++) Days.Add(new(records[i], DayCount == 7
                ? records[i].Date.ToString("ddd", CultureInfo.GetCultureInfo("zh-CN"))
                : string.Empty));
            SelectedDay = Days.FirstOrDefault(day => day.Record.Date == selectedDate) ?? Days[^1];
        }
        var maximum = records.Max(day => day.TotalDuration.Ticks);
        for (var i = 0; i < records.Count; i++) Days[i].Apply(records[i], maximum);
        OnPropertyChanged(string.Empty);
    }
    internal static string Format(TimeSpan duration)
    {
        var seconds = (long)duration.TotalSeconds;
        return $"{seconds / 3600:00}:{seconds / 60 % 60:00}:{seconds % 60:00}";
    }
    public void Dispose() { disposed = true; Deactivate(); }
}

public sealed class StatisticsDayViewModel(DailyFocusRecord record, string label) : ObservableObject
{
    public DailyFocusRecord Record { get; private set; } = record;
    public string Label { get; } = label;
    public double BarHeight { get; private set; }
    public bool HasActivity => Record.TotalDuration > TimeSpan.Zero;
    public string Detail => $"{Record.Date:yyyy/MM/dd}   {StatisticsPageViewModel.Format(Record.TotalDuration)}   ·   番茄 {StatisticsPageViewModel.Format(Record.PomodoroDuration)}   自由 {StatisticsPageViewModel.Format(Record.FreeDuration)}";
    public void Apply(DailyFocusRecord value, long maximum)
    {
        var height = value.TotalDuration == TimeSpan.Zero ? 1 : Math.Max(3, 150d * value.TotalDuration.Ticks / maximum);
        if (Record == value && BarHeight == height) return;
        Record = value;
        BarHeight = height;
        OnPropertyChanged(string.Empty);
    }
}
