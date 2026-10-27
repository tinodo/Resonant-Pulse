using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using Windows.Foundation;

namespace ResonantPulse.App;

/// <summary>
/// The page kit (docs/design/pages-design.md): a titled page of cards, setting rows, and fields that are as wide as what
/// they hold. It is opt-in, so a page moves to it without changing pages that still use the older form helpers.
/// </summary>
public sealed partial class MainWindow
{
    private enum ButtonKind { Secondary, Primary, Tertiary }

    private static readonly Windows.UI.ViewManagement.UISettings TextSettings = new();

    /// <summary>The Windows text size as a factor (1 is 100%), never below 1.</summary>
    private static double TextScale
    {
        get
        {
            var scale = Math.Max(1, TextSettings.TextScaleFactor);
            AdjustTextScale(ref scale);
            return scale;
        }
    }

    // Lets the local verification build stand in for a larger text size (MainWindow.UiTest.cs); shipped builds have no body.
    static partial void AdjustTextScale(ref double scale);

    // ---- page structure ----------------------------------------------------------------------------------------------

    /// <summary>A scrolling page: title (and an optional line of purpose and page-level actions), then cards.</summary>
    private static ScrollViewer PageScaffold(string titleKey, string? purposeKey, UIElement? actions, params UIElement[] content)
    {
        var title = new TextBlock { Text = Text.Get(titleKey), Style = ResonantStyle("PageTitleStyle") };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(title);
        if (purposeKey is not null)
            heading.Children.Add(new TextBlock { Text = Text.Get(purposeKey), Style = ResonantStyle("NoteStyle") });
        // Stretch with a maximum, not Center: a centred stack is as wide as its content, so a two-pane page that changes its
        // content when its width crosses the breakpoint would change its own width and never settle (a layout cycle). A
        // stretched element with a maximum is centred in a wider slot anyway.
        var stack = new StackPanel { Spacing = 16, MaxWidth = FormMetrics.PageMaxWidth, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(HeaderRow(heading, actions));
        foreach (var item in content) stack.Children.Add(item);
        return new ScrollViewer
        {
            // The right padding keeps the overlay scroll bar off the cards.
            Content = new Grid { Padding = new Thickness(0, 4, 12, 24), Children = { stack } },
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
    }

    private static UIElement HeaderRow(UIElement left, UIElement? right)
    {
        if (right is not FrameworkElement actions) return left;
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(left);
        actions.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);
        return row;
    }

    /// <summary>One idea: a title, at most a sentence of purpose, an optional action at the right, then the content.</summary>
    private static Border Card(string? titleKey, UIElement content, string? descriptionKey = null, UIElement? action = null,
        string? id = null)
    {
        var body = new StackPanel { Spacing = 12 };
        if (titleKey is not null)
        {
            var text = new StackPanel { Spacing = 2 };
            var title = new TextBlock { Text = Text.Get(titleKey), Style = ResonantStyle("CardTitleStyle") };
            AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
            text.Children.Add(title);
            if (descriptionKey is not null)
                text.Children.Add(new TextBlock { Text = Text.Get(descriptionKey), Style = ResonantStyle("NoteStyle") });
            body.Children.Add(HeaderRow(text, action));
        }
        body.Children.Add(content);
        var card = new Border { Style = ResonantStyle("PageCardStyle"), Child = body };
        if (id is not null && titleKey is not null) Identify(card, id, Text.Get(titleKey));
        return card;
    }

    /// <summary>Two cards side by side on a wide page and stacked on a narrow one.</summary>
    private static Grid TwoPane(FrameworkElement primary, FrameworkElement secondary, double secondaryWidth = 0)
    {
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 16 };
        grid.Children.Add(primary);
        grid.Children.Add(secondary);
        var columns = 0;
        void Arrange(double width)
        {
            var wanted = FormMetrics.PageColumns(width);
            if (wanted == columns) return;
            columns = wanted;
            grid.ColumnDefinitions.Clear();
            grid.RowDefinitions.Clear();
            if (columns == 2)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition
                    { Width = secondaryWidth > 0 ? new GridLength(secondaryWidth) : new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(primary, 0);
                Grid.SetColumn(primary, 0);
                Grid.SetRow(secondary, 0);
                Grid.SetColumn(secondary, 1);
                return;
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(primary, 0);
            Grid.SetColumn(primary, 0);
            Grid.SetRow(secondary, 1);
            Grid.SetColumn(secondary, 0);
        }
        Arrange(0);
        grid.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        return grid;
    }

    // ---- setting rows ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Title and description at the left, the control at the right; below a card width of 560 DIP the control moves under
    /// the text. The control is named after the title for screen readers.
    /// </summary>
    private static UIElement SettingRow(string titleKey, string? descriptionKey, FrameworkElement control,
        double sideBySideFrom = FormMetrics.StackedRowBreakpoint)
    {
        var title = new TextBlock { Text = Text.Get(titleKey), Style = ResonantStyle("BodyStyle"), FontSize = 14 };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        if (descriptionKey is not null)
            text.Children.Add(new TextBlock { Text = Text.Get(descriptionKey), Style = ResonantStyle("NoteStyle") });
        var host = Bounded(control);
        host.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetLabeledBy(control, title);
        if (string.IsNullOrEmpty(AutomationProperties.GetName(control))) AutomationProperties.SetName(control, title.Text);

        var row = new Grid { ColumnSpacing = 24, RowSpacing = 8, MinHeight = 44 };
        row.Children.Add(text);
        row.Children.Add(host);
        bool? sideBySide = null;
        void Arrange(double width)
        {
            var side = width >= sideBySideFrom;
            if (sideBySide == side) return;
            sideBySide = side;
            row.ColumnDefinitions.Clear();
            row.RowDefinitions.Clear();
            if (side)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetRow(host, 0);
                Grid.SetColumn(host, 1);
                host.HorizontalAlignment = HorizontalAlignment.Right;
                return;
            }
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(host, 1);
            Grid.SetColumn(host, 0);
            host.HorizontalAlignment = HorizontalAlignment.Left;
        }
        Arrange(0);
        row.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        return row;
    }

