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
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static Grid Flow(params UIElement[] children) => WrapControls(220, children);

    private static Grid WrapControls(double minimumWidth, params UIElement[] children)
    {
        var scale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
        var panel = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        foreach (var child in children)
        {
            if (child is not FrameworkElement element)
                throw new ArgumentException("Form controls must be framework elements.", nameof(children));
            element.VerticalAlignment = VerticalAlignment.Top;
            element.HorizontalAlignment = HorizontalAlignment.Stretch;
            panel.Children.Add(child);
        }
        var lastColumns = 0;
        void Arrange(double width)
        {
            var columns = Math.Clamp((int)Math.Floor((Math.Max(0, width) + 12) /
                (minimumWidth * Math.Sqrt(scale) + 12)), 1, Math.Max(1, children.Length));
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

    private static StackPanel Commands(params UIElement[] children)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static UIElement CommandFlow(params UIElement[] children) => WrapControls(170, children);

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

    private Button ActionButton(string key, string id, Func<Task> action)
    {
        var button = new Button { HorizontalContentAlignment = HorizontalAlignment.Center };
        LabelButton(button, key);
        Identify(button, id, Text.Get(key));
        button.Click += async (_, _) => await GuardAsync(action);
        return button;
    }

    private Button ActionButton(string key, string id, Action action) =>
        ActionButton(key, id, () => { action(); return Task.CompletedTask; });

    private static NumberBox Number(string key, string id, double value, double minimum, double maximum, double increment = 1)
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
                IntegerDigits = 1, FractionDigits = 1, IsGrouped = false,
                NumberRounder = new Windows.Globalization.NumberFormatting.SignificantDigitsNumberRounder { SignificantDigits = 12 }
            },
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top
        };
        Identify(input, id, Text.Get(key));
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

    private static VariableSizedWrapGrid AccentFlow(double width = 155, double height = 76)
    {
        var scale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
        var panel = new VariableSizedWrapGrid
        {
            Orientation = Orientation.Horizontal, ItemWidth = width * Math.Sqrt(scale), ItemHeight = height * scale
        };
        panel.SizeChanged += (_, args) => panel.ItemWidth = Math.Min(Math.Max(100, args.NewSize.Width), width * Math.Sqrt(scale));
        return panel;
    }

    private static CheckBox Check(string key, string id, bool value)
    {
        var check = new CheckBox { Content = new TextBlock { Text = Text.Get(key), TextWrapping = TextWrapping.Wrap }, IsChecked = value, MinHeight = 44 };
        Identify(check, id, Text.Get(key));
        return check;
    }

    private static double Value(NumberBox number)
    {
        if (!double.IsFinite(number.Value)) throw new ArgumentException(Text.Get("Error"));
        return number.Value;
    }

    private static int Integer(NumberBox number)
    {
        var value = Value(number);
        if (value != Math.Truncate(value) || value is < int.MinValue or > int.MaxValue)
            throw new ArgumentException(Text.Get("WholeNumber"));
        return (int)value;
    }

    private async Task<bool> ConfirmAsync(string message, string primary = "Continue")
    {
        if (_isDialogOpen) return false;
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
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally { _isDialogOpen = false; }
    }
}
