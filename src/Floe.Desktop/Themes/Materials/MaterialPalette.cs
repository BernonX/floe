using System.Windows;
using System.Windows.Media;

namespace Floe.Desktop.Themes.Materials;

internal static class MaterialPalette
{
    public static void AddBrushes(ResourceDictionary palette, bool materialsEnabled)
    {
        // Resolve colors inside this palette, then freeze brushes. Replacing the dictionary invalidates
        // the outer DynamicResource references without relying on Freezable inheritance contexts.
        (string Brush, string Color)[] solids =
        [
            ("WindowBrush", "WindowColor"), ("SurfaceBrush", "SurfaceColor"),
            ("PrimaryTextBrush", "TextColor"), ("SecondaryTextBrush", "MutedTextColor"),
            ("DividerBrush", "BorderColor"), ("AccentBrush", "AccentColor"),
            ("AccentTextBrush", "AccentTextColor"), ("AccentSoftBrush", "AccentSoftColor"),
            ("HoverBrush", "HoverColor"), ("InputBrush", "InputColor"), ("ShellTintBrush", "ShellTintColor")
        ];
        foreach (var (brush, color) in solids)
        {
            // High contrast supplies its own live SystemColors brushes.
            if (!palette.Contains(brush)) AddFrozen(palette, brush, new SolidColorBrush((Color)palette[color]));
        }

        var fill = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.65, 1) };
        fill.GradientStops.Add(new GradientStop((Color)palette["GlassTopColor"], 0));
        fill.GradientStops.Add(new GradientStop((Color)palette["GlassBottomColor"], 1));
        AddFrozen(palette, "GlassFillBrush", fill);

        var edge = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.25, 1) };
        edge.GradientStops.Add(new GradientStop((Color)palette["EdgeHighlightColor"], 0));
        edge.GradientStops.Add(new GradientStop((Color)palette["BorderColor"], 0.6));
        edge.GradientStops.Add(new GradientStop((Color)palette["InnerEdgeColor"], 1));
        AddFrozen(palette, "GlassEdgeBrush", edge);
        var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.8, 1) };
        var highlight = (Color)palette["EdgeHighlightColor"];
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(28, highlight.R, highlight.G, highlight.B), 0));
        sheen.GradientStops.Add(new GradientStop(Colors.Transparent, 0.3));
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(12, highlight.R, highlight.G, highlight.B), 0.48));
        sheen.GradientStops.Add(new GradientStop(Colors.Transparent, 0.65));
        AddFrozen(palette, "GlassSheenBrush", sheen);
        var popup = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.6, 1) };
        var surface = (Color)palette["SurfaceColor"];
        popup.GradientStops.Add(new GradientStop(Color.FromArgb(250, surface.R, surface.G, surface.B), 0));
        popup.GradientStops.Add(new GradientStop(Color.FromArgb(238, surface.R, surface.G, surface.B), 1));
        if (materialsEnabled) AddFrozen(palette, "PopupGlassBrush", popup);
        else palette["PopupGlassBrush"] = palette["SurfaceBrush"];
        if (materialsEnabled)
        {
            var input = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.4, 1) };
            var inputColor = (Color)palette["InputColor"];
            input.GradientStops.Add(new GradientStop(Color.FromArgb(150, inputColor.R, inputColor.G, inputColor.B), 0));
            input.GradientStops.Add(new GradientStop(Color.FromArgb(70, inputColor.R, inputColor.G, inputColor.B), 1));
            AddFrozen(palette, "InputBrush", input);
        }

        foreach (var name in new[] { "A", "B" })
        {
            var ambient = new RadialGradientBrush();
            ambient.GradientStops.Add(new GradientStop((Color)palette[$"Ambient{name}Color"], 0));
            ambient.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            AddFrozen(palette, $"Ambient{name}Brush", ambient);
        }
    }

    private static void AddFrozen(ResourceDictionary palette, string key, Brush brush)
    {
        brush.Freeze();
        palette[key] = brush;
    }
}
