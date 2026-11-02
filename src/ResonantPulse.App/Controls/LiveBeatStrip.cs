using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Controls;

/// <summary>
/// The metronome as a strip: the tempo and the time signature, a light for each beat of the bar (the one that sounds is lit), and
/// what the program is doing. Studio pins it above its scrolling page, so the beat stays in view while a lane or a step is edited.
/// Presentation only: no scheduling, audio or input.
/// </summary>
public sealed class LiveBeatStrip : Grid
{
    private const double BeatSize = 18;
    private const double EmphasizedSize = 26;

    private readonly WrapPanel _layout = new() { HorizontalSpacing = 24, VerticalSpacing = 4 };
    private readonly WrapPanel _lights = new() { HorizontalSpacing = 8, VerticalSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
    private readonly List<Ellipse> _dots = [];
    private readonly TextBlock _tempo = new()
    {
        FontSize = 30, FontFamily = new FontFamily("Bahnschrift"), VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _unit = new() { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _note = new()
    {
        FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed
    };
    private IReadOnlySet<int> _emphasized = new HashSet<int>();
    private int _beats = -1;
    private int[] _groups = [];
    private int _lit = -1;
    private string _value = "";

    internal string AccessibleValue => _value;

    protected override AutomationPeer OnCreateAutomationPeer() => new StripPeer(this);

    private sealed class StripPeer(LiveBeatStrip owner) : FrameworkElementAutomationPeer(owner), IValueProvider
    {
        protected override string GetClassNameCore() => nameof(LiveBeatStrip);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
        protected override string GetNameCore() => Text.Get("LiveMetronome");
        protected override object GetPatternCore(PatternInterface patternInterface) =>
            patternInterface == PatternInterface.Value ? this : base.GetPatternCore(patternInterface);
        public bool IsReadOnly => true;
        public string Value => owner.AccessibleValue;
        public void SetValue(string value) => throw new InvalidOperationException(Text.Get("ReadOnly"));
        // Read on demand only: no beat-rate LiveRegionChanged or Value events.
    }

    public LiveBeatStrip()
    {
        Margin = new Thickness(0, 4, 12, 8);
        MaxWidth = FormMetrics.PageMaxWidth;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        AutomationProperties.SetName(this, Text.Get("LiveMetronome"));
        AutomationProperties.SetAutomationId(this, "StudioMetronome");
        var readout = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
        readout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        readout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        readout.Children.Add(_tempo);
        Grid.SetColumn(_unit, 1);
        readout.Children.Add(_unit);
        _layout.Children.Add(readout);
        _layout.Children.Add(_lights);
        _layout.Children.Add(_note);
        Children.Add(new Border
        {
            Style = (Style)Application.Current.Resources["PageCardStyle"], Padding = new Thickness(16, 8, 16, 8), Child = _layout
        });
        Loaded += (_, _) => RefreshTheme();
        ActualThemeChanged += (_, _) => RefreshTheme();
    }

    private Brush Brush(string key) => ThemeBrushes.Resolve(this, key);

    public void RefreshTheme()
    {
        _tempo.Foreground = Brush("TextBrush");
        _unit.Foreground = Brush("MutedBrush");
        _note.Foreground = Brush("PulseBrush");
        for (var index = 0; index < _dots.Count; index++) Paint(index);
    }

    /// <summary>
    /// Shows one frame of the metronome. Called for every frame of the presentation loop, so it touches the lights only when the beat
    /// or the bar changes, and the texts only when they change.
    /// </summary>
    /// <param name="lit">The beat to light (counted from 0), or -1 for none.</param>
    /// <param name="note">What the program is doing, or empty when there is nothing to say.</param>
    public void Present(int beats, IReadOnlyList<int> groups, int lit, string tempo, string unit, string note, string value)
    {
        if (beats != _beats || !groups.SequenceEqual(_groups)) Rebuild(beats, groups);
        if (lit != _lit)
        {
            var before = _lit;
            _lit = lit;
            Paint(before);
            Paint(lit);
        }
        if (_tempo.Text != tempo) _tempo.Text = tempo;
        if (_unit.Text != unit) _unit.Text = unit;
        if (_note.Text != note)
        {
            _note.Text = note;
            _note.Visibility = note.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            // The wrapping layout only measures the children that are visible: a note that appears or goes is a different row of them.
            _layout.InvalidateMeasure();
        }
        _value = value;
    }

    private void Rebuild(int beats, IReadOnlyList<int> groups)
    {
        _beats = beats;
        _groups = groups.ToArray();
        _emphasized = LiveMetronome.Emphasized(beats, groups);
        _lit = -1;
        _lights.Children.Clear();
        _dots.Clear();
        for (var index = 0; index < beats; index++)
        {
            var size = _emphasized.Contains(index) ? EmphasizedSize : BeatSize;
            var dot = new Ellipse { Width = size, Height = size, StrokeThickness = 2, VerticalAlignment = VerticalAlignment.Center };
            _dots.Add(dot);
            _lights.Children.Add(dot);
            Paint(index);
        }
    }

    /// <summary>A light is a ring while its beat is not sounding and a filled disc while it is, so the two differ by more than colour.</summary>
    private void Paint(int index)
    {
        if (index < 0 || index >= _dots.Count) return;
        var dot = _dots[index];
        if (index == _lit)
        {
            var on = Brush(_emphasized.Contains(index) ? "AccentBrush" : "PulseBrush");
            dot.Fill = on;
            dot.Stroke = on;
            return;
        }
        dot.Fill = null;
        dot.Stroke = Brush("MarkerBrush");
    }
}
