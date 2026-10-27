using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using ResonantPulse.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ResonantPulse.App;

// Local visual verification only. This file is compiled ONLY with -p:UiTest=true (see ResonantPulse.App.csproj);
// shipped builds contain none of it. A private desktop is never composed by Windows, so PrintWindow and screen copies
// return black there. With RESONANT_PULSE_UITEST_DIR set, a UI Automation driver sets the text of the hidden
// "UiTestCaptureName" box and invokes the hidden "UiTestCapture" button; the app then renders its own XAML tree and
// every open flyout to <name>.png in that folder. Nothing outside this window can ever be captured.
public sealed partial class MainWindow
{
    partial void InstallUiTestHook()
    {
        var folder = Environment.GetEnvironmentVariable("RESONANT_PULSE_UITEST_DIR");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        var name = Tiny(new TextBox());
        var capture = Tiny(new Button());
        var grow = Tiny(new Button());
        var expand = Tiny(new Button());
        var textScale = Tiny(new Button());
        var replace = Tiny(new Button());
        var holdClose = Tiny(new Button());
        var ask = Tiny(new Button());
        Identify(name, "UiTestCaptureName", "ui test capture name");
        Identify(capture, "UiTestCapture", "ui test capture");
        Identify(grow, "UiTestGrowToolLabels", "ui test grow tool labels");
        Identify(expand, "UiTestExpandScrollBars", "ui test expand scroll bars");
        Identify(textScale, "UiTestTextScale", "ui test text scale");
        Identify(replace, "UiTestReplaceProject", "ui test replace project");
        Identify(holdClose, "UiTestHoldClose", "ui test hold close");
        Identify(ask, "UiTestAskQuestion", "ui test ask question");
        capture.Click += async (_, _) => await CaptureAsync(folder, name.Text);
        // Stands in for a request that was still loading when the window began to close (an Open validating its file) and now
        // asks whether to replace the project.
        ask.Click += async (_, _) => await GuardAsync(async () => { _ = await ConfirmAsync("Replace the project? (ui test)"); });
        // Makes the last save of the next close take twelve seconds longer, so the window can be looked at while it closes.
        holdClose.Click += (_, _) => _uiTestCloseHold = TimeSpan.FromSeconds(25);
        // Stands in for Ctrl+O on the Stage page (a keystroke a private desktop cannot send): the project is replaced by an empty
        // one, and the replacement is held open for three seconds so the page can be looked at while it runs.
        replace.Click += async (_, _) =>
        {
            _uiTestReplaceHold = TimeSpan.FromSeconds(3);
            await GuardAsync(() => ReplaceDocumentAsync(new PulseDocument { Name = "Replacement" }));
        };
        // Stands in for a Windows text-size change: the page-tools labels get bigger while the page keeps its size.
        grow.Click += (_, _) => GrowToolLabels(1.6);
        // Stands in for the pointer being over a scroll bar (or Windows set to always show scroll bars), which a private
        // desktop cannot do: the beat strip's bar takes its full, expanded form.
        expand.Click += (_, _) => ExpandScrollBars();
        // Stands in for the Windows text size changing while the app runs: the toggle between 100 % and 200 %.
        textScale.Click += (_, _) =>
        {
            _uiTestTextScale = _uiTestTextScale >= 2 ? 1 : 2;
            RaiseTextScaleChanged();
        };
        var host = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
        host.Children.Add(name);
        host.Children.Add(capture);
        host.Children.Add(grow);
        host.Children.Add(expand);
        host.Children.Add(textScale);
        host.Children.Add(replace);
        host.Children.Add(holdClose);
        host.Children.Add(ask);
        Grid.SetRowSpan(host, 4);
        Root.Children.Add(host);
        Diagnostics.Write("UI test capture hook installed (local verification build)");
    }

    private TimeSpan _uiTestReplaceHold;
    private TimeSpan _uiTestCloseHold;

    partial void HoldReplacementForUiTest(ref Task? hold)
    {
        if (_uiTestReplaceHold <= TimeSpan.Zero) return;
        hold = Task.Delay(_uiTestReplaceHold);
        _uiTestReplaceHold = TimeSpan.Zero;
    }

    partial void HoldCloseForUiTest(ref Task? hold)
    {
        if (_uiTestCloseHold <= TimeSpan.Zero) return;
        hold = Task.Delay(_uiTestCloseHold);
        _uiTestCloseHold = TimeSpan.Zero;
    }

    // Stands in for a larger Windows text size: with RESONANT_PULSE_UITEST_TEXTSCALE set (for example 2), every width that
    // follows the text size is worked out as if the text were that big. That is what decides whether a field fits its room or
    // gives way, which a private desktop cannot show by changing the real system setting (and must not). The hidden
    // "UiTestTextScale" button does the same while the app runs, and raises the event the real change raises.
    private static double _uiTestTextScale;

