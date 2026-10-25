using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;
using Windows.Foundation;
using Windows.UI.ViewManagement;

namespace ResonantPulse.App.Controls;

/// <summary>Presentation-only retained geometry. No scheduling, audio or input
/// side effects. Native text and accent buttons provide accessible equivalents.</summary>
public sealed class PulseInstrument : Grid
{
    private readonly Canvas _dial = new() { Width = 440, Height = 420 };
    private readonly Canvas _horizon = new() { Width = 440, Height = 76 };
    private readonly Ellipse _ring = new() { Width = 340, Height = 340, StrokeThickness = 2 };
    private readonly Ellipse _inner = new() { Width = 284, Height = 284, StrokeThickness = 1 };
    private readonly Ellipse _phase = new() { Width = 14, Height = 14 };
    private readonly TextBlock _tempo = new()
    {
        Width = 300, FontSize = 78, TextAlignment = TextAlignment.Center,
        FontFamily = new FontFamily("Bahnschrift")
    };
    private readonly TextBlock _unit = new() { Width = 360, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 18 };
    private readonly List<Shape> _ticks = [];
    private readonly List<Shape> _onsets = [];
    private readonly Line _playhead = new() { X1 = 100, X2 = 100, Y1 = 5, Y2 = 70, StrokeThickness = 2 };
    private readonly Line _baseline = new() { X1 = 8, X2 = 432, Y1 = 38, Y2 = 38, StrokeThickness = 1 };
    private int _beatCount;
    private int[] _groups = [];
    private PulseSettings? _settings;
    private bool _isBlind;
    private readonly UISettings _uiSettings = new();
    internal string AccessibleValue => $"{_tempo.Text} · {_unit.Text}";

    protected override AutomationPeer OnCreateAutomationPeer() => new InstrumentPeer(this);

    private sealed class InstrumentPeer(PulseInstrument owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        protected override string GetClassNameCore() => nameof(PulseInstrument);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => Text.Get("PhaseInstrument");
        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPatternCore(patternInterface);
        public bool IsReadOnly => true;
        public string Value => owner.AccessibleValue;
        public void SetValue(string value) => throw new InvalidOperationException(Text.Get("ReadOnly"));
        // Read-on-demand only. No beat-rate LiveRegionChanged/Value events.
    }

    public PulseInstrument()
    {
        AutomationProperties.SetName(this, Text.Get("PhaseInstrument"));
        AutomationProperties.SetAutomationId(this, "PhaseInstrument");
        var column = new StackPanel();
        column.Children.Add(_dial);
        column.Children.Add(_horizon);
        Children.Add(new Viewbox { Child = column, Stretch = Stretch.Uniform, MaxHeight = 500 });
        _dial.Children.Add(_ring);
        Canvas.SetLeft(_ring, 50); Canvas.SetTop(_ring, 30);
        _dial.Children.Add(_inner);
        Canvas.SetLeft(_inner, 78); Canvas.SetTop(_inner, 58);
        _dial.Children.Add(_phase);
        _dial.Children.Add(_tempo); Canvas.SetLeft(_tempo, 70); Canvas.SetTop(_tempo, 142);
        _dial.Children.Add(_unit); Canvas.SetLeft(_unit, 40); Canvas.SetTop(_unit, 246);
        _horizon.Children.Add(_baseline);
        _horizon.Children.Add(_playhead);
        for (var i = 0; i < 128; i++)
        {
            var mark = new Rectangle { Width = 5, Height = 18, RadiusX = 1, RadiusY = 1, Visibility = Visibility.Collapsed };
            _horizon.Children.Add(mark);
            _onsets.Add(mark);
        }
        Loaded += (_, _) => RefreshTheme();
        ActualThemeChanged += (_, _) => RefreshTheme();
    }

    internal Brush Brush(string key) => ThemeBrushes.Resolve(this, key);

    public void RefreshTheme()
    {
        _ring.Stroke = Brush("MarkerBrush");
        _inner.Stroke = Brush("LaneBrush");
        _phase.Fill = Brush("PulseBrush");
        _tempo.Foreground = Brush("TextBrush");
        _unit.Foreground = Brush("PulseBrush");
        _playhead.Stroke = Brush("TextBrush");
        _baseline.Stroke = Brush("MarkerBrush");
        if (_settings is not null) DrawTicks(_settings, true);
    }

