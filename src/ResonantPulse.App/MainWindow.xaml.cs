using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;
using Windows.Graphics;
using Windows.System;

namespace ResonantPulse.App;

public sealed partial class MainWindow : Window
{
    private readonly AudioTransport _transport = new();
    private readonly LocalWorkspace _workspace = new();
    private readonly SemaphoreSlim _edits = new(1);
    private readonly Stack<PulseDocument> _undo = new();
    private readonly Stack<PulseDocument> _redo = new();
    private readonly TapTempoEstimator _tap = new();
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private Preferences _preferences = new();
    private PulseDocument _document = new();
    private string _view = "Play";
    private bool _isReady;
    private bool _isClosed;
    private bool _isBusy;
    private bool _hasPendingEdits;
    private bool _isLocked;
    private bool _isDialogOpen;
    private bool _isSynchronizingGain;
    private bool _isOutputMissing;
    private int _transportIntent;
    private int _documentRevision;
    private int _armedSong = -1;
    private PulseInstrument? _instrument;
    private TextBlock? _position;
    private TextBlock? _stageTempo;
    private TextBlock? _programStatus;
    private ProgressBar? _progress;
    private TextBlock? _timingStatus;
    private TempoCurve? _tempoCurve;
    private ListView? _libraryList;
    private TextBox? _search;
    private IReadOnlyList<LibraryItem> _library = [];

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    public MainWindow()
    {
        InitializeComponent();
        Title = "Resonant Pulse";
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new SizeInt32(Math.Min(area.Width, (int)(1080 * scale)),
            Math.Min(area.Height, (int)(820 * scale))));
        Navigation.SelectionChanged += (_, args) =>
        {
            if (!_isReady) return;
            _view = args.IsSettingsSelected ? "Settings" :
                (args.SelectedItemContainer?.Tag as string ?? "Play");
            _preferences.LastView = _view;
            BuildPage();
            _ = GuardAsync(() => _workspace.SavePreferencesAsync(_preferences));
        };
        StartButton.Click += async (_, _) => await GuardAsync(TogglePlaybackAsync);
        StopButton.Click += (_, _) =>
        {
            _transportIntent++;
            InvalidateTiming();
            _transport.Stop();
            _hasPendingEdits = false;
            RefreshSnapshot();
        };
        MuteButton.Click += (_, _) => { _transport.IsMuted = !_transport.IsMuted; RefreshSnapshot(); };
        OutputButton.Click += (_, _) => SelectView("Settings");
        MasterGain.ValueChanged += (_, args) =>
        {
            _transport.Gain = (float)args.NewValue;
            if (_isReady && !_isSynchronizingGain)
                _ = GuardAsync(() => CommitAsync(doc => doc.Settings.MasterGain = (float)args.NewValue, false));
        };
        Root.KeyDown += OnKeyDown;
        Root.ActualThemeChanged += (_, _) => _instrument?.RefreshTheme();
        Root.Loaded += async (_, _) =>
        {
            if (_isReady) return;
            await InitializeAsync();
        };
        _transport.Failed += (_, exception) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed) return;
            try { _transport.AcknowledgeFault(); }
            catch (Exception stopError) { Diagnostics.Write("Failed endpoint stop", stopError); }
            InvalidateTiming();
            ShowError(exception, "AudioError");
            RefreshSnapshot();
        });
        _refresh.Tick += (_, _) => RefreshSnapshot();
        InstallPowerHook(hwnd);
        Closed += (_, _) =>
        {
            _isClosed = true;
            _refresh.Stop();
            RemovePowerHook(hwnd);
            _transport.Dispose();
            Diagnostics.Write("Native window closed");
        };
    }

    private async Task InitializeAsync()
    {
        try { _preferences = await _workspace.LoadPreferencesAsync(); }
        catch (Exception exception) { ShowError(exception); }
        Text.Select(_preferences.Language);
        try
        {
            var outputs = await AudioTransport.EnumerateOutputsAsync();
            var selected = outputs.FirstOrDefault(device => device.Id == _preferences.OutputId);
            _isOutputMissing = !string.IsNullOrEmpty(_preferences.OutputId) && selected is null;
            if (selected is not null) _preferences.OutputName = selected.Name;
        }
        catch (Exception exception) { ShowError(exception, "AudioError"); }
        _document = new() { Name = Text.Get("OpenPractice") };
        if (File.Exists(_workspace.RecoveryPath))
        {
            try
            {
                var recovered = await DocumentStore.LoadAsync(_workspace.RecoveryPath);
                await ValidateAsync(recovered);
                _document = recovered;
                ShowNotice("Recovered");
            }
            catch (Exception exception) { ShowError(exception); }
        }
        SynchronizeGain();
        ApplyTheme();
        _isReady = true;
        RebuildNavigation();
        SelectView(_preferences.LastView);
        LocalizeTransport();
        _refresh.Start();
        Diagnostics.Write("Workspace initialized; no playback or capture started");
    }

    private void RebuildNavigation()
    {
        Navigation.MenuItems.Clear();
        foreach (var key in new[] { "Play", "Studio", "Train", "Stage", "Library" })
        {
            var item = new NavigationViewItem { Content = Text.Get(key), Tag = key };
            Identify(item, "Destination" + key, Text.Get(key));
            Navigation.MenuItems.Add(item);
        }
        if (Navigation.SettingsItem is NavigationViewItem settings)
        {
            settings.Content = Text.Get("Settings");
            Identify(settings, "DestinationSettings", Text.Get("Settings"));
        }
    }

    private void SelectView(string view)
    {
        _view = view is "Play" or "Studio" or "Train" or "Stage" or "Library" or "Settings" ? view : "Play";
        Navigation.SelectedItem = _view == "Settings" ? Navigation.SettingsItem :
            Navigation.MenuItems.OfType<NavigationViewItem>().First(item => (string)item.Tag == _view);
        BuildPage();
    }

    private void BuildPage()
    {
        _instrument = null;
        _position = null;
        _stageTempo = null;
        _programStatus = null;
        _progress = null;
        _timingStatus = null;
        _tempoCurve = null;
        _libraryList = null;
        _search = null;
        PageHost.Content = _view switch
        {
            "Studio" => BuildStudio(),
            "Train" => BuildTrain(),
            "Stage" => BuildStage(),
            "Library" => BuildLibrary(),
            "Settings" => BuildSettings(),
            _ => BuildPlay()
        };
        RefreshSnapshot();
    }

    private async Task TogglePlaybackAsync()
    {
        if (_isBusy) return;
        InvalidateTiming();
        if (_transport.State == TransportState.Playing)
        {
            _transport.Pause();
            RefreshSnapshot();
            return;
        }
        _isBusy = true;
        StartButton.IsEnabled = false;
        try
        {
            var retainsRevision = _transport.State is TransportState.Paused or TransportState.Faulted;
            var startRevision = _documentRevision;
            await _transport.StartAsync(_document, _preferences.OutputId);
            if (!retainsRevision) _hasPendingEdits = startRevision != _documentRevision;
        }
        finally { _isBusy = false; StartButton.IsEnabled = true; RefreshSnapshot(); }
    }

    private async Task CommitAsync(Action<PulseDocument> edit, bool affectsPlayback = true)
    {
        if (_isLocked) { ShowNotice("Locked"); return; }
        await _edits.WaitAsync();
        try
        {
            var revised = LocalWorkspace.Clone(_document);
            edit(revised);
            await ValidateAsync(revised);
            _undo.Push(_document);
            if (_undo.Count > 64)
            {
                var recent = _undo.Take(64).Reverse().ToArray();
                _undo.Clear();
                foreach (var item in recent) _undo.Push(item);
            }
            _redo.Clear();
            _document = revised;
            _documentRevision++;
            if (affectsPlayback && _transport.State is TransportState.Playing or TransportState.Paused)
                _hasPendingEdits = true;
            if (affectsPlayback) InvalidateTiming();
            await _workspace.SaveRecoveryAsync(LocalWorkspace.Clone(_document));
        }
        finally { _edits.Release(); }
        RefreshSnapshot();
    }

    private static Task ValidateAsync(PulseDocument document) =>
        Task.Run(() => { _ = ScheduleCompiler.Compile(document, 48000, 1, false); });

    private async Task UndoAsync(bool redo)
    {
        if (_isLocked) { ShowNotice("Locked"); return; }
        await _edits.WaitAsync();
        try
        {
            var source = redo ? _redo : _undo;
            var target = redo ? _undo : _redo;
            if (source.Count == 0) return;
            target.Push(_document);
            _document = source.Pop();
            _documentRevision++;
            _hasPendingEdits = _transport.State is TransportState.Playing or TransportState.Paused;
            InvalidateTiming();
            await _workspace.SaveRecoveryAsync(LocalWorkspace.Clone(_document));
            SynchronizeGain();
        }
        finally { _edits.Release(); }
        BuildPage();
    }

    private void SynchronizeGain()
    {
        _isSynchronizingGain = true;
        try
        {
            MasterGain.Value = _document.Settings.MasterGain;
            _transport.Gain = _document.Settings.MasterGain;
        }
        finally { _isSynchronizingGain = false; }
    }

    private async Task GuardAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!_isClosed) ShowError(exception);
            else Diagnostics.Write("Action completed after window close", exception);
        }
    }

    private void ShowNotice(string key)
    {
        Notice.Severity = InfoBarSeverity.Informational;
        Notice.Title = Text.Get(key);
        Notice.Message = "";
        Notice.IsOpen = true;
    }

    private void ShowError(Exception exception, string key = "Error")
    {
        Diagnostics.Write(key, exception);
        Notice.Severity = InfoBarSeverity.Error;
        Notice.Title = Text.Get(key);
        Notice.Message = $"{ErrorText.Explain(exception)}\n{Text.Format("ErrorDiagnostics", Diagnostics.LogPath)}";
        Notice.IsOpen = true;
    }

    private void LocalizeTransport()
    {
        LabelButton(StopButton, "Stop");
        LabelButton(OutputButton, "Output");
        OutputButton.MaxWidth = 220;
        if (!string.IsNullOrEmpty(_preferences.OutputName))
        {
            OutputButton.Content = new TextBlock
            {
                Text = $"{Text.Get("Output")}\n{_preferences.OutputName}",
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 2
            };
            AutomationProperties.SetName(OutputButton, $"{Text.Get("Output")}: {_preferences.OutputName}");
        }
        Identify(MasterGain, "MasterGain", Text.Get("Volume"));
        MasterGain.Header = Text.Get("Volume");
        MasterGain.MinHeight = 44;
        AutomationProperties.SetName(Navigation, Text.Get("Destinations"));
    }

    private static void LabelButton(Button button, string key)
    {
        button.Content = new TextBlock { Text = Text.Get(key), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
        AutomationProperties.SetName(button, Text.Get(key));
    }

    private void ApplyTheme() => Root.RequestedTheme = _preferences.Theme switch
    {
        "Dark" => ElementTheme.Dark, "Light" => ElementTheme.Light, _ => ElementTheme.Default
    };

    private static void Identify(DependencyObject control, string id, string name)
    {
        AutomationProperties.SetAutomationId(control, id);
        AutomationProperties.SetName(control, name);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_isDialogOpen || !_isReady) return;
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && args.Key == VirtualKey.M)
        {
            _transport.IsMuted = !_transport.IsMuted;
            RefreshSnapshot();
            args.Handled = true;
            return;
        }
        if (ctrl && args.Key is VirtualKey.S or VirtualKey.O)
        {
            _ = GuardAsync(args.Key == VirtualKey.S ? SaveProjectAsync : OpenProjectAsync);
            args.Handled = true;
            return;
        }
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
        if (focused is TextBox or PasswordBox or NumberBox or ComboBox or Slider) return;
        // NumberBox focuses its inner TextBox; do not intercept normal typing.
        if (args.Key == VirtualKey.F && _isTimingArmed)
        {
            if (!args.KeyStatus.WasKeyDown) RecordAttack();
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Space && focused is not Button)
        {
            _ = GuardAsync(TogglePlaybackAsync);
            args.Handled = true;
        }
    }
}
