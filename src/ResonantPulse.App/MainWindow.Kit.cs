using System.Runtime.CompilerServices;
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

    /// <summary>
    /// Raised on the UI thread when the Windows text size changes. A field sized from the text size listens only while it is on
    /// screen, so a page that is gone never keeps itself alive through this event.
    /// </summary>
    private static event Action? TextScaleChanged;

    private static void RaiseTextScaleChanged() => TextScaleChanged?.Invoke();

    /// <summary>
    /// How a field's preferred width follows the text size (a function of the scale), kept so the width can be worked out
    /// again if the size changes while the page is open. A field without a rule keeps the width it was given.
    /// </summary>
    private static readonly ConditionalWeakTable<FrameworkElement, Func<double, double>> WidthRules = new();

    private static Func<double, double> WidthRuleOf(FrameworkElement control)
    {
        if (WidthRules.TryGetValue(control, out var rule)) return rule;
        var width = control.Width;
        return _ => width;
    }

    // ---- page structure ----------------------------------------------------------------------------------------------

    /// <summary>A scrolling page: title (and an optional line of purpose and page-level actions), then cards.</summary>
    private static ScrollViewer PageScaffold(string titleKey, string? purposeKey, UIElement? actions, params UIElement[] content) =>
        PageScaffoldText(titleKey, purposeKey is null ? null : Text.Get(purposeKey), actions, content);

    /// <summary>The same page when the line of purpose is worked out (it names the project, for instance).</summary>
    private static ScrollViewer PageScaffoldText(string titleKey, string? purpose, UIElement? actions, params UIElement[] content)
    {
        var title = new TextBlock { Text = Text.Get(titleKey), Style = ResonantStyle("PageTitleStyle") };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(title);
        if (purpose is not null)
            heading.Children.Add(new TextBlock { Text = purpose, Style = ResonantStyle("NoteStyle") });
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

    /// <summary>
    /// A title block with actions at its right. When both do not fit on one line the actions move under the title, so
    /// neither is squeezed or clipped in a narrow window.
    /// </summary>
    private static UIElement HeaderRow(UIElement left, UIElement? right)
    {
        if (right is not FrameworkElement actions) return left;
        var row = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        row.Children.Add(left);
        row.Children.Add(actions);
        actions.VerticalAlignment = VerticalAlignment.Top;
        bool? sideBySide = null;
        var natural = 0d;
        void Arrange()
        {
            var side = row.ActualWidth <= 0 || natural <= row.ActualWidth;
            if (sideBySide == side) return;
            sideBySide = side;
            row.ColumnDefinitions.Clear();
            row.RowDefinitions.Clear();
            if (side)
            {
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetRow((FrameworkElement)left, 0);
                Grid.SetColumn((FrameworkElement)left, 0);
                Grid.SetRow(actions, 0);
                Grid.SetColumn(actions, 1);
                actions.HorizontalAlignment = HorizontalAlignment.Right;
                return;
            }
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow((FrameworkElement)left, 0);
            Grid.SetColumn((FrameworkElement)left, 0);
            Grid.SetRow(actions, 1);
            Grid.SetColumn(actions, 0);
            actions.HorizontalAlignment = HorizontalAlignment.Left;
        }
        void Measure()
        {
            var unbounded = new Size(double.PositiveInfinity, double.PositiveInfinity);
            left.Measure(unbounded);
            actions.Measure(unbounded);
            natural = left.DesiredSize.Width + row.ColumnSpacing + actions.DesiredSize.Width;
            sideBySide = null;
            Arrange();
        }
        // The text and the actions grow with the Windows text size, so the width they need is worked out again when it changes.
        // On a later turn: the text has been laid out at its new size by then.
        void OnTextScaleChanged() => row.DispatcherQueue?.TryEnqueue(Measure);
        row.Loaded += (_, _) =>
        {
            TextScaleChanged += OnTextScaleChanged;
            Measure();
        };
        row.Unloaded += (_, _) => TextScaleChanged -= OnTextScaleChanged;
        row.SizeChanged += (_, _) => Arrange();
        Arrange();
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

    /// <summary>
    /// A number box sized to its largest value, at the current text size and again whenever that changes. The caption comes
    /// from <see cref="Field"/>. <paramref name="atLeast"/> is a width (a function of the text size) the box must not go below,
    /// for a box whose digits are drawn larger than the usual 14 pt.
    /// </summary>
    private static NumberBox NumberInput(string captionKey, string id, double value, double minimum, double maximum,
        double step = 1, int fractionDigits = 0, bool whole = true, Func<double, double>? atLeast = null, bool trimmed = false)
    {
        // A trimmed box shows only the digits a value needs ("4", "0.125"), but keeps the width for the most it can hold.
        var box = whole && fractionDigits == 0
            ? NumberInt(captionKey, id, value, minimum, maximum, step)
            : Number(captionKey, id, value, minimum, maximum, step, trimmed ? 0 : fractionDigits);
        box.Header = null;
        box.HorizontalAlignment = HorizontalAlignment.Left;
        double Rule(double scale) =>
            Math.Max(FormMetrics.NumberWidth(minimum, maximum, fractionDigits, scale), atLeast?.Invoke(scale) ?? 0);
        WidthRules.AddOrUpdate(box, Rule);
        box.Width = Rule(TextScale);
        return box;
    }

    /// <summary>A drop-down as wide as its longest option, at the current text size and again whenever that changes.</summary>
    private static ComboBox Dropdown(string captionKey, string id, string value, IReadOnlyList<Choice> options)
    {
        var combo = SelectChoices(captionKey, id, value, options);
        combo.Header = null;
        combo.HorizontalAlignment = HorizontalAlignment.Left;
        var labels = options.Select(option => option.Label).Append(value).ToArray();
        double Rule(double scale) => FormMetrics.SelectWidth(WidestText(labels), scale);
        WidthRules.AddOrUpdate(combo, Rule);
        combo.Width = Rule(TextScale);
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
    /// A control as wide as its rule asks (a function of the Windows text size), never wider than the room it is offered, so
    /// its text wraps in a narrow window instead of being clipped. While it is on screen it follows the text size.
    /// </summary>
    private static FitWidth Fitted(FrameworkElement control, Func<double, double> rule)
    {
        var host = new FitWidth { PreferredWidth = rule(TextScale), HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(control);
        void Follow()
        {
            var wanted = rule(TextScale);
            if (Math.Abs(wanted - host.PreferredWidth) >= 0.5) host.PreferredWidth = wanted;
        }
        host.Loaded += (_, _) =>
        {
            TextScaleChanged += Follow;
            Follow();
        };
        host.Unloaded += (_, _) => TextScaleChanged -= Follow;
        return host;
    }

    /// <summary>
    /// Hosts a field at the width it was sized for but never wider than the room it is offered, so it cannot be clipped by a
    /// narrow window or a large text size. A number box whose steppers no longer fit switches to compact ones. While it is on
    /// screen the host follows the Windows text size: if the size changes the preferred width is worked out again, and only the
    /// width and the stepper style change, so a draft, the caret and the focus stay as they are.
    /// </summary>
    private static FrameworkElement Bounded(FrameworkElement control)
    {
        if ((control is NumberBox || control is ComboBox || control is TextBox) && double.IsFinite(control.Width))
        {
            var rule = WidthRuleOf(control);
            var host = new FitWidth { PreferredWidth = control.Width, HorizontalAlignment = HorizontalAlignment.Left };
            control.Width = double.NaN;
            control.HorizontalAlignment = HorizontalAlignment.Stretch;
            host.Children.Add(control);
            void UpdateSteppers()
            {
                if (control is NumberBox box)
                    box.SpinButtonPlacementMode = FormMetrics.SteppersInline(host.ActualWidth, host.PreferredWidth)
                        ? NumberBoxSpinButtonPlacementMode.Inline : NumberBoxSpinButtonPlacementMode.Compact;
            }
            void Follow()
            {
                var wanted = rule(TextScale);
                if (Math.Abs(wanted - host.PreferredWidth) < 0.5) return;
                host.PreferredWidth = wanted;
                host.UpdateLayout();
                UpdateSteppers();
            }
            host.SizeChanged += (_, _) => UpdateSteppers();
            host.Loaded += (_, _) =>
            {
                TextScaleChanged += Follow;
                Follow();
            };
            host.Unloaded += (_, _) => TextScaleChanged -= Follow;
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
        // One width for the group, and it follows the text size like any other field: the widest of the boxes' own rules.
        var rules = rows.Select(row => WidthRuleOf(row.Box)).ToArray();
        double GroupWidth(double scale) => rules.Max(rule => rule(scale));
        var width = GroupWidth(TextScale);
        var units = rows.Select(row => Text.Get(row.UnitKey)).ToArray();
        // Equal unit widths make every row wrap at the same width, and line the units up in a column when they fit. The common
        // width is measured again when the Windows text size changes, with the boxes, so the rows keep wrapping together.
        var unitWidth = Math.Ceiling(WidestText(units));
        var labels = new List<TextBlock>();
        var stack = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Left };
        for (var index = 0; index < rows.Length; index++)
        {
            var box = rows[index].Box;
            WidthRules.AddOrUpdate(box, GroupWidth);
            box.Width = width;
            AutomationProperties.SetName(box, $"{Text.Get(captionKey)}: {units[index]}");
            var label = new TextBlock
            {
                Text = units[index], Style = ResonantStyle("BodyStyle"), FontSize = 14, MinWidth = unitWidth,
                VerticalAlignment = VerticalAlignment.Center
            };
            labels.Add(label);
            // A unit that wraps sits 2 DIP under its box; the 14 DIP between rows keeps it with the box it belongs to.
            stack.Children.Add(Wrapped(8, 2, [Bounded(box), label]));
        }
        void FollowUnits()
        {
            var wanted = Math.Ceiling(WidestText(units));
            foreach (var label in labels) label.MinWidth = wanted;
        }
        stack.Loaded += (_, _) =>
        {
            TextScaleChanged += FollowUnits;
            FollowUnits();
        };
        stack.Unloaded += (_, _) => TextScaleChanged -= FollowUnits;
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

    /// <summary>A square icon-only command with a tooltip and a screen-reader name, for plain commands such as up and down.</summary>
    private Button IconButton(string glyph, string nameKey, string id, Func<Task> action)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 16 }, MinWidth = 44, Padding = new Thickness(0) };
        Identify(button, id, Text.Get(nameKey));
        ToolTipService.SetToolTip(button, Text.Get(nameKey));
        button.Click += async (_, _) => await GuardAsync(action);
        return button;
    }

    /// <summary>
    /// A switch with its caption at the left, linked to it for screen readers. The caption takes the room the switch leaves and
    /// wraps there, so a long caption (another language, a larger Windows text size) never pushes the pair past the page.
    /// </summary>
    private static Grid LabeledSwitch(string captionKey, ToggleSwitch toggle)
    {
        var label = new TextBlock
        {
            Text = Text.Get(captionKey), Style = ResonantStyle("BodyStyle"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        AutomationProperties.SetLabeledBy(toggle, label);
        var row = new Grid { ColumnSpacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(toggle, 1);
        row.Children.Add(label);
        row.Children.Add(toggle);
        return row;
    }

    /// <summary>Rebuilds the page and puts the scroll position back, so a structural change does not throw the reader to the top.</summary>
    private void RebuildPreservingScroll()
    {
        var offset = (PageHost.Content as ScrollViewer)?.VerticalOffset ?? 0;
        BuildPage();
        if (offset <= 0 || PageHost.Content is not ScrollViewer scroll) return;
        EventHandler<object>? restore = null;
        restore = (_, _) =>
        {
            scroll.LayoutUpdated -= restore;
            scroll.ChangeView(null, offset, null, true);
        };
        scroll.LayoutUpdated += restore;
    }

    /// <summary>
    /// Applies the commit contract to a number box. A NumberBox raises ValueChanged only when the user commits (Enter, focus
    /// leaving, a stepper), so an empty or invalid draft never reaches the setting: the box goes back to the value the setting
    /// has now, which <paramref name="current"/> reads. A refused value (<paramref name="commit"/> throws before it applies
    /// anything) goes back the same way, and the reason is shown.
    /// <paramref name="commit"/> applies the value to the setting before its first await, so edits are applied in the order
    /// they were made and the setting always holds what the box shows. Whatever follows the first await (remembering the
    /// value for the next session) cannot make the box and the setting disagree: if it fails, the error is shown and the
    /// box keeps the value the setting now has.
    /// A value that belongs to the project is applied in turn by the edit queue instead, so the box may show a newer value
    /// than the project holds for a moment. A refusal then puts the box back only if no newer value was committed after it: the
    /// newer one is still on its way and decides what the box shows. An empty or invalid draft submits nothing and shows the
    /// value that is on its way (see <see cref="SubmissionTracker{T}"/>), not the one the project still holds; so does a value
    /// that is refused at once while an older one is still in the queue.
    /// </summary>
    private void CommitNumber(NumberBox box, Func<double> current, Func<double, Task> commit)
    {
        var reverting = false;
        var submissions = new SubmissionTracker<double>();
        void Show(double value)
        {
            reverting = true;
            box.Value = value;
            reverting = false;
        }
        box.ValueChanged += async (sender, args) =>
        {
            if (reverting) return;
            if (!double.IsFinite(args.NewValue))
            {
                Show(submissions.OnItsWayOr(current()));
                return;
            }
            var ticket = submissions.Begin(args.NewValue);
            var applied = false;
            await GuardAsync(async () =>
            {
                await commit(args.NewValue);
                applied = true;
            });
            if (submissions.Resolve(ticket, applied) == SubmissionAnswer.Refused) Show(submissions.OnItsWayOr(current()));
        };
    }

    /// <summary>
    /// The same contract for a text box: nothing is written while typing; Enter or leaving the box commits a changed draft, and
    /// Esc puts back what the setting holds (or, while a draft is on its way, that draft: it is what the setting is about to
    /// hold). A refused draft goes back to what the setting holds, with the reason shown, unless a newer draft was committed
    /// after it or the person has typed on since. The same draft is never committed twice while the first is on its way.
    /// </summary>
    private void CommitText(TextBox box, Func<string> current, Func<string, Task> commit)
    {
        var submissions = new SubmissionTracker<string>();
        // The draft last applied as typed: what the setting holds, which may differ from current() when the commit tidies it.
        string? settled = null;
        string Held() => submissions.TryGetPending(out var onItsWay) ? onItsWay : settled ?? current();
        async Task CommitDraftAsync()
        {
            var draft = box.Text;
            if (draft == Held()) return;
            var ticket = submissions.Begin(draft);
            var applied = false;
            await GuardAsync(async () =>
            {
                await commit(draft);
                applied = true;
            });
            switch (submissions.Resolve(ticket, applied))
            {
                case SubmissionAnswer.Applied:
                    settled = draft;
                    break;
                case SubmissionAnswer.Refused:
                    settled = null;
                    // Only the refused draft is put back: whatever was typed after it is the person's to commit. It goes back to
                    // what the project holds, or to an older draft that is still in the queue (a blank name is refused at once).
                    if (box.Text == draft) box.Text = Held();
                    break;
            }
        }
        box.LostFocus += async (_, _) => await CommitDraftAsync();
        box.KeyDown += async (_, args) =>
        {
            if (args.Key == Windows.System.VirtualKey.Enter)
            {
                args.Handled = true;
                await CommitDraftAsync();
            }
            else if (args.Key == Windows.System.VirtualKey.Escape)
            {
                args.Handled = true;
                box.Text = Held();
            }
        };
    }

    /// <summary>
    /// The same contract for a drop-down: a choice commits at once, and a refused one puts the drop-down back to what the
    /// setting holds, unless a newer choice was made after it.
    /// </summary>
    private void CommitChoice(ComboBox combo, Func<string> current, Func<string, Task> commit)
    {
        var reverting = false;
        var submissions = new SubmissionTracker<string>();
        combo.SelectionChanged += async (_, _) =>
        {
            if (reverting || combo.SelectedItem is not Choice choice) return;
            var ticket = submissions.Begin(choice.Value);
            var applied = false;
            await GuardAsync(async () =>
            {
                await commit(choice.Value);
                applied = true;
            });
            if (submissions.Resolve(ticket, applied) != SubmissionAnswer.Refused) return;
            reverting = true;
            SelectChoice(combo, submissions.OnItsWayOr(current()));
            reverting = false;
        };
    }

    private const double SliderTrackWidth = 190;

    /// <summary>
    /// A caption above a slider with its value as text beside it, for a percentage or a level. A drag or a held arrow key is
    /// one gesture: the text follows the thumb, and the value is committed once, when the gesture ends (see
    /// <see cref="GestureCommit"/>), so a drag is one undo step and one write, not one per tick. A refused value puts the slider
    /// back to what the setting holds, unless a newer value was committed after it. The slider is as wide as its track and its
    /// longest value text, and gives way in a narrow card. It waits for the editor gate like the other fields of a lane
    /// (see <see cref="EditorControl{T}"/>).
    /// </summary>
    private StackPanel SliderField(string captionKey, string id, string accessibleName, int value, int minimum, int maximum,
        Func<int, string> format, IEnumerable<string> samples, Func<int> current, Func<int, Task> commit)
    {
        var caption = new TextBlock { Text = Text.Get(captionKey), Style = ResonantStyle("FieldCaptionStyle"), Margin = new Thickness(0, 0, 0, 4) };
        var slider = EditorControl(new Slider
        {
            Minimum = minimum, Maximum = maximum, Value = value, StepFrequency = 1, SmallChange = 1, LargeChange = 10,
            IsThumbToolTipEnabled = false, MinWidth = 96, MinHeight = 44,
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center
        });
        Identify(slider, id, accessibleName);
        AutomationProperties.SetLabeledBy(slider, caption);
        var readout = new TextBlock
        {
            Text = format(value), Style = ResonantStyle("BodyStyle"), FontSize = 14, TextWrapping = TextWrapping.NoWrap,
            TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            MinWidth = Math.Ceiling(WidestText(samples))
        };
        // The value as text ("70%", "Left 30") belongs to the slider, so what it says is linked to it, not only drawn beside it.
        AutomationProperties.GetDescribedBy(slider).Add(readout);
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(readout, 1);
        row.Children.Add(slider);
        row.Children.Add(readout);
        var host = new FitWidth { PreferredWidth = SliderTrackWidth + row.ColumnSpacing + readout.MinWidth, HorizontalAlignment = HorizontalAlignment.Left };
        host.Children.Add(row);

        var gesture = new GestureCommit();
        var reverting = false;
        var submissions = new SubmissionTracker<int>();
        async Task CommitGestureAsync()
        {
            var wanted = (int)Math.Round(slider.Value);
            var ticket = submissions.Begin(wanted);
            var applied = false;
            await GuardAsync(async () =>
            {
                await commit(wanted);
                applied = true;
            });
            if (submissions.Resolve(ticket, applied) != SubmissionAnswer.Refused) return;
            reverting = true;
            slider.Value = submissions.OnItsWayOr(current());
            readout.Text = format((int)Math.Round(slider.Value));
            reverting = false;
        }
        slider.ValueChanged += async (_, args) =>
        {
            readout.Text = format((int)Math.Round(args.NewValue));
            if (reverting) return;
            // The thumb has moved on from any value submitted before: if that one is refused it must not put the slider back
            // (the new value would be overwritten, and releasing the thumb would commit the value that was put back).
            submissions.Changed();
            if (gesture.Changed()) await CommitGestureAsync();
        };
        // The slider's own parts handle pointer and key events, so these are heard even when they are marked handled.
        slider.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => gesture.PointerDown()), true);
        var released = new Microsoft.UI.Xaml.Input.PointerEventHandler(async (_, _) =>
        {
            if (gesture.PointerUp()) await CommitGestureAsync();
        });
        slider.AddHandler(UIElement.PointerReleasedEvent, released, true);
        slider.AddHandler(UIElement.PointerCaptureLostEvent, released, true);
        slider.AddHandler(UIElement.PointerCanceledEvent, released, true);
        static bool StepsValue(Windows.System.VirtualKey key) => key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right
            or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down or Windows.System.VirtualKey.Home
            or Windows.System.VirtualKey.End or Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown;
        slider.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
        {
            if (StepsValue(args.Key)) gesture.KeyDown();
        }), true);
        slider.AddHandler(UIElement.KeyUpEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler(async (_, args) =>
        {
            if (StepsValue(args.Key) && gesture.KeyUp()) await CommitGestureAsync();
        }), true);
        // A key still held when the focus leaves never reports its release here.
        slider.LostFocus += async (_, _) =>
        {
            if (gesture.KeyUp()) await CommitGestureAsync();
        };

        var field = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
        field.Children.Add(caption);
        field.Children.Add(host);
        return field;
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
