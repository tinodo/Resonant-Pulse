using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private sealed record Choice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static StackPanel Column(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 16 };
        foreach (var child in children) panel.Children.Add(Aligned(child));
        return panel;
    }

    /// <summary>
    /// A ComboBox keeps its header inside the control, which leaves the box itself 32 DIP tall, while NumberBox and TextBox
    /// boxes are 44 DIP. Rows of mixed fields therefore misaligned (Settings, Studio). The caption moves above the box and the
    /// box becomes 44 DIP with the same 4 DIP gap the other fields use.
    /// </summary>
    private static UIElement Aligned(UIElement child)
    {
        if (child is not ComboBox { Header: TextBlock caption } combo) return child;
        // The caption moved out, so the implicit style's 44 DIP minimum is now the box itself. No fixed height: the box must
        // be able to grow with Windows text size.
        combo.Header = null;
        combo.HorizontalAlignment = HorizontalAlignment.Stretch;
        caption.Margin = new Thickness(0, 0, 0, 4);
        var field = new StackPanel();
        field.Children.Add(caption);
        field.Children.Add(combo);
        return field;
    }

    private static Grid Flow(params UIElement[] children) => WrapControls(220, false, children);

    private static Grid WrapControls(double minimumWidth, bool stretchHeight, params UIElement[] source)
    {
        var scale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
        var children = source.Select(Aligned).ToArray();
        var panel = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        foreach (var child in children)
        {
            if (child is not FrameworkElement element)
                throw new ArgumentException("Form controls must be framework elements.", nameof(source));
            // Fields keep their own height; commands in one row share the tallest one's height, so a two-line label
            // never leaves its neighbours shorter.
            element.VerticalAlignment = stretchHeight ? VerticalAlignment.Stretch : VerticalAlignment.Top;
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(child);
        }
        var lastColumns = 0;
        void Arrange(double width)
        {
            var columns = FlowColumns(width, minimumWidth, scale, children.Length);
            if (columns == lastColumns) return;
            lastColumns = columns;
            panel.ColumnDefinitions.Clear();
            panel.RowDefinitions.Clear();
            for (var column = 0; column < columns; column++)
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (var row = 0; row < (children.Length + columns - 1) / columns; row++)
                panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var index = 0; index < children.Length; index++)
            {
                if (children[index] is FrameworkElement element)
                {
                    Grid.SetColumn(element, index % columns);
                    Grid.SetRow(element, index / columns);
                }
            }
        }
        Arrange(0);
        panel.SizeChanged += (_, args) => Arrange(args.NewSize.Width);
        return panel;
    }

    private static int FlowColumns(double width, double minimumWidth, double textScale, int count) =>
        Math.Clamp((int)Math.Floor((Math.Max(0, width) + 12) /
            (minimumWidth * Math.Sqrt(textScale) + 12)), 1, Math.Max(1, count));

    private static StackPanel Commands(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static UIElement CommandFlow(params UIElement[] children) => WrapControls(170, true, children);

    private static TextBlock Body(string text) => new()
    {
        Text = text, FontSize = 14,
        Style = (Style)Application.Current.Resources["BodyStyle"]
    };

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.Resources["HeadingStyle"]
    };

    private static TextBlock Copy(string key) => Body(Text.Get(key));

    private static ScrollViewer Scroll(UIElement content) => new()
    {
        Content = content,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalScrollMode = ScrollMode.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private Button ActionButton(string key, string id, Func<Task> action, string? glyph = null,
        ButtonKind kind = ButtonKind.Secondary)
    {
        var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Center };
        if (kind == ButtonKind.Primary) button.Style = ResonantStyle("PrimaryButtonStyle");
        else if (kind == ButtonKind.Tertiary) button.Style = ResonantStyle("ToolButtonStyle");
        LabelButton(button, key, glyph);
        Identify(button, id, Text.Get(key));
        button.Click += async (_, _) => await GuardAsync(action);
        return button;
    }

    private Button ActionButton(string key, string id, Action action, string? glyph = null,
        ButtonKind kind = ButtonKind.Secondary) =>
        ActionButton(key, id, () => { action(); return Task.CompletedTask; }, glyph, kind);

    private static NumberBox Number(string key, string id, double value, double minimum, double maximum, double increment = 1,
        int fractionDigits = 1)
    {
        var input = new NumberBox
        {
            Header = Copy(key),
            Value = value,
            Minimum = minimum,
            Maximum = maximum,
            SmallChange = increment,
            LargeChange = increment * 10,
            NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter([Text.Culture.Name], "US")
            {
                IntegerDigits = 1, FractionDigits = fractionDigits, IsGrouped = false,
                NumberRounder = new Windows.Globalization.NumberFormatting.SignificantDigitsNumberRounder { SignificantDigits = 12 }
            },
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        Identify(input, id, Text.Get(key));
        return input;
    }

    private static NumberBox NumberInt(string key, string id, double value, double minimum, double maximum, double increment = 1)
    {
        var input = Number(key, id, value, minimum, maximum, increment);
        input.NumberFormatter = new Windows.Globalization.NumberFormatting.DecimalFormatter([Text.Culture.Name], "US")
        {
            IntegerDigits = 1, FractionDigits = 0, IsGrouped = false,
            NumberRounder = new Windows.Globalization.NumberFormatting.IncrementNumberRounder
            {
                Increment = 1, RoundingAlgorithm = Windows.Globalization.NumberFormatting.RoundingAlgorithm.RoundHalfUp
            }
        };
        return input;
    }

    private static TextBox Input(string key, string id, string value)
    {
        var input = new TextBox
        {
            Header = Copy(key), Text = value,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Identify(input, id, Text.Get(key));
        return input;
    }

    private static ComboBox Select(string key, string id, string value, params string[] options) =>
        SelectChoices(key, id, value, options.Select(option => new Choice(option, Text.Get(option))).ToArray());

    private static ComboBox SelectChoices(string key, string id, string value, IReadOnlyList<Choice> options)
    {
        // Preserve valid imported values not in a quick-pick list (notably a
        // custom tempo unit). Never silently replace them with the first option.
        if (!options.Any(option => option.Value == value)) options = options.Append(new Choice(value, value)).ToArray();
        var input = new ComboBox
        {
            Header = Copy(key),
            ItemsSource = options,
            SelectedIndex = Math.Max(0, options.ToList().FindIndex(option => option.Value == value)),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Identify(input, id, Text.Get(key));
        return input;
    }

    private static string Selected(ComboBox combo) => ((Choice)combo.SelectedItem).Value;

    private static VariableSizedWrapGrid AccentFlow()
    {
        var scale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
        var panel = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal, ItemWidth = 155 * Math.Sqrt(scale), ItemHeight = 76 * scale
        };
        panel.SizeChanged += (_, args) => panel.ItemWidth = Math.Min(Math.Max(100, args.NewSize.Width), 155 * Math.Sqrt(scale));
        return panel;
    }

    private static CheckBox Check(string key, string id, bool value)
    {
        // The default 5 DIP top padding pushes a centred label below the box's centre; without it they share one line.
        var check = new CheckBox
        {
            Content = new TextBlock { Text = Text.Get(key), TextWrapping = TextWrapping.Wrap }, IsChecked = value, MinHeight = 44,
            VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 0, 0, 0)
        };
        Identify(check, id, Text.Get(key));
        return check;
    }

    private static double Value(NumberBox number)
    {
        if (!double.IsFinite(number.Value)) throw new ArgumentException(Text.Get("Error"));
        return number.Value;
    }

    private static int Integer(NumberBox number) => Integer(number.Value);

    /// <summary>
    /// A NumberBox shows a whole number rounded, but reports the fraction typed ("1.9" shows as 2 and is 1.9), so a count must be
    /// checked rather than cast: the project should hold what the field shows or refuse it with a message.
    /// </summary>
    private static int Integer(double value)
    {
        if (!double.IsFinite(value)) throw new ArgumentException(Text.Get("Error"));
        if (!WholeNumbers.TryGet(value, out var whole)) throw new ArgumentException(Text.Get("WholeNumber"));
        return whole;
    }

    /// <summary>The question being asked, if any. Closing the window dismisses it (as Cancel): nothing is confirmed while it saves.</summary>
    private ContentDialog? _confirmation;

    private async Task<bool> ConfirmAsync(string message, string primary = "Continue")
    {
        // No question is asked while the window closes: a request that was still loading when closing began (an Open that is
        // validating its file) would otherwise ask one after the last save has passed the point where it could be answered.
        if (_isDialogOpen || _closeFlushing || _isClosed) return false;
        _isDialogOpen = true;
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.RequestedTheme,
                Title = Text.Get(primary),
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = Text.Get(primary),
                CloseButtonText = Text.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close
            };
            Identify(dialog, "ConfirmationDialog", Text.Get(primary));
            var mute = new Microsoft.UI.Xaml.Input.KeyboardAccelerator
            {
                Key = Windows.System.VirtualKey.M,
                Modifiers = Windows.System.VirtualKeyModifiers.Control
            };
            mute.Invoked += (_, args) =>
            {
                _transport.IsMuted = true;
                RefreshSnapshot();
                args.Handled = true;
            };
            dialog.KeyboardAccelerators.Add(mute);
            _confirmation = dialog;
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _confirmation = null;
            _isDialogOpen = false;
        }
    }
}