    private void DrawTicks(PulseSettings settings, bool force = false)
    {
        if (!force && _beatCount == settings.BeatsPerBar && _groups.SequenceEqual(settings.Groups)) return;
        foreach (var tick in _ticks) _dial.Children.Remove(tick);
        _ticks.Clear();
        _beatCount = settings.BeatsPerBar;
        _groups = settings.Groups.ToArray();
        var boundaries = new HashSet<int> { 0 };
        var total = 0;
        foreach (var group in settings.Groups) { boundaries.Add(total); total += group; }
        for (var beat = 0; beat < _beatCount; beat++)
        {
            var angle = 2 * Math.PI * beat / _beatCount - Math.PI / 2;
            var strong = boundaries.Contains(beat);
            var start = strong ? 178 : 181;
            var line = new Line
            {
                X1 = 220 + Math.Cos(angle) * start, Y1 = 200 + Math.Sin(angle) * start,
                X2 = 220 + Math.Cos(angle) * 191, Y2 = 200 + Math.Sin(angle) * 191,
                StrokeThickness = strong ? 4 : 2,
                Stroke = Brush(strong ? "AccentBrush" : "MarkerBrush")
            };
            _dial.Children.Add(line);
            _ticks.Add(line);
        }
    }

    public void Present(PulseSettings settings, double tempo, double barPhase,
        RenderPlan? plan, long localFrame, bool blind, bool reducedMotion)
    {
        _settings = settings;
        DrawTicks(settings);
        _isBlind = blind;
        _tempo.Text = blind ? Text.Get("Silent") : tempo.ToString("0.0", Text.Culture);
        _unit.Text = blind ? "" : $"BPM · {NoteUnit(settings.BeatUnitQuarters)}";
        _phase.Visibility = blind ? Visibility.Collapsed : Visibility.Visible;
        _horizon.Visibility = blind ? Visibility.Collapsed : Visibility.Visible;
        _ring.Visibility = blind ? Visibility.Collapsed : Visibility.Visible;
        _inner.Visibility = blind ? Visibility.Collapsed : Visibility.Visible;
        foreach (var tick in _ticks) tick.Visibility = blind ? Visibility.Collapsed : Visibility.Visible;
        if (blind) return;
        var reduced = reducedMotion || !_uiSettings.AnimationsEnabled;
        // Reduced motion quantizes the small marker to a beat, not a flashing area.
        var phase = reduced ? Math.Floor(barPhase * settings.BeatsPerBar) / settings.BeatsPerBar : barPhase;
        var angle = phase * 2 * Math.PI - Math.PI / 2;
        Canvas.SetLeft(_phase, 213 + 170 * Math.Cos(angle));
        Canvas.SetTop(_phase, 193 + 170 * Math.Sin(angle));
        foreach (var mark in _onsets) mark.Visibility = Visibility.Collapsed;
        if (plan is null || reduced) return;
        var start = localFrame - (long)(plan.SampleRate * 0.75);
        var end = localFrame + (long)(plan.SampleRate * 2.5);
        var lo = 0; var hi = plan.Onsets.Count;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (plan.Onsets[mid].Frame < start) lo = mid + 1; else hi = mid;
        }
        for (var index = 0; lo < plan.Onsets.Count && index < _onsets.Count; lo++, index++)
        {
            var onset = plan.Onsets[lo];
            if (onset.Frame >= end) break;
            var mark = _onsets[index];
            mark.Visibility = Visibility.Visible;
            mark.Height = onset.Accent == Accent.Strong ? 28 : onset.LaneIndex == -2 ? 8 : 18;
            mark.Width = onset.LaneIndex >= 0 ? 8 : onset.LaneIndex == -2 ? 4 : 6;
            mark.Fill = Brush(onset.Accent == Accent.Strong ? "AccentBrush" : onset.LaneIndex >= 0 ? "LaneBrush" : "PulseBrush");
            Canvas.SetLeft(mark, 100 + (double)(onset.Frame - localFrame) / plan.SampleRate * 126);
            Canvas.SetTop(mark, 38 - mark.Height / 2);
        }
    }

    public static string NoteUnit(double unit) => unit switch
    {
        0.5 => Text.Get("Eighth"), 1 => Text.Get("Quarter"), 1.5 => Text.Get("DottedQuarter"),
        2 => Text.Get("Half"), 3 => Text.Get("DottedHalf"),
        _ => $"{unit.ToString("0.###", Text.Culture)} × {Text.Get("Quarter")}"
    };
}
