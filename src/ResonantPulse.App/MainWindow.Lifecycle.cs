using System.Runtime.InteropServices;
using ResonantPulse.App.Storage;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassCallback(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    private SubclassCallback? _powerCallback;

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
        _beatStrip?.RefreshTheme();
        _tempoCurve?.RefreshTheme();
        ApplyCaptionTheme();
    }

    private void RemovePowerHook(nint hwnd)
    {
        if (_powerCallback is not null) RemoveWindowSubclass(hwnd, _powerCallback, 0x5250);
    }
}