    /// <summary>Setting rows separated by a hairline.</summary>
    private static StackPanel SettingRows(params UIElement[] rows)
    {
        var stack = new StackPanel();
        for (var index = 0; index < rows.Length; index++)
        {
            var cell = new Border
            {
                Child = rows[index], Padding = new Thickness(0, 10, 0, 10),
                BorderThickness = new Thickness(0, index == 0 ? 0 : 1, 0, 0)
            };
            if (index > 0) Themed(cell, brush => cell.BorderBrush = brush("OutlineBrush"));
            stack.Children.Add(cell);
        }
        return stack;
    }

    private static void Themed(FrameworkElement element, Action<Func<string, Microsoft.UI.Xaml.Media.Brush>> apply)
    {
        void Refresh() => apply(key => ThemeBrushes.Resolve(element, key));
        element.Loaded += (_, _) => Refresh();
        element.ActualThemeChanged += (_, _) => Refresh();
    }

    // ---- fields: as wide as what they hold ---------------------------------------------------------------------------

    /// <summary>A number box sized to its largest value. The caption comes from <see cref="Field"/>.</summary>
    private static NumberBox NumberInput(string captionKey, string id, double value, double minimum, double maximum,
        double step = 1, int fractionDigits = 0, bool whole = true)
    {
        var box = whole && fractionDigits == 0
            ? NumberInt(captionKey, id, value, minimum, maximum, step)
            : Number(captionKey, id, value, minimum, maximum, step, fractionDigits);
        box.Header = null;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        box.Width = FormMetrics.NumberWidth(minimum, maximum, fractionDigits, TextScale);
        return box;
    }

    /// <summary>A drop-down as wide as its longest option.</summary>
    private static ComboBox Dropdown(string captionKey, string id, string value, IReadOnlyList<Choice> options)
    {
        var combo = SelectChoices(captionKey, id, value, options);
        combo.Header = null;
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        var labels = options.Select(option => option.Label).Append(value);
        combo.Width = FormMetrics.SelectWidth(WidestText(labels));
        return combo;
    }

