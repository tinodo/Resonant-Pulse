using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    private readonly DispatcherTimer _noticeTimer = new() { Interval = TimeSpan.FromSeconds(6) };
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
    /// <summary>Counts the times the whole project was replaced (opened, new, a ready-made rhythm, the next song).</summary>
    private int _projectGeneration;
    private int _editorContentRevision;
    private int _armedSong = -1;
    private PulseInstrument? _instrument;
    private TextBlock? _position;
    private TextBlock? _stageTempo;
    private TextBlock? _stageMeter;
    private TextBlock? _programStatus;
    private ProgressBar? _progress;
    private TextBlock? _timingStatus;
    private TempoCurve? _tempoCurve;
    private ListView? _libraryList;
    private TextBox? _search;
    private IReadOnlyList<LibraryItem> _library = [];

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    /// <summary>Implemented only in local verification builds (MainWindow.UiTest.cs, -p:UiTest=true).</summary>
    partial void InstallUiTestHook();

    /// <summary>Implemented only in local verification builds: makes an edit wait before it is validated, to widen a race.</summary>
    partial void HoldEditForUiTest(ref Task? hold);

    public MainWindow()
    {
        Diagnostics.Write("MainWindow XAML initialization beginning");
        _tempoNudges = new TempoNudges(
            wanted => CommitAsync(doc => doc.Settings.Tempo = wanted, editorChange: EditorChangeKind.GlobalTempoOnly),
            GuardAsync,
            () =>
            {
                if (!_isClosed) RefreshSnapshot();
            });
        InitializeComponent();
        Diagnostics.Write("MainWindow XAML initialization completed");
        Title = "Resonant Pulse";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragTitleBar);
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App.ico");
        if (File.Exists(iconPath)) AppWindow.SetIcon(iconPath);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        Root.SizeChanged += (_, _) => UpdateTitleBarInset();
        // The Windows text size can change while the app runs. UISettings raises the event on a worker thread; the fields that
        // are sized from it (the page kit) listen for the UI-thread version.
        void OnTextScaleChanged(Windows.UI.ViewManagement.UISettings sender, object args) =>
            DispatcherQueue.TryEnqueue(RaiseTextScaleChanged);
        TextSettings.TextScaleFactorChanged += OnTextScaleChanged;
        Closed += (_, _) => TextSettings.TextScaleFactorChanged -= OnTextScaleChanged;
        _noticeTimer.Tick += (_, _) =>
        {
            _noticeTimer.Stop();
            switch (_stemExport.WhenTransientNoticeFades(Notice.Severity == InfoBarSeverity.Informational))
            {
                case FadedNotice.Close: Notice.IsOpen = false; break;
                // The notice that said the tracks are being saved was replaced by the one that has just faded.
                case FadedNotice.ShowSaving: ShowSavingNotice(); break;
            }
        };
        // A warning or an error that replaced the notice of an export that is still being written has been dismissed by the person:
        // the notice comes back. (A notice that is closed by the code, or the informational notices, are handled by the timer.)
        Notice.Closed += (_, args) =>
        {
            if (args.Reason == InfoBarCloseReason.CloseButton && Notice.Severity != InfoBarSeverity.Informational && _stemExport.IsWriting)
                ShowSavingNotice();
        };
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
            BuildPage();
        };
        StartButton.Click += async (_, _) => await GuardAsync(TogglePlaybackAsync);
        StopButton.Click += (_, _) =>
        {
            SupersedeTransportIntent();
            InvalidateTiming();
            _transport.Stop();
            _hasPendingEdits = false;
            RefreshSnapshot();
        };
        MuteButton.Click += (_, _) => { _transport.IsMuted = !_transport.IsMuted; RefreshSnapshot(); };
        TransportPanel.SizeChanged += (_, args) =>
        {
            var compact = args.NewSize.Width < 520;
            var narrow = args.NewSize.Width < 700;
            var columns = compact ? 2 : 5;
            if (TransportCommands.ColumnDefinitions.Count != columns)
            {
                TransportCommands.ColumnDefinitions.Clear();
                for (int column = 0; column < columns; column++)
                    TransportCommands.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = compact || column == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
                    });
            }
            var statusRow = compact ? 2 : narrow ? 1 : 0;
            if (TransportCommands.RowDefinitions.Count != statusRow + 1)
            {
                TransportCommands.RowDefinitions.Clear();
                for (int row = 0; row <= statusRow; row++)
                    TransportCommands.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            Grid.SetColumn(MuteButton, compact ? 0 : 2);
            Grid.SetRow(MuteButton, compact ? 1 : 0);
            Grid.SetColumn(AudioOptionsButton, compact ? 1 : 4);
            Grid.SetRow(AudioOptionsButton, compact ? 1 : 0);
            Grid.SetColumn(TransportStatus, narrow ? 0 : 3);
            Grid.SetColumnSpan(TransportStatus, compact ? 2 : narrow ? 5 : 1);
            Grid.SetRow(TransportStatus, statusRow);
            TransportCommands.RowSpacing = narrow ? 8 : 0;
        };
        OutputButton.Click += async (_, _) => await GuardAsync(RefreshOutputsAsync);
        AudioOptionsClose.Click += (_, _) => AudioOptionsFlyout.Hide();
        AudioOptionsPanel.KeyDown += OnKeyDown;
        AudioOptionsFlyout.Opening += (_, _) => SizeOptionsFlyout(AudioOptionsFlyout, AudioOptionsPanel, AudioOptionsScroll);
        AudioOptionsFlyout.Opened += (_, _) => OutputPicker.Focus(FocusState.Programmatic);
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
        Root.KeyDown += OnKeyDown;
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
        InstallUiTestHook();
        InstallCloseFlush();
        Closed += (_, _) =>
        {
            _isClosed = true;
            _refresh.Stop();
            try { StopOutputWatch(); }
            catch (Exception exception) { Diagnostics.Write("Output watch shutdown", exception); }
            RemovePowerHook(hwnd);
            _transport.Dispose();
            Diagnostics.Write($"Native window closed: {DescribeClose()}");
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
                // Your last settings simply come back; a metronome should not announce that. Failures still report below.
                _document = recovered;
            }
            catch (Exception exception) { ShowError(exception); }
        }
        SynchronizeGain();
        ApplyTheme();
        _isReady = true;
        RebuildNavigation();
        // A metronome opens on the metronome. The page left last is not restored: opening on Settings, or on a page that
        // takes a moment to build, is never what someone starting to play wants.
        SelectView("Play");
        LocalizeTransport();
        _refresh.Start();
        Diagnostics.Write("Workspace initialized; no playback or capture started");
    }

    private void RebuildNavigation()
    {
        Navigation.MenuItems.Clear();
        var icons = new Dictionary<string, string>
        {
            ["Play"] = "\uE768", ["Studio"] = "\uE9E9", ["Train"] = "\uEC4A", ["Stage"] = "\uE7F4", ["Library"] = "\uE8F1"
        };
        foreach (var key in new[] { "Play", "Studio", "Train", "Stage", "Library" })
        {
            var item = new NavigationViewItem { Content = Text.Get(key), Tag = key, Icon = new FontIcon { Glyph = icons[key] } };
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

    /// <summary>
    /// Counts how often the page was built. The Studio page, built while edits are still being applied, is built again once they
    /// are in; that second build is skipped if the page was built again (or left) in the meantime.
    /// </summary>
    private int _pageGeneration;

    private void BuildPage()
    {
        // The controls of Tap to start go with the page: a rebuilt page (another page, a language, a changed project) never keeps
        // an intent whose switch and counter it no longer shows.
        CancelTapIn();
        ReleaseTapControls();
        _pageGeneration++;
        // The notes box is about to be replaced. What was typed in it goes with the request that saves it, not with the box, so
        // a rebuild for any reason (a recovery write that failed, a language change, another page) cannot discard a draft.
        QueueStageNotesSave();
        _instrument = null;
        _beatStrip = null;
        _clickGrid = null;
        _tempoEdit = null;
        _position = null;
        _stageTempo = null;
        _stageMeter = null;
        _stageList = null;
        _stageCue = null;
        _stageNotes = null;
        _stageLockNote = null;
        _stageNotesCaption = null;
        _programStatus = null;
        _progress = null;
        _programHost = null;
        _timingStatus = null;
        _timingTarget = null;
        _timingToleranceInput = null;
        _timingSilentTargets = null;
        _timingBegin = null;
        _timingHit = null;
        _timingFinish = null;
        _timingExport = null;
        _tempoCurve = null;
        _libraryList = null;
        _search = null;
        _libraryStatus = null;
        _libraryNoMatch = null;
        _libraryEmpty = null;
        _editorOperationControls.Clear();
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
        // Starting or pausing by hand ends a Tap to start that was waiting for its taps (or for its start): the person has chosen.
        CancelTapIn();
        // While the window closes nothing new may start or resume (Stop and Mute stay available), whichever button or key asked.
        if (_isBusy || !_isReady || _closeFlushing) return;
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
            // A new start plays the project as it is after every change asked for before it. Changes are applied in turn, so
            // Start takes its place in the queue first; a change still being checked would otherwise be left out of what plays
            // and only be heard after Stop and Start. A resume keeps the revision it paused on and does not wait.
            if (!retainsRevision)
            {
                // Stop, a change of output, a power transition and a replaced project end the wait by superseding the transport
                // intent (which also cancels the wait, so Start lets go of the busy state at once, not when the queue clears).
                // Stop stays available while Start waits, so a save that stalls cannot leave a start that cannot be cancelled.
                var intent = _transportIntent;
                using var wait = new CancellationTokenSource();
                _startWait = wait;
                _startWaitingIntent = intent;
                try
                {
                    // The tempo is submitted one step at a time and the newest request waits outside the queue until the one
                    // being committed is done, so it is waited for first, as closing does.
                    await _tempoNudges.Settled.WaitAsync(wait.Token);
                    await WaitForPendingEditsAsync(wait.Token);
                }
                catch (OperationCanceledException) { return; }
                finally
                {
                    _startWait = null;
                    _startWaitingIntent = null;
                }
                // The window may have begun to close, or the start been cancelled or overtaken, while Start waited.
                if (intent != _transportIntent || _closeFlushing || _isClosed) return;
                if (!HasSelectedOutput)
                {
                    PromptForOutput();
                    return;
                }
            }
            var startRevision = _documentRevision;
            await _transport.StartAsync(_document, _preferences.OutputId);
            if (!retainsRevision) _hasPendingEdits = startRevision != _documentRevision;
        }
        finally { _isBusy = false; RefreshSnapshot(); }
    }

    /// <summary>The transport intent of a Start that waits for the edits asked for before it, while it waits.</summary>
    private int? _startWaitingIntent;

    /// <summary>Cancels the wait of a Start that waits for earlier edits, while it waits.</summary>
    private CancellationTokenSource? _startWait;

    /// <summary>True while a Start waits for earlier edits and nothing has cancelled it: Stop is available then, and ends the wait.</summary>
    private bool IsStartPending => _startWaitingIntent is { } waiting && waiting == _transportIntent;

    /// <summary>
    /// Ends whatever the transport was about to do. A Start that waits for earlier edits is one such thing: it checks the intent
    /// when it wakes, and its wait is cancelled so that it lets go of the busy state at once.
    /// </summary>
    private void SupersedeTransportIntent()
    {
        _transportIntent++;
        _startWait?.Cancel();
        // Tap to start is intent to start too: whatever overtakes the transport's intent (Stop, a change of output, sleep, a replaced
        // project) ends it, so none of those can forget it.
        CancelTapIn();
    }

    /// <summary>
    /// Returns once every edit asked for before this call has been applied (the edit lock is handed out in the order it was
    /// asked for). <paramref name="cancel"/> ends the wait without taking the lock.
    /// </summary>
    private async Task WaitForPendingEditsAsync(CancellationToken cancel = default)
    {
        await _edits.WaitAsync(cancel);
        _edits.Release();
    }

    private async Task CommitAsync(Action<PulseDocument> edit, bool affectsPlayback = true,
        int? expectedRevision = null, EditorChangeKind editorChange = EditorChangeKind.Document, string? coalesceKey = null,
        Action? onPublished = null, Func<bool>? stillWanted = null, CancellationToken cancel = default)
    {
        // When the person made the change, not when the queue reaches it: a slow validation or recovery write must not split quick
        // changes into several Undo steps, and a queue that drains must not join changes that were made seconds apart.
        var madeAt = DateTime.UtcNow;
        if (_isLocked) { ShowNotice("Locked"); return; }
        await DocumentPublication.RunAsync(_edits, async published =>
        {
            if (_isLocked) { ShowNotice("Locked"); return; }
            if (expectedRevision is int revision)
                EditorOperationGate.EnsureRevision(revision, _editorContentRevision);
            var revised = LocalWorkspace.Clone(_document);
            edit(revised);
            Task? hold = null;
            HoldEditForUiTest(ref hold);
            if (hold is not null) await hold;
            await ValidateAsync(revised);
            // Live-eligible edits follow the running transport (sound at the next quantum, tempo/meter/feel at the
            // next pulse). This happens before the document is replaced so a failed update changes nothing.
            var live = LiveEditOutcome.NotPlaying;
            var countInChanged = false;
            if (LiveEditPolicy.IsLive(editorChange))
            {
                // Stop (or a restart) during the off-thread compile only means the edit has nothing to follow:
                // it is still a valid document change, so keep it instead of reporting an error.
                try { live = await _transport.UpdateLiveAsync(revised.Settings); }
                catch (LiveSessionChangedException) { live = LiveEditOutcome.NotPlaying; }
                countInChanged = _transport.Snapshot().Revision is { } audible &&
                    audible.Document.Settings.CountInBars != revised.Settings.CountInBars;
            }
            // A change that was asked for by something that has been cancelled since (a Tap to start that Stop ended while its
            // tempo was being checked) leaves nothing behind: no Undo step, no new project, no save. Nothing is awaited between
            // this check and the publication below, so the answer cannot change in between.
            if (stillWanted is not null && !stillWanted()) throw new OperationCanceledException();
            RecordUndo(coalesceKey, madeAt);
            _document = revised;
            _recoveryUnsaved = true;
            _documentRevision++;
            _editorContentRevision = EditorRevisionPolicy.Advance(_editorContentRevision, editorChange);
            if (LiveEditPolicy.LeavesPendingEdit(editorChange, affectsPlayback,
                    _transport.State is TransportState.Playing or TransportState.Paused or TransportState.Faulted,
                    live, countInChanged))
                _hasPendingEdits = true;
            // Edits that cannot follow live are pending until Stop + Start. The immutable active
            // revision and a measurement against it have not changed.
            published();
            // From here the project holds the edit, whatever happens to the recovery file next.
            onPublished?.Invoke();
            await SaveRecoveryAsync();
        }, succeeded =>
        {
            if (_isClosed) return;
            // A failed recovery save still reports its error, but an independent
            // global scalar change must not discard Studio drafts or focus.
            if (!succeeded) SynchronizeGain();
            if (EditorRevisionPolicy.ShouldRebuild(succeeded, editorChange)) BuildPage();
            else RefreshSnapshot();
            // Undo and Redo become available (or not) with every change.
            RefreshEditorControlState();
        }, cancel);
    }

    /// <summary>True from the moment the project changes until the recovery file has been written with that change.</summary>
    private bool _recoveryUnsaved;

    /// <summary>
    /// Writes the project to the recovery file. Until a write has succeeded the file is behind the project; closing the window
    /// checks that, writes again, and stays open if that fails too. A write of an older snapshot that finishes after a newer
    /// change leaves the file marked behind.
    /// </summary>
    private async Task SaveRecoveryAsync()
    {
        var revision = _documentRevision;
        await _workspace.SaveRecoveryAsync(LocalWorkspace.Clone(_document));
        if (revision == _documentRevision) _recoveryUnsaved = false;
    }

    private string? _undoKey;
    private DateTime _undoKeyAt;

    /// <summary>
    /// Keeps the project as it is now so Undo can come back to it. An edit that continues a burst on one field (the same
    /// <paramref name="coalesceKey"/>, soon after the last) joins that burst's undo step instead of adding its own, so
    /// nudging a value ten times is one step back, not ten. "Soon after" is measured between the moments the changes were
    /// made (<paramref name="madeAt"/>), not the moments the queue applied them. Called with the edit queue held.
    /// </summary>
    private void RecordUndo(string? coalesceKey = null, DateTime? madeAt = null)
    {
        var now = madeAt ?? DateTime.UtcNow;
        var joinsBurst = _undo.Count > 0 && UndoCoalescing.ShouldMerge(_undoKey, coalesceKey, _undoKeyAt, now);
        _undoKey = coalesceKey;
        _undoKeyAt = now;
        _redo.Clear();
        if (joinsBurst) return;
        _undo.Push(_document);
        if (_undo.Count <= 64) return;
        var recent = _undo.Take(64).Reverse().ToArray();
        _undo.Clear();
        foreach (var item in recent) _undo.Push(item);
    }

    private static Task ValidateAsync(PulseDocument document) =>
        Task.Run(() => { _ = ScheduleCompiler.Compile(document, 48000, 1, false); });

    private async Task UndoAsync(bool redo, int? expectedRevision = null)
    {
        if (_isLocked) { ShowNotice("Locked"); return; }
        // Notes typed or on their way belong to the project as it is now. They are saved first, in order, so the undo acts on the
        // latest completed edit and no save that was queued behind it can land on top of its result (and clear what Redo holds).
        if (!await FlushStageNotesAsync()) return;
        await DocumentPublication.RunAsync(_edits, async published =>
        {
            if (_isLocked) { ShowNotice("Locked"); return; }
            if (expectedRevision is int revision)
                EditorOperationGate.EnsureRevision(revision, _editorContentRevision);
            var source = redo ? _redo : _undo;
            var target = redo ? _undo : _redo;
            if (source.Count == 0) return;
            // The next edit starts a step of its own, even on the field that was just undone.
            _undoKey = null;
            target.Push(_document);
            _document = source.Pop();
            // The setlist may be shorter now; the song that was up next may not exist any more.
            if (_armedSong >= _document.Setlist.Count) _armedSong = -1;
            _recoveryUnsaved = true;
            _documentRevision++;
            _editorContentRevision = EditorRevisionPolicy.Advance(_editorContentRevision);
            _hasPendingEdits = _transport.State is TransportState.Playing or TransportState.Paused or TransportState.Faulted;
            published();
            SynchronizeGain();
            await SaveRecoveryAsync();
        }, _ => { if (!_isClosed) RebuildPreservingScroll(); });
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

    private void ShowNotice(string key) => ShowNoticeText(Text.Get(key));

    private void ShowNoticeText(string text)
    {
        Notice.Severity = InfoBarSeverity.Informational;
        Notice.Title = text;
        Notice.Message = "";
        Notice.IsOpen = true;
        // Informational notices (restored session, saved) fade away; errors stay until dismissed.
        _noticeTimer.Stop();
        _noticeTimer.Start();
    }

    private void ShowError(Exception exception, string key = "Error")
    {
        Diagnostics.Write(key, exception);
        _noticeTimer.Stop();
        Notice.Severity = InfoBarSeverity.Error;
        Notice.Title = Text.Get(key);
        Notice.Message = exception is UserMessageException
            ? ErrorText.Explain(exception)
            : $"{ErrorText.Explain(exception)}\n{Text.Format("ErrorDiagnostics", Diagnostics.LogPath)}";
        Notice.IsOpen = true;
    }

    private void LocalizeTransport()
    {
        LabelButton(StopButton, "Stop", "\uE71A");
        LabelButton(AudioOptionsButton, "AudioOptions", "\uE7F5");
        LabelButton(AudioOptionsClose, "CloseOptions");
        AudioOptionsTitle.Text = Text.Get("AudioOptions");
        ToolTipService.SetToolTip(AudioOptionsButton, Text.Get("AudioOptionsHelp"));
        AutomationProperties.SetHelpText(AudioOptionsButton, Text.Get("AudioOptionsHelp"));
        OutputPicker.Header = null;
        OutputCaption.Text = Text.Get("Output");
        OutputNote.Text = Text.Get("AudioNote");
        OutputPicker.PlaceholderText = Text.Get("ChooseOutput");
        Identify(OutputPicker, "AudioOutputPicker", Text.Get("Output"));
        AutomationProperties.SetName(OutputButton, Text.Get("RefreshDevices"));
        ToolTipService.SetToolTip(OutputButton, Text.Get("RefreshDevices"));
        Identify(MasterGain, "MasterGain", Text.Get("Volume"));
        MasterGain.Header = Text.Get("Volume");
        MasterGain.MinHeight = 44;
        AutomationProperties.SetName(Navigation, Text.Get("Destinations"));
    }

    private static void LabelButton(ButtonBase button, string key, string? glyph = null)
    {
        FrameworkElement content = new TextBlock
        {
            Text = Text.Get(key), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (glyph is not null)
        {
            // The label has a star column of its own, so in a button narrower than icon + label (a narrow window, a large text
            // size) it wraps inside the button instead of being clipped. At its natural width nothing changes.
            // The gap between icon and label belongs to the label (a margin), so a label that is hidden leaves no gap.
            var row = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            content.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            content = row;
        }
        button.Content = content;
        AutomationProperties.SetName(button, Text.Get(key));
    }

    private void ApplyTheme()
    {
        Root.RequestedTheme = _preferences.Theme switch
        {
            "Dark" => ElementTheme.Dark, "Light" => ElementTheme.Light, _ => ElementTheme.Default
        };
        ApplyCaptionTheme();
    }

    private void UpdateTitleBarInset()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        CaptionInset.Width = new GridLength(AppWindow.TitleBar.RightInset / Math.Max(1.0, scale));
    }

    private void ApplyCaptionTheme()
    {
        var bar = AppWindow.TitleBar;
        var palette = CaptionPalettes.Choose(ThemeBrushes.IsHighContrast, Root.ActualTheme == ElementTheme.Light);
        if (palette == CaptionPalette.System)
        {
            // A null color restores the system default, which a Contrast theme pairs legibly.
            bar.ButtonForegroundColor = null;
            bar.ButtonInactiveForegroundColor = null;
            bar.ButtonBackgroundColor = null;
            bar.ButtonInactiveBackgroundColor = null;
            return;
        }
        var foreground = palette == CaptionPalette.DarkOnLight ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White;
        bar.ButtonForegroundColor = foreground;
        bar.ButtonInactiveForegroundColor = foreground;
        bar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
    }

    private static void Identify(DependencyObject control, string id, string name)
    {
        AutomationProperties.SetAutomationId(control, id);
        AutomationProperties.SetName(control, name);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (_isDialogOpen || !_isReady) return;
        // Esc ends Tap to start, unless a panel is open: Esc closes that first, as it always does.
        if (args.Key == VirtualKey.Escape && IsTapStartOn && !HasOpenPanel())
        {
            CancelTapIn();
            args.Handled = true;
            return;
        }
        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (ctrl && args.Key == VirtualKey.M)
        {
            _transport.IsMuted = !_transport.IsMuted;
            RefreshSnapshot();
            args.Handled = true;
            return;
        }
        // While the window is closing nothing new is started: not a save or an open, not playback.
        if (_closeFlushing) return;
        if (ctrl && args.Key is VirtualKey.S or VirtualKey.O)
        {
            _ = GuardAsync(args.Key == VirtualKey.S ? SaveProjectAsync : OpenProjectAsync);
            args.Handled = true;
            return;
        }
        var focused = FocusManager.GetFocusedElement(Root.XamlRoot);
        if (focused is TextBox or PasswordBox or NumberBox or ComboBox or Slider) return;
        // NumberBox focuses its inner TextBox; do not intercept normal typing.
        if (!ctrl && args.Key == VirtualKey.T && _view == "Play" && _tapOnce is not null && !HasOpenPanel())
        {
            // The key repeats while it is held down; a held key is one tap, not a run of them.
            if (!args.KeyStatus.WasKeyDown) _tapOnce();
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
