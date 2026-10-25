using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;
using Windows.Foundation;

namespace ResonantPulse.App.Controls;

/// <summary>Finite resolved tempo trajectory against elapsed audio-plan seconds.
/// Positions use Core's actual time/beat ramp inversion, never guessed slopes.</summary>
public sealed class TempoCurve : StackPanel
{
    private readonly Polyline _curve = new() { StrokeThickness = 2 };
    private readonly Line _cursor = new() { Y1 = 8, Y2 = 172, StrokeThickness = 2 };
    private readonly TextBlock _range = new() { TextWrapping = TextWrapping.Wrap };
    private RenderPlan? _plan;

    public TempoCurve()
    {
        Spacing = 8;
        var canvas = new Canvas { Width = 600, Height = 180 };
        canvas.Children.Add(_curve);
        canvas.Children.Add(_cursor);
        Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 220 });
        Children.Add(_range);
        AutomationProperties.SetAutomationId(this, "ResolvedTempoCurve");
        AutomationProperties.SetName(this, Text.Get("TempoChartAxis"));
        ActualThemeChanged += (_, _) => RefreshBrushes();
        Loaded += (_, _) => RefreshBrushes();
    }

    private void RefreshBrushes()
    {
        _curve.Stroke = ThemeBrushes.Resolve(this, "PulseBrush");
        _cursor.Stroke = ThemeBrushes.Resolve(this, "LaneBrush");
        _range.Foreground = ThemeBrushes.Resolve(this, "TextBrush");
    }

    public void Present(RenderPlan? plan, long frame, bool blind)
    {
        Visibility = blind ? Visibility.Collapsed : Visibility.Visible;
        if (blind || plan is null) return;
        if (!ReferenceEquals(_plan, plan))
        {
            _plan = plan;
            var minimum = plan.TempoSegments.Min(segment => Math.Min(segment.StartTempo, segment.EndTempo));
            var maximum = plan.TempoSegments.Max(segment => Math.Max(segment.StartTempo, segment.EndTempo));
            var span = Math.Max(10, maximum - minimum);
            var points = new PointCollection();
            for (var sample = 0; sample <= 256; sample++)
            {
                var at = (long)((double)(plan.TotalFrames - 1) * sample / 256);
                var tempo = plan.PositionAtFrame(at).Tempo;
                points.Add(new Point(8 + 584d * sample / 256, 166 - (tempo - minimum) * 150 / span));
            }
            _curve.Points = points;
            _range.Text = Text.Format("TempoChartRange", minimum, maximum, (double)plan.TotalFrames / plan.SampleRate);
        }
        var x = 8 + 584d * Math.Clamp((double)frame / plan.TotalFrames, 0, 1);
        _cursor.X1 = x;
        _cursor.X2 = x;
    }
}
