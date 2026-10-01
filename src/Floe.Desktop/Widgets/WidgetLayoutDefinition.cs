using System.Windows;

namespace Floe.Desktop.Widgets;

/// <summary>Layout registration owns view and dimensions; the window host has no focus-specific layout code.</summary>
public sealed record WidgetLayoutDefinition(string Id, double Width, double Height, Func<FrameworkElement> CreateView, double? CompactHeight = null);

internal static class WidgetLayouts
{
    private static readonly IReadOnlyDictionary<string, WidgetLayoutDefinition> definitions =
        new Dictionary<string, WidgetLayoutDefinition> { ["focus"] = new("focus", 240, 156, () => new FocusWidgetView(), CompactHeight: 140) };
    public static WidgetLayoutDefinition Resolve(string id) => definitions.GetValueOrDefault(id) ?? definitions["focus"];
}
