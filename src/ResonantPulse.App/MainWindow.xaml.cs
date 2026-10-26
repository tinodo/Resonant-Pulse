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
    private int _editorContentRevision;
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
        Diagnostics.Write("MainWindow XAML initialization beginning");
        InitializeComponent();
        Diagnostics.Write("MainWindow XAML initialization completed");
        Title = "Resonant Pulse";
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1 : dpi / 96.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        // AppWindow/DisplayArea coordinates are physical pixels. Resizing alone
        // leaves the default position offset and can push the far edges outside
        // the work area, even when width/height individually fit.
        var inset = Math.Min((int)Math.Round(8 * scale),
            Math.Max(0, Math.Min(area.Width, area.Height) / 20));
        var availableWidth = Math.Max(1, area.Width - 2 * inset);
        var availableHeight = Math.Max(1, area.Height - 2 * inset);
        var width = Math.Min(availableWidth, (int)Math.Round(1040 * scale));
        var height = Math.Min(availableHeight, (int)Math.Round(780 * scale));
        var bounds = new RectInt32(area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2, width, height);
        AppWindow.MoveAndResize(bounds);
        Diagnostics.Write($"Initial physical window bounds: {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}; work area: {area.X},{area.Y} {area.Width}x{area.Height}; DPI: {dpi}");
        Navigation.SelectionChanged += (sender, args) =>
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
            CancelTapIn();
            _transportIntent++;
            InvalidateTiming();
            _transport.Stop();
            _hasPendingEdits = false;
            RefreshSnapshot();
        };
        MuteButton.Click += (_, _) => { _transport.IsMuted = !_transport.IsMuted; RefreshSnapshot(); };
        OutputButton.Click += async (_, _) => await GuardAsync(RefreshOutputsAsync);
        OutputPicker.SelectionChanged += async (_, _) =>
        {
            if (!_isRefreshingOutputs && OutputPicker.SelectedItem is OutputDevice device)
                await GuardAsync(() => SelectOutputAsync(device));
        };
        OutputRow.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 600;
            OutputRow.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(220);
            Grid.SetColumn(MasterGain, narrow ? 0 : 1);
            Grid.SetRow(MasterGain, narrow ? 1 : 0);
        };
        MasterGain.ValueChanged += (_, args) =>
        {
            _transport.Gain = (float)args.NewValue;
            if (_isReady && !_isSynchronizingGain)
                _ = GuardAsync(() => CommitAsync(doc => doc.Settings.MasterGain = (float)args.NewValue,
                    false, editorChange: EditorChangeKind.MasterGainOnly));
        };
        Root.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
        Root.ActualThemeChanged += (_, _) => RefreshInstrumentTheme();
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
            CancelTapIn();
            _refresh.Stop();
            try { StopOutputMonitoring(); }
            catch (Exception exception) { Diagnostics.Write("Output monitor shutdown", exception); }
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
            await RefreshOutputsAsync();
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
        foreach (var key in new[] { "Play", "Studio", "Train", "Stage", "Library", "Help" })
        {
            var label = Text.Get(key == "Help" ? "HelpDestination" : key);
            var item = new NavigationViewItem { Content = label, Tag = key };
            Identify(item, "Destination" + key, label);
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
        _view = view is "Play" or "Studio" or "Train" or "Stage" or "Library" or "Help" or "Settings" ? view : "Play";
        Navigation.SelectedItem = _view == "Settings" ? Navigation.SettingsItem :
            Navigation.MenuItems.OfType<NavigationViewItem>().First(item => (string)item.Tag == _view);
        BuildPage();
    }

    private void BuildPage()
    {
        CancelTapIn();
        _tapStatus = null;
        _tapInArm = _tapInCancel = null;
        _tapInCount = _playTempo = null;
        _instrument = null;
        _position = null;
        _stageTempo = null;
        _programStatus = null;
        _progress = null;
        _timingStatus = null;
        _timingTarget = null;
        _timingToleranceInput = null;
        _timingSilentTargets = null;
        _tempoCurve = null;
        _libraryList = null;
        _search = null;
        PageHost.Content = _view switch
        {
            "Studio" => BuildStudio(),
            "Train" => BuildTrain(),
            "Stage" => BuildStage(),
            "Library" => BuildLibrary(),
            "Help" => BuildHelp(),
            "Settings" => BuildSettings(),
            _ => BuildPlay()
        };
        RefreshSnapshot();
    }

    private async Task TogglePlaybackAsync()
    {
        CancelTapIn();
        if (_isBusy || !_isReady) return;
        InvalidateTiming();
        if (_transport.State == TransportState.Playing)
        {
            _transport.Pause();
            RefreshSnapshot();
            return;
        }
        if (!HasSelectedOutput)
        {
            PromptForOutput();
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
        finally { _isBusy = false; RefreshSnapshot(); }
    }

    private async Task CommitAsync(Action<PulseDocument> edit, bool affectsPlayback = true,
        int? expectedRevision = null, EditorChangeKind editorChange = EditorChangeKind.Document)
    {
        if (_isLocked) { ShowNotice("Locked"); return; }
        await DocumentPublication.RunAsync(_edits, async published =>
        {
            if (_isLocked) { ShowNotice("Locked"); return; }
            if (expectedRevision is int revision)
                EditorOperationGate.EnsureRevision(revision, _editorContentRevision);
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
            _editorContentRevision = EditorRevisionPolicy.Advance(_editorContentRevision, editorChange);
            if (affectsPlayback && _transport.State is TransportState.Playing or TransportState.Paused or TransportState.Faulted)
                _hasPendingEdits = true;
            // Edits are pending until Stop + Start. The immutable active
            // revision and a measurement against it have not changed.
            published();
            await _workspace.SaveRecoveryAsync(LocalWorkspace.Clone(_document));
        }, succeeded =>
        {
            if (_isClosed) return;
            // A failed recovery save still reports its error, but an independent
            // global scalar change must not discard Studio drafts or focus.
            if (!succeeded) SynchronizeGain();
            if (EditorRevisionPolicy.ShouldRebuild(succeeded, editorChange)) BuildPage();
            else RefreshSnapshot();
        });
    }

    private static Task ValidateAsync(PulseDocument document) =>
        Task.Run(() => { _ = ScheduleCompiler.Compile(document, 48000, 1, false); });

    private async Task UndoAsync(bool redo, int? expectedRevision = null)
    {
        if (_isLocked) { ShowNotice("Locked"); return; }
        await DocumentPublication.RunAsync(_edits, async published =>
        {
            if (_isLocked) { ShowNotice("Locked"); return; }
            if (expectedRevision is int revision)
                EditorOperationGate.EnsureRevision(revision, _editorContentRevision);
            var source = redo ? _redo : _undo;
            var target = redo ? _undo : _redo;
            if (source.Count == 0) return;
            target.Push(_document);
            _document = source.Pop();
            _documentRevision++;
            _editorContentRevision = EditorRevisionPolicy.Advance(_editorContentRevision);
            _hasPendingEdits = _transport.State is TransportState.Playing or TransportState.Paused or TransportState.Faulted;
            published();
            SynchronizeGain();
            await _workspace.SaveRecoveryAsync(LocalWorkspace.Clone(_document));
        }, _ => { if (!_isClosed) BuildPage(); });
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
        OutputPicker.Header = Text.Get("Output");
        OutputPicker.PlaceholderText = Text.Get("ChooseOutput");
        Identify(OutputPicker, "AudioOutputPicker", Text.Get("Output"));
        AutomationProperties.SetName(OutputButton, Text.Get("RefreshDevices"));
        ToolTipService.SetToolTip(OutputButton, Text.Get("RefreshDevices"));
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
        if (args.Key == VirtualKey.Escape && (_tapIn.IsArmed || _tapIn.IsPending))
        {
            CancelTapIn();
            args.Handled = true;
            return;
        }
        if (args.Handled) return;
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
        if (!ctrl && args.Key == VirtualKey.T && _view == "Play")
        {
            if (!args.KeyStatus.WasKeyDown) _ = GuardAsync(TapTempoAsync);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.F && _isTimingArmed)
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
