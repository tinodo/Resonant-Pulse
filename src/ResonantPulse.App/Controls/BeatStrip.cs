using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ResonantPulse.Core;
using Windows.System;

namespace ResonantPulse.App.Controls;

/// <summary>
/// One labelled bar per click in the bar: height and colour show strength, a tap cycles it. The
/// bar that is sounding is softly lit. Presentation only; the owner applies edits.
/// </summary>
public sealed class BeatStrip : StackPanel
{
    private const double BarZone = 36;
    private readonly List<Cell> _cells = [];
    private int _highlight = -1;

    private sealed class Cell(Button button, Grid layout, Rectangle bar, TextBlock number, Border glow)
    {
        public Button Button { get; } = button;
        public Grid Layout { get; } = layout;
        public Rectangle Bar { get; } = bar;
        public TextBlock Number { get; } = number;
        public Border Glow { get; } = glow;
        public Accent Level { get; set; }
    }

    /// <summary>Localized accessible name for a beat; supplied by the owner.</summary>
    public Func<int, Accent, string> Describe { get; set; } = (index, level) => $"{index + 1}: {level}";

    /// <summary>The number shown under a cell; supplied by the owner when it is not simply the cell's place in the strip.</summary>
    public Func<int, string> Label { get; set; } = index => (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>
    /// True for a click that splits a beat: its bar is narrower and its number smaller than a beat's, so the strip reads as beats
    /// with clicks between them. The number keeps the full text colour: fading it would drop it below the contrast the theme's
    /// text needs, in Light and in the Contrast themes.
    /// </summary>
    public Func<int, bool> IsSplit { get; set; } = _ => false;

    /// <summary>The automation id of a beat is this prefix and its number, counted from 0.</summary>
    public string AutomationPrefix { get; set; } = "MainAccent";

    /// <summary>
    /// Makes the whole strip one tab stop: Tab enters it once, and Left, Right, Home and End move between beats. Without it
    /// a strip of 64 beats takes 64 presses of Tab to get past.
    /// </summary>
    public bool RovingFocus { get; set; }

    public event EventHandler<int>? BeatInvoked;
    public event EventHandler<(int Index, int Delta)>? BeatNudged;

    public int Count => _cells.Count;

    /// <summary>How many extra gaps the last <see cref="Rebuild"/> put between additive groups.</summary>
    public int GroupGapCount { get; private set; }

    public BeatStrip()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Center;
        Loaded += (_, _) => RefreshTheme();
        ActualThemeChanged += (_, _) => RefreshTheme();
    }

    private int _tabStop;

    private void SetTabStop(int index)
    {
        _tabStop = index;
        for (var cell = 0; cell < _cells.Count; cell++) _cells[cell].Button.IsTabStop = !RovingFocus || cell == index;
    }

    private void MoveFocus(int target)
    {
        if (_cells.Count == 0) return;
        target = Math.Clamp(target, 0, _cells.Count - 1);
        SetTabStop(target);
        _cells[target].Button.Focus(FocusState.Keyboard);
    }

