using Floe.Domain.Tasks;

namespace Floe.Domain.Tests;

public sealed class TodoTaskTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreationTrimsTheTitleAndStartsWithNoFocusProgress()
    {
        var task = TodoTask.Create("  数据结构复习  ", 5, Created);
        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal("数据结构复习", task.Title);
        Assert.Equal(Created, task.CreatedAtUtc);
        Assert.Equal(Created, task.UpdatedAtUtc);
        Assert.False(task.IsCompleted);
        Assert.Equal(0, task.CompletedPomodoros);
        Assert.Equal(TimeSpan.Zero, task.TotalFocusTime);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t ")]
    [InlineData("第一行\n第二行")]
    [InlineData("任务\t标题")]
    public void EmptyOrMultilineTitlesAreRejected(string title) => Assert.Throws<ArgumentException>(() => TodoTask.Create(title, 1, Created));

    [Fact]
    public void TitleLengthBoundaryIsValidatedAfterTrimming()
    {
        Assert.Equal(120, TodoTask.Create("  " + new string('学', 120) + "  ", 1, Created).Title.Length);
        Assert.Throws<ArgumentException>(() => TodoTask.Create(new string('学', 121), 1, Created));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public void InvalidEstimatesAreRejected(int estimate) => Assert.Throws<ArgumentOutOfRangeException>(() => TodoTask.Create("复习", estimate, Created));

    [Fact]
    public void EditPreservesIdentityCreationAndCompletion()
    {
        var completedAt = Created.AddHours(1);
        var original = TodoTask.Create("阅读", 2, Created).SetCompleted(true, completedAt);
        var edited = original.Edit("阅读第十章", 0, Created.AddHours(2));
        Assert.Equal(original.Id, edited.Id);
        Assert.Equal(Created, edited.CreatedAtUtc);
        Assert.Equal(completedAt, edited.CompletedAtUtc);
        Assert.Equal(0, edited.EstimatedPomodoros);
        Assert.Equal("阅读", original.Title);
    }

    [Fact]
    public void RepeatedCompletionIsIdempotentAndRestoreClearsCompletionTime()
    {
        var original = TodoTask.Create("阅读", 99, Created);
        var completed = original.SetCompleted(true, Created.AddMinutes(10));
        Assert.Same(completed, completed.SetCompleted(true, Created.AddMinutes(20)));
        var restored = completed.SetCompleted(false, Created.AddMinutes(30));
        Assert.Null(restored.CompletedAtUtc);
        Assert.False(restored.IsCompleted);
        Assert.Equal(Created, restored.CreatedAtUtc);
        Assert.Equal(Created.AddMinutes(30), restored.UpdatedAtUtc);
    }

    [Fact]
    public void SoftDeletionIsReversibleAndProtectsTheHiddenTask()
    {
        var original = TodoTask.Create("阅读", 3, Created).SetCompleted(true, Created.AddMinutes(10));
        var deleted = original.Delete(Created.AddMinutes(20));
        Assert.True(deleted.IsDeleted);
        Assert.Throws<InvalidOperationException>(() => deleted.Edit("新标题", 4, Created));
        Assert.Throws<InvalidOperationException>(() => deleted.SetCompleted(false, Created));
        var restored = deleted.UndoDelete(Created.AddMinutes(30));
        Assert.False(restored.IsDeleted);
        Assert.Equal(original.CompletedAtUtc, restored.CompletedAtUtc);
        Assert.Equal(original.Id, restored.Id);
    }
}
