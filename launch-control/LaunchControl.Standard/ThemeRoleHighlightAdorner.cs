using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace LaunchControl.Standard;

/// <summary>
/// Bright green outline used by reverse theme-role inspection (theme row → host window).
/// </summary>
public sealed class ThemeRoleHighlightAdorner : Adorner
{
    private static readonly Pen OutlinePen;

    static ThemeRoleHighlightAdorner()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14));
        brush.Freeze();
        OutlinePen = new Pen(brush, 3);
        OutlinePen.Freeze();
    }

    public ThemeRoleHighlightAdorner(UIElement adornedElement)
        : base(adornedElement)
    {
        IsHitTestVisible = false;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = AdornedElement.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
            return;

        drawingContext.DrawRectangle(null, OutlinePen, new Rect(size));
    }
}
