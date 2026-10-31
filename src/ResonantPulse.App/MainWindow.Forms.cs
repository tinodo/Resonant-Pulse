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
            var content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
            Identify(content, "ConfirmationMessage", message);
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot,
                RequestedTheme = Root.RequestedTheme,
                Title = Text.Get(primary),
                Content = content,
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