    /// <param name="groupStarts">Cell indices that get a little extra space before them: where an additive group, or a split beat, begins.</param>
    public void Rebuild(int count, IReadOnlySet<int> groupStarts, double cellWidth, IReadOnlyList<Accent> levels)
    {
        Children.Clear();
        _cells.Clear();
        _highlight = -1;
        GroupGapCount = 0;
        for (var index = 0; index < count; index++)
        {
            var split = IsSplit(index);
            var bar = new Rectangle
            {
                Width = split ? 10 : 14, RadiusX = 3, RadiusY = 3,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom
            };
            var number = new TextBlock
            {
                Text = Label(index), FontSize = split ? 11 : 12,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var glow = new Border
            {
                CornerRadius = new CornerRadius(8), Opacity = 0.18, Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            Grid.SetRowSpan(glow, 2);
            var layout = new Grid { Width = cellWidth, Height = BarZone + 24 };
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(BarZone + 4) });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(20) });
            layout.Children.Add(glow);
            layout.Children.Add(bar);
            Grid.SetRow(number, 1);
            layout.Children.Add(number);
            var startsGroup = groupStarts.Contains(index) && index > 0;
            if (startsGroup) GroupGapCount++;
            var button = new Button
            {
                Content = layout, Padding = new Thickness(0), MinWidth = 0, MinHeight = 0,
                Width = cellWidth, Height = BarZone + 24, BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(startsGroup ? PlayLayout.StripGroupGap : 0, 0, 0, 0)
            };
            var captured = index;
            button.Click += (_, _) => BeatInvoked?.Invoke(this, captured);
            button.GotFocus += (_, _) =>
            {
                if (RovingFocus && _tabStop != captured) SetTabStop(captured);
            };
            button.KeyDown += (_, args) =>
            {
                switch (args.Key)
                {
                    case VirtualKey.Up or VirtualKey.Down:
                        BeatNudged?.Invoke(this, (captured, args.Key == VirtualKey.Up ? 1 : -1));
                        break;
                    case VirtualKey.Left when RovingFocus:
                        MoveFocus(captured - 1);
                        break;
                    case VirtualKey.Right when RovingFocus:
                        MoveFocus(captured + 1);
                        break;
                    case VirtualKey.Home when RovingFocus:
                        MoveFocus(0);
                        break;
                    case VirtualKey.End when RovingFocus:
                        MoveFocus(_cells.Count - 1);
                        break;
                    default:
                        return;
                }
                args.Handled = true;
            };
            AutomationProperties.SetAutomationId(button, $"{AutomationPrefix}{index}");
            var cell = new Cell(button, layout, bar, number, glow);
            _cells.Add(cell);
            Children.Add(button);
        }
        SetTabStop(Math.Min(_tabStop, Math.Max(0, count - 1)));
        SetLevels(levels);
    }

    public void SetCellWidth(double cellWidth)
    {
        foreach (var cell in _cells) cell.Button.Width = cell.Layout.Width = cellWidth;
    }

    public void SetLevels(IReadOnlyList<Accent> levels)
    {
        for (var index = 0; index < _cells.Count; index++)
            _cells[index].Level = index < levels.Count ? levels[index] : Accent.Normal;
        RefreshTheme();
    }

    public void Highlight(int index)
    {
        if (index == _highlight) return;
        if (_highlight >= 0 && _highlight < _cells.Count) _cells[_highlight].Glow.Visibility = Visibility.Collapsed;
        _highlight = index;
        if (index >= 0 && index < _cells.Count) _cells[index].Glow.Visibility = Visibility.Visible;
    }

    public void RefreshTheme()
    {
        var pulse = ThemeBrushes.Resolve(this, "PulseBrush");
        var accent = ThemeBrushes.Resolve(this, "AccentBrush");
        var muted = ThemeBrushes.Resolve(this, "MutedBrush");
        var marker = ThemeBrushes.Resolve(this, "MarkerBrush");
        for (var index = 0; index < _cells.Count; index++)
        {
            var cell = _cells[index];
            cell.Number.Foreground = muted;
            cell.Glow.Background = pulse;
            cell.Bar.Opacity = 1;
            cell.Bar.Stroke = null;
            cell.Bar.StrokeThickness = 0;
            switch (cell.Level)
            {
                case Accent.Strong:
                    cell.Bar.Height = BarZone; cell.Bar.Fill = accent;
                    break;
                case Accent.Normal:
                    cell.Bar.Height = 24; cell.Bar.Fill = pulse;
                    break;
                case Accent.Soft:
                    cell.Bar.Height = 12; cell.Bar.Fill = pulse; cell.Bar.Opacity = 0.55;
                    break;
                default:
                    cell.Bar.Height = 6; cell.Bar.Fill = null; cell.Bar.Stroke = marker; cell.Bar.StrokeThickness = 1.5;
                    break;
            }
            var name = Describe(index, cell.Level);
            AutomationProperties.SetName(cell.Button, name);
            ToolTipService.SetToolTip(cell.Button, name);
        }
    }
}