    /// <summary>A text box that fills its row up to <paramref name="maxWidth"/>.</summary>
    private static TextBox TextInput(string captionKey, string id, string value, double maxWidth = FormMetrics.TextMaxWidth)
    {
        var box = Input(captionKey, id, value);
        box.Header = null;
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
        box.MaxWidth = maxWidth;
        return box;
    }

    private static ToggleSwitch Toggle(string captionKey, string id, bool isOn)
    {
        var toggle = new ToggleSwitch { IsOn = isOn, OnContent = Text.Get("On"), OffContent = Text.Get("Off"), MinWidth = 0 };
        Identify(toggle, id, Text.Get(captionKey));
        return toggle;
    }

    /// <summary>
    /// Hosts a field at the width it was sized for but never wider than the room it is offered, so it cannot be clipped by a
    /// narrow window or a large text size. A number box whose steppers no longer fit switches to compact ones.
    /// </summary>
    private static FrameworkElement Bounded(FrameworkElement control)
    {
        if ((control is NumberBox || control is ComboBox) && double.IsFinite(control.Width))
        {
            var preferred = control.Width;
            control.Width = double.NaN;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            var host = new FitWidth { PreferredWidth = preferred, HorizontalAlignment = HorizontalAlignment.Left };
            host.Children.Add(control);
            if (control is NumberBox box)
                host.SizeChanged += (_, args) => box.SpinButtonPlacementMode = FormMetrics.SteppersInline(args.NewSize.Width, preferred)
                    ? NumberBoxSpinButtonPlacementMode.Inline : NumberBoxSpinButtonPlacementMode.Compact;
            return host;
        }
        if (control.HorizontalAlignment == HorizontalAlignment.Stretch && double.IsFinite(control.MaxWidth))
        {
            // A stretched control with a MaxWidth would be centred in a wider slot. A star column with the same maximum keeps
            // it at the left edge, as wide as the card allows up to that limit.
            var host = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = control.MaxWidth });
            control.MaxWidth = double.PositiveInfinity;
            host.Children.Add(control);
            return host;
        }
        return control;
    }

    /// <summary>
    /// A caption above a field, with an optional unit after it ("seconds"); the unit moves under the field when the row is
    /// too narrow for both. The caption is the control's own name, and the control is linked to it, so a screen reader never
    /// meets an unnamed edit box.
    /// </summary>
    private static StackPanel Field(FrameworkElement control, string? unitKey = null)
    {
        var caption = AutomationProperties.GetName(control);
        var label = new TextBlock { Text = caption, Style = ResonantStyle("FieldCaptionStyle"), Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetLabeledBy(control, label);
        var body = Bounded(control);
        var field = new StackPanel
        {
            HorizontalAlignment = body.HorizontalAlignment == HorizontalAlignment.Stretch
                ? HorizontalAlignment.Stretch : HorizontalAlignment.Left
        };
        field.Children.Add(label);
        if (unitKey is null)
        {
            field.Children.Add(body);
            return field;
        }
        var unit = new TextBlock { Text = Text.Get(unitKey), Style = ResonantStyle("NoteStyle"), VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(control, $"{caption} ({unit.Text})");
        field.Children.Add(Wrapped(8, 4, [body, unit]));
        return field;
    }

    /// <summary>
    /// Number boxes of one width in a column, each followed by its unit: "[ 300 ] seconds". The unit is the label. A box
    /// never gets wider than the room it is offered, and every unit moves under its box together when a row is too narrow.
    /// </summary>
    private static StackPanel UnitRows(string captionKey, params (NumberBox Box, string UnitKey)[] rows)
    {
        var width = rows.Max(row => row.Box.Width);
        var units = rows.Select(row => Text.Get(row.UnitKey)).ToArray();
        // Equal unit widths make every row wrap at the same width, and line the units up in a column when they fit.
        var unitWidth = Math.Ceiling(WidestText(units));
        var stack = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Left };
        for (var index = 0; index < rows.Length; index++)
        {
            var box = rows[index].Box;
            box.Width = width;
            AutomationProperties.SetName(box, $"{Text.Get(captionKey)}: {units[index]}");
            var label = new TextBlock
            {
                Text = units[index], Style = ResonantStyle("BodyStyle"), FontSize = 14, MinWidth = unitWidth,
                VerticalAlignment = VerticalAlignment.Center
            };
            // A unit that wraps sits 2 DIP under its box; the 14 DIP between rows keeps it with the box it belongs to.
            stack.Children.Add(Wrapped(8, 2, [Bounded(box), label]));
        }
        return stack;
    }

    private static double WidestText(IEnumerable<string> texts)
    {
        var probe = new TextBlock { FontSize = 14 };
        var widest = 0d;
        foreach (var text in texts)
        {
            probe.Text = text;
            probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            widest = Math.Max(widest, probe.DesiredSize.Width);
        }
        return widest;
    }

    /// <summary>A short explanation under a field or card title.</summary>
    private static TextBlock Note(string key) => new() { Text = Text.Get(key), Style = ResonantStyle("NoteStyle") };

    /// <summary>
    /// Applies the commit contract to a number box. A NumberBox raises ValueChanged only when the user commits (Enter, focus
    /// leaving, a stepper), so an empty or invalid draft never reaches the project: it reverts to the last committed value.
    /// If the commit itself fails, the box returns to the committed value too, and the error is shown.
    /// Commits run one at a time, in the order they were made: a slow save never lets a later edit overtake it, and a revert
    /// always shows the value of the latest commit that actually succeeded. When a newer edit is already waiting, an older
    /// failure leaves the box alone, because the newer edit decides what it shows.
    /// </summary>
    private void CommitNumber(NumberBox box, Func<double, Task> commit)
    {
        var commits = new OrderedCommits(box.Value);
        var reverting = false;
        void Revert()
        {
            reverting = true;
            box.Value = commits.Committed;
            reverting = false;
        }
        box.ValueChanged += (sender, args) =>
        {
            if (reverting) return;
            _ = commits.Propose(args.NewValue, async value =>
            {
                var applied = false;
                await GuardAsync(async () =>
                {
                    await commit(value);
                    applied = true;
                });
                return applied;
            }, Revert);
        };
    }

    private static WrapPanel Wrapped(double horizontal, double vertical, UIElement[] children)
    {
        var panel = new WrapPanel { HorizontalSpacing = horizontal, VerticalSpacing = vertical };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    /// <summary>Fields side by side at their own width, wrapping when the row is full.</summary>
    private static WrapPanel FieldRow(params UIElement[] children) => Wrapped(16, 12, children);

    /// <summary>Buttons side by side at their own width, wrapping when the row is full.</summary>
    private static WrapPanel CommandRow(params UIElement[] children) => Wrapped(8, 8, children);

    // ---- empty states and key hints ----------------------------------------------------------------------------------

    /// <summary>What this place is for and the next step to take, instead of a blank area.</summary>
    private static StackPanel EmptyState(string glyph, string titleKey, string bodyKey, params UIElement[] actions)
    {
        var stack = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 520 };
        stack.Children.Add(new FontIcon { Glyph = glyph, Style = ResonantStyle("AccentGlyphStyle"), HorizontalAlignment = HorizontalAlignment.Left });
        stack.Children.Add(new TextBlock { Text = Text.Get(titleKey), Style = ResonantStyle("CardTitleStyle") });
        stack.Children.Add(new TextBlock { Text = Text.Get(bodyKey), Style = ResonantStyle("NoteStyle") });
        if (actions.Length > 0) stack.Children.Add(CommandRow(actions));
        return stack;
    }

    /// <summary>A keyboard key drawn as a small keycap.</summary>
    private static Border KeyChip(string text) => new()
    {
        Style = ResonantStyle("KeyChipStyle"),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text, FontSize = 12, Style = ResonantStyle("BodyStyle"), HorizontalAlignment = HorizontalAlignment.Center,
            TextWrapping = TextWrapping.NoWrap
        }
    };
}
