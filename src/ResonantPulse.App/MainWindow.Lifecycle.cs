using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassCallback(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    private SubclassCallback? _powerCallback;

    /// <summary>
    /// Set by the first key or press of the pointer, however a control handles it: from then on the keyboard focus is the person's and the
    /// start of the application never moves it (see <see cref="InitialFocus"/>).
    /// </summary>
    private bool _personHasActed;

    /// <summary>
    /// The Settings item shows its name where the pane has the room for it beside the item of any page (<see cref="NavigationFit"/>), and its
    /// gear alone where it has not: the pane clips the name there. The name stays as the item's tooltip and as its name for a screen reader.
    /// Runs when the language changes and whenever the pane's width does. The names it measures widen with the Windows text size, which is
    /// read when it runs: a change of the text size while the app runs is only followed at the next of those (not wired to the text size).
    /// </summary>
    private void UpdateSettingsLabel()
    {
        if (!_isReady || Navigation.SettingsItem is not NavigationViewItem settings || Navigation.ActualWidth <= 0) return;
        var name = Text.Get("Settings");
        var pages = WidestText(new[] { "Play", "Studio", "Train", "Stage", "Library" }.Select(Text.Get));
        var label = NavigationFit.SettingsNameFits(Navigation.ActualWidth, pages, WidestText([name])) ? name : "";
        if (!string.Equals(settings.Content as string, label, StringComparison.Ordinal)) settings.Content = label;
        ToolTipService.SetToolTip(settings, name);
    }

    /// <summary>
    /// Puts the keyboard focus where a person starts from (<see cref="InitialFocus"/>), once the page is built and again whenever the window
    /// is activated until the person has acted: the activation that is first can come before the page exists, and a focus that was given
    /// to a window that is not active yet may not be kept. A focus that is not the system's own leftover is never taken.
    /// </summary>
    private void PlaceInitialFocus()
    {
        if (!_isReady || IsWindowGoing || _closeFlushing || _isDialogOpen || HasOpenPanel()) return;
        var leftBySystem = FocusedElement() is null or NavigationViewItemBase;
        if (!InitialFocus.Takes(_personHasActed, leftBySystem)) return;
        var place = InitialFocus.Choose(StartButton.IsEnabled && StartButton.IsLoaded, AudioOptionsButton.IsEnabled && AudioOptionsButton.IsLoaded);
        var target = place switch { InitialFocus.Place.Start => StartButton, InitialFocus.Place.Audio => AudioOptionsButton, _ => null };
        target?.Focus(FocusState.Programmatic);
    }

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint hwnd, SubclassCallback callback, nuint id, nuint data);
    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint hwnd, SubclassCallback callback, nuint id);
    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    private string _closeSource = "";
    private long _closeSourceAt;

    /// <summary>
    /// Remembers what started a close, so the log can say afterwards whether a person closed the window (the X button,
    /// Alt+F4 or the taskbar) or something else did (another program, UI Automation, Windows ending the session).
    /// </summary>
    private void RecordCloseSource(uint message, nuint wParam)
    {
        var source = message switch
        {
            // WM_SYSCOMMAND / SC_CLOSE comes first; the WM_CLOSE it produces right after must not overwrite it.
            0x0112 when (wParam & 0xFFF0) == 0xF060 => "close command (X button, Alt+F4 or taskbar)",
            0x0010 when Environment.TickCount64 - _closeSourceAt > 2000 => "close message without a close command",
            0x0011 => "Windows is asking to end the session",
            0x0016 when wParam != 0 => "Windows is ending the session",
            _ => null
        };
        if (source is null) return;
        _closeSource = source;
        _closeSourceAt = Environment.TickCount64;
    }

    private string DescribeClose()
    {
        var info = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        var idle = GetLastInputInfo(ref info)
            ? $"{unchecked((uint)Environment.TickCount - info.Time) / 1000.0:0.#} s before"
            : "unknown";
        var source = _closeSource.Length == 0 ? "no close message seen" : _closeSource;
        return $"{source}; last keyboard or mouse input {idle}";
    }

    private void InstallPowerHook(nint hwnd)
    {
        _powerCallback = (window, message, wParam, lParam, _, _) =>
        {
            RecordCloseSource(message, wParam);
            // Win32 desktop windows receive contrast/theme broadcasts here.
            // WinRT accessibility event registration failed with ERROR_NOT_FOUND
            // in the unpackaged desktop host.
            if (message is 0x0015 or 0x001A or 0x031A)
                DispatcherQueue.TryEnqueue(RefreshInstrumentTheme);
            // WM_POWERBROADCAST: suspend, automatic resume, user resume.
            // Waking never starts audio and never catches up missed beats.
            if (message == 0x0218 && wParam is 4 or 7 or 18)
            {
                try
                {
                    InvalidateTiming();
                    SupersedeTransportIntent();
                    _transport.Stop();
                    _ = GuardAsync(SaveRecoveryAsync);
                    RefreshSnapshot();
                }
                catch (Exception exception) { Diagnostics.Write("Power transition", exception); }
            }
            return DefSubclassProc(window, message, wParam, lParam);
        };
        if (!SetWindowSubclass(hwnd, _powerCallback, 0x5250, 0))
            Diagnostics.Write($"Window notification hook unavailable ({Marshal.GetLastWin32Error()})");
    }

    private void RefreshInstrumentTheme()
    {
        if (_isClosed) return;
        _instrument?.RefreshTheme();
        _liveStrip?.RefreshTheme();
        _beatStrip?.RefreshTheme();
        _tempoCurve?.RefreshTheme();
        ApplyCaptionTheme();
    }

    private void RemovePowerHook(nint hwnd)
    {
        if (_powerCallback is not null) RemoveWindowSubclass(hwnd, _powerCallback, 0x5250);
    }
}
