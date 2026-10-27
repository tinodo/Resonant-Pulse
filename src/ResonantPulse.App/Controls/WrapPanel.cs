using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ResonantPulse.App.Controls;

/// <summary>
/// Lays children out left to right at their natural width and starts a new row when the next one does not fit. A grid of
/// equal columns stretches every field to its column, so a short field becomes as wide as the window; here it stays short.
/// A row is as tall as its tallest child, and children keep their own vertical alignment inside it.
/// </summary>
public sealed class WrapPanel : Panel
{
    private double _horizontalSpacing = 16;
    private double _verticalSpacing = 12;

    public double HorizontalSpacing
    {
        get => _horizontalSpacing;
        set { _horizontalSpacing = value; InvalidateMeasure(); }
    }

    public double VerticalSpacing
    {
        get => _verticalSpacing;
        set { _verticalSpacing = value; InvalidateMeasure(); }
    }

    private List<UIElement> Present() => Children.Where(child => child.Visibility == Visibility.Visible).ToList();

    protected override Size MeasureOverride(Size availableSize)
    {
        var children = Present();
        var constraint = new Size(availableSize.Width, double.PositiveInfinity);
        foreach (var child in children) child.Measure(constraint);
        var widths = children.Select(child => child.DesiredSize.Width).ToList();
        var starts = WrapRows.Starts(widths, availableSize.Width, HorizontalSpacing, WrapRows.Slack);
        double width = 0, height = 0;
        for (var row = 0; row < starts.Count; row++)
        {
            var end = row + 1 < starts.Count ? starts[row + 1] : children.Count;
            double rowWidth = 0, rowHeight = 0;
            for (var index = starts[row]; index < end; index++)
            {
                rowWidth += widths[index] + (index > starts[row] ? HorizontalSpacing : 0);
                rowHeight = Math.Max(rowHeight, children[index].DesiredSize.Height);
            }
            width = Math.Max(width, rowWidth);
            height += rowHeight + (row > 0 ? VerticalSpacing : 0);
        }
        return new Size(double.IsFinite(availableSize.Width) ? Math.Min(width, availableSize.Width) : width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var children = Present();
        var widths = children.Select(child => child.DesiredSize.Width).ToList();
        var starts = WrapRows.Starts(widths, finalSize.Width, HorizontalSpacing, WrapRows.Slack);
        double y = 0;
        for (var row = 0; row < starts.Count; row++)
        {
            var end = row + 1 < starts.Count ? starts[row + 1] : children.Count;
            var rowHeight = 0d;
            for (var index = starts[row]; index < end; index++)
                rowHeight = Math.Max(rowHeight, children[index].DesiredSize.Height);
            double x = 0;
            for (var index = starts[row]; index < end; index++)
            {
                var width = Math.Min(widths[index], finalSize.Width);
                children[index].Arrange(new Rect(x, y, width, rowHeight));
                x += width + HorizontalSpacing;
            }
            y += rowHeight + VerticalSpacing;
        }
        return new Size(finalSize.Width, Math.Max(0, y - (starts.Count > 0 ? VerticalSpacing : 0)));
    }
}
