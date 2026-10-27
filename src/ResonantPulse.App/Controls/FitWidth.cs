using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace ResonantPulse.App.Controls;

/// <summary>
/// Gives its content a preferred width (what the field holds) but never more than the room it is offered. A fixed Width
/// is clipped when the window is narrow or the text size is large; a stretched field becomes as wide as the window. This is
/// neither: it is as wide as it needs to be, and gives way when there is not enough room.
/// </summary>
public sealed class FitWidth : Panel
{
    private double _preferredWidth = 120;

    public double PreferredWidth
    {
        get => _preferredWidth;
        set
        {
            if (value == _preferredWidth) return;
            _preferredWidth = value;
            InvalidateMeasure();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = FormMetrics.Fit(PreferredWidth, availableSize.Width);
        var height = 0d;
        foreach (var child in Children)
        {
            child.Measure(new Size(width, availableSize.Height));
            height = Math.Max(height, child.DesiredSize.Height);
        }
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children) child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }
}
