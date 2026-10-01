using Floe.Application.Storage;
using Floe.Application.Widgets;
using Floe.Domain.Widgets;

namespace Floe.Application.Tests;

public sealed class WidgetTests
{
    [Fact]
    public void FailedSaveDoesNotPublishWidgetStateAndRetryPreservesOtherFields()
    {
        using var store = new TestAppStore();
        var initial = new WidgetSettings(LeftPixels: -800, TopPixels: 120);
        var controller = new WidgetController(store, initial);
        var changes = 0;
        controller.Changed += (_, _) => changes++;
        store.FailWrites = true;
        Assert.Throws<DataStoreException>(() => controller.SetVisible(true));
        Assert.Equal(initial, controller.Settings);
        Assert.Equal(0, changes);
        store.FailWrites = false;
        controller.SetVisible(true);
        controller.SetTopmost(false);
        controller.SetLocked(true);
        controller.SetOpacity(0.75);
        Assert.Equal(new WidgetSettings(true, false, true, 0.75, -800, 120), store.Load().WidgetSettings);
        var restored = new WidgetController(store, store.Load().WidgetSettings);
        restored.ResetPosition();
        Assert.True(restored.Settings.IsVisible);
        Assert.True(restored.Settings.IsPositionLocked);
        Assert.Null(restored.Settings.LeftPixels);
        Assert.Null(restored.Settings.TopPixels);
        Assert.Equal(0.75, restored.Settings.Opacity);
    }

    [Fact]
    public void RepeatedSettingsAreNoOpsAndAppearanceCannotOverwriteWidgetState()
    {
        using var store = new TestAppStore();
        var controller = new WidgetController(store);
        var changes = 0;
        controller.Changed += (_, _) => changes++;
        controller.SetVisible(false);
        Assert.Equal(0, changes);
        controller.SetVisible(true);
        store.SaveAppSettings(new(AppTheme.Dark, true, true));
        Assert.True(store.Load().WidgetSettings!.IsVisible);
        controller.SavePosition(-100, -200);
        controller.SavePosition(-100, -200);
        Assert.Equal(2, changes);
        Assert.Equal(AppTheme.Dark, store.Load().AppSettings.Theme);
    }

    [Theory]
    [InlineData(-2000, 0, -1920, 0)]
    [InlineData(-100, 1200, -324, 732)]
    [InlineData(-800, 150, -800, 150)]
    public void FitsNegativeCoordinateMonitorAndExcludesTaskbar(int left, int top, int expectedLeft, int expectedTop)
    {
        Assert.Equal(new WidgetPosition(expectedLeft, expectedTop),
            WidgetPlacement.Fit(new(left, top), 324, 348, new(-1920, 0, 0, 1080)));
    }

    [Fact]
    public void OversizedWidgetStillKeepsItsDragStripAccessible()
    {
        Assert.Equal(new WidgetPosition(10, 20), WidgetPlacement.Fit(new(200, 300), 324, 348, new(10, 20, 110, 120)));
        Assert.Throws<ArgumentOutOfRangeException>(() => WidgetPlacement.Fit(new(), 0, 348, new(0, 0, 100, 100)));
    }

    [Theory]
    [InlineData(0.59)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidOpacityCannotReachStore(double opacity)
    {
        using var store = new TestAppStore();
        var controller = new WidgetController(store);
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.SetOpacity(opacity));
        Assert.Equal(0.92, controller.Settings.Opacity);
    }

    [Fact]
    public void IncompletePositionAndInvalidLayoutAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new WidgetController(restored: new(LeftPixels: 10)));
        Assert.Throws<ArgumentException>(() => new WidgetSettings(LayoutId: " ").Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new WidgetSettings(LeftPixels: int.MaxValue, TopPixels: 0).Validate());
        new WidgetSettings(LeftPixels: -1920, TopPixels: -1080, LayoutId: "future-layout.2").Validate();
    }
}
