namespace Floe.Domain.Widgets;

/// <summary>Position is stored in physical desktop pixels, including negative coordinates on secondary monitors.</summary>
public sealed record WidgetSettings(bool IsVisible = false, bool IsTopmost = true, bool IsPositionLocked = false,
    double Opacity = 0.92, int? LeftPixels = null, int? TopPixels = null, string LayoutId = "focus", bool IsCompact = false)
{
    public void Validate()
    {
        if (!double.IsFinite(Opacity) || Opacity is < 0.6 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Opacity), "组件透明度应为 60%–100%。");
        if (LeftPixels.HasValue != TopPixels.HasValue)
            throw new ArgumentException("组件位置必须包含两个坐标。");
        if (LeftPixels is < -1_000_000 or > 1_000_000 || TopPixels is < -1_000_000 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(LeftPixels), "组件坐标超出可恢复范围。");
        if (string.IsNullOrWhiteSpace(LayoutId) || LayoutId.Length > 32 ||
            LayoutId.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '-' or '.')))
            throw new ArgumentException("组件布局标识无效。", nameof(LayoutId));
    }
}
