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

    private void InstallPowerHook(nint hwnd)
    {
        _powerCallback = (window, message, wParam, lParam, _, _) =>
        {
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
                    _transportIntent++;
                    _transport.Stop();
                    _ = GuardAsync(() => _workspace.SaveRecoveryAsync(LocalWorkspace.Clone(_document)));
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