    static partial void AdjustTextScale(ref double scale)
    {
        var forced = _uiTestTextScale;
        if (forced <= 0 && !double.TryParse(Environment.GetEnvironmentVariable("RESONANT_PULSE_UITEST_TEXTSCALE"),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out forced))
            return;
        if (forced > scale) scale = forced;
    }

    private static T Tiny<T>(T control) where T : Control
    {
        control.Width = control.Height = 2;
        control.MinWidth = control.MinHeight = 0;
        control.Padding = new Thickness(0);
        control.BorderThickness = new Thickness(0);
        control.Opacity = 0.01;
        return control;
    }

    private void GrowToolLabels(double factor)
    {
        if (FindById(Root, "PlaySettingsStrip") is not StackPanel strip) return;
        foreach (var button in strip.Children.OfType<Button>())
            if (button.Content is Grid { Children: [_, TextBlock label] })
                label.FontSize *= factor;
    }

    private static FrameworkElement? FindById(DependencyObject parent, string id)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is FrameworkElement element && AutomationProperties.GetAutomationId(element) == id) return element;
            if (FindById(child, id) is { } found) return found;
        }
        return null;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private void ExpandScrollBars()
    {
        if (FindById(Root, "PulseAccentStrip") is not { } strip) return;
        foreach (var bar in Descendants<ScrollBar>(strip))
        {
            // Two state groups: how big the bar is, and whether it is shown at all (it is hidden until something scrolls).
            VisualStateManager.GoToState(bar, "ExpandedWithoutAnimation", false);
            VisualStateManager.GoToState(bar, "MouseIndicator", false);
        }
    }

    private async Task CaptureAsync(string folder, string requested)
    {
        var safe = new string(requested.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        if (safe.Length == 0) safe = "capture";
        // RenderTargetBitmap returns nothing for elements inside a Popup. Each open flyout presenter is therefore moved
        // into the page at the same position for the single render and handed back to its popup afterwards.
        var moved = new List<(Popup Popup, FrameworkElement Child, Grid Host, (HorizontalAlignment Horizontal, VerticalAlignment Vertical) Alignment)>();
        try
        {
            var notes = new List<string>();
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(Root.XamlRoot).ToList())
            {
                if (popup.Child is not FrameworkElement child || child.ActualWidth < 1 || child.ActualHeight < 1) continue;
                var origin = child.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0, 0));
                // The presenter keeps the margin the popup placed it with, so the host simply fills the window.
                var host = new Grid { IsHitTestVisible = false };
                popup.Child = null;
                var alignment = (child.HorizontalAlignment, child.VerticalAlignment);
                child.HorizontalAlignment = HorizontalAlignment.Left;
                child.VerticalAlignment = VerticalAlignment.Top;
                host.Children.Add(child);
                Grid.SetRowSpan(host, 4);
                Canvas.SetZIndex(host, 1000);
                Root.Children.Add(host);
                moved.Add((popup, child, host, alignment));
                notes.Add($"{child.GetType().Name}@{origin.X:0},{origin.Y:0}");
            }
            if (moved.Count > 0)
            {
                Root.UpdateLayout();
                await Task.Delay(250);
                Root.UpdateLayout();
                foreach (var (_, child, _, _) in moved)
                    notes.Add($"laid out {child.ActualWidth:0}x{child.ActualHeight:0}");
            }
            var page = new RenderTargetBitmap();
            await page.RenderAsync(Root);
            var scale = Root.XamlRoot.RasterizationScale;
            await WritePngAsync(Path.Combine(folder, safe + ".png"), await ReadPixelsAsync(page), page.PixelWidth, page.PixelHeight, 96 * scale);
            await File.WriteAllTextAsync(Path.Combine(folder, safe + ".ok"),
                $"{page.PixelWidth}x{page.PixelHeight} scale {scale}; popups: {(notes.Count == 0 ? "none" : string.Join(" | ", notes))}");
        }
        catch (Exception exception)
        {
            Diagnostics.Write("UI test capture failed", exception);
            await File.WriteAllTextAsync(Path.Combine(folder, safe + ".err"), exception.ToString());
        }
        finally
        {
            foreach (var (popup, child, host, alignment) in moved)
            {
                host.Children.Remove(child);
                Root.Children.Remove(host);
                child.HorizontalAlignment = alignment.Horizontal;
                child.VerticalAlignment = alignment.Vertical;
                popup.Child = child;
            }
        }
    }

    private static async Task<byte[]> ReadPixelsAsync(RenderTargetBitmap bitmap)
    {
        var buffer = await bitmap.GetPixelsAsync();
        var bytes = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static async Task WritePngAsync(string path, byte[] pixels, int width, int height, double dpi)
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)width, (uint)height, dpi, dpi, pixels);
        await encoder.FlushAsync();
        var bytes = new byte[stream.Size];
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        reader.ReadBytes(bytes);
        await File.WriteAllBytesAsync(path, bytes);
    }
}
