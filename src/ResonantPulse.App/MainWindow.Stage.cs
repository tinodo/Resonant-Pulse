using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private readonly StageOperationGate _stageOperations = new();
    private readonly List<Control> _stageOperationControls = [];

    private UIElement BuildStage()
    {
        _stageOperationControls.Clear();
        _stageTempo = Heading("");
        _stageTempo.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Bahnschrift");
        _stageTempo.FontSize = 96;
        _position = Body("");
        _instrument = new PulseInstrument { MaxHeight = 320 };
        var panel = Column(Heading(_document.Name), _stageTempo, _position, _instrument);
        panel.Children.Add(CommandFlow(StageControl(ActionButton(_isLocked ? "Unlock" : "Lock", "StageLock", () => RunStageOperationAsync(() =>
        {
            _isLocked = !_isLocked;
            BuildPage();
            return Task.CompletedTask;
        }, requiresUnlocked: false)))));
        var onTop = Check("AlwaysOnTop", "AlwaysOnTop", AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true });
        onTop.Checked += (_, _) => { if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = true; };
        onTop.Unchecked += (_, _) => { if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = false; };
        panel.Children.Add(onTop);
        panel.Children.Add(Heading(Text.Get("Setlist")));
        panel.Children.Add(Copy("EmptySetlist"));
        var songs = new ComboBox
        {
            Header = Text.Get("Setlist"),
            ItemsSource = _document.Setlist.Select((song, index) => new Choice(index.ToString(System.Globalization.CultureInfo.InvariantCulture), song.Name)).ToArray(),
            SelectedIndex = _armedSong >= 0 && _armedSong < _document.Setlist.Count ? _armedSong : -1,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        StageControl(songs);
        Identify(songs, "StageSetlist", Text.Get("Setlist"));
        var cue = Body(_armedSong >= 0 && _armedSong < _document.Setlist.Count
            ? Text.Format("ArmedSong", _document.Setlist[_armedSong].Name) : Text.Get("ChooseSong"));
        var notes = Input("Notes", "SongNotes", _armedSong >= 0 && _armedSong < _document.Setlist.Count ? _document.Setlist[_armedSong].Notes : "");
        notes.AcceptsReturn = true;
        notes.TextWrapping = TextWrapping.Wrap;
        notes.MinHeight = 120;
        notes.IsReadOnly = _isLocked;
        StageControl(notes);
        var isRestoringSelection = false;
        songs.SelectionChanged += async (_, _) =>
        {
            if (isRestoringSelection || songs.SelectedIndex < 0) return;
            var requestedIndex = songs.SelectedIndex;
            await GuardAsync(async () =>
            {
                if (await RunStageOperationAsync(() =>
                {
                    if (requestedIndex >= _document.Setlist.Count)
                        throw new InvalidOperationException(Text.Get("ChooseSong"));
                    _armedSong = requestedIndex;
                    BuildPage();
                    return Task.CompletedTask;
                }, requiresUnlocked: false)) return;

                // Disabling prevents user changes while busy; also restore a
                // change whose event was already queued before disabling.
                isRestoringSelection = true;
                try
                {
                    songs.SelectedIndex = _armedSong >= 0 && _armedSong < _document.Setlist.Count ? _armedSong : -1;
                }
                finally { isRestoringSelection = false; }
            });
        };
        panel.Children.Add(songs);
        panel.Children.Add(cue);
        panel.Children.Add(StageControl(ActionButton("NextCountIn", "StageNextCountIn", NextSongAsync)));
        panel.Children.Add(Copy("StageTransition"));
        panel.Children.Add(notes);
        panel.Children.Add(StageControl(ActionButton("Apply", "SaveSongNotes", () => RunStageOperationAsync(async () =>
        {
            var value = notes.Text;
            await CommitAsync(doc => doc.Setlist[GetArmedSongIndex(doc)].Notes = value, false);
        }))));
        panel.Children.Add(StageControl(ActionButton("AddSong", "AddCurrentSong", () => RunStageOperationAsync(async () =>
        {
            await CommitAsync(doc =>
            {
                var snapshot = LocalWorkspace.Clone(doc);
                snapshot.Setlist.Clear();
                doc.Setlist.Add(new SongEntry { Name = snapshot.Name, Document = snapshot, Notes = "" });
            }, false);
            BuildPage();
        }))));
        if (_armedSong >= 0 && _armedSong < _document.Setlist.Count)
        {
            panel.Children.Add(CommandFlow(
                StageControl(ActionButton("MoveUp", "SongMoveUp", () => RunStageOperationAsync(() => MoveSongAsync(-1)))),
                StageControl(ActionButton("MoveDown", "SongMoveDown", () => RunStageOperationAsync(() => MoveSongAsync(1)))),
                StageControl(ActionButton("Remove", "RemoveSong", () => RunStageOperationAsync(async () =>
                {
                    var name = _document.Setlist[GetArmedSongIndex(_document)].Name;
                    if (!await ConfirmAsync(Text.Format("ConfirmRemove", name), "Remove")) return;
                    PulseDocument? committedDocument = null;
                    try
                    {
                        await CommitAsync(doc =>
                        {
                            doc.Setlist.RemoveAt(GetArmedSongIndex(doc));
                            committedDocument = doc;
                        }, false);
                    }
                    finally
                    {
                        if (ReferenceEquals(_document, committedDocument))
                        {
                            _armedSong = -1;
                            if (!_isClosed) BuildPage();
                        }
                    }
                })))));
        }
        return Scroll(panel);
    }

    private T StageControl<T>(T control) where T : Control
    {
        _stageOperationControls.Add(control);
        control.IsEnabled = !_stageOperations.IsBusy;
        return control;
    }

    private void RefreshStageControlState()
    {
        if (_isClosed) return;
        foreach (var control in _stageOperationControls)
            control.IsEnabled = !_stageOperations.IsBusy;
    }

    private async Task<bool> RunStageOperationAsync(Func<Task> operation, bool requiresUnlocked = true)
    {
        try
        {
            var accepted = await _stageOperations.TryRunAsync(async () =>
            {
                RefreshStageControlState();
                if (_isClosed) return;
                if (requiresUnlocked && _isLocked) { ShowNotice("Locked"); return; }
                await operation();
            });
            // An overlapping request is busy, not necessarily editing-locked.
            // No stale keyboard/already-queued action is enqueued.
            if (!accepted && !_isClosed) ShowNotice("StageBusy");
            return accepted;
        }
        finally { RefreshStageControlState(); }
    }

    private int GetArmedSongIndex(PulseDocument document)
    {
        if (_armedSong < 0 || _armedSong >= document.Setlist.Count)
            throw new InvalidOperationException(Text.Get("ChooseSong"));
        return _armedSong;
    }

    private async Task MoveSongAsync(int direction)
    {
        var destination = GetArmedSongIndex(_document) + direction;
        if (destination < 0 || destination >= _document.Setlist.Count) return;
        var nextArmedSong = _armedSong;
        PulseDocument? committedDocument = null;
        try
        {
            await CommitAsync(doc =>
            {
                // Resolve only in the accepted operation, under the edit lock;
                // never retain a page-construction index across validation.
                var selectedIndex = GetArmedSongIndex(doc);
                var targetIndex = selectedIndex + direction;
                if (targetIndex < 0 || targetIndex >= doc.Setlist.Count)
                    throw new InvalidOperationException(Text.Get("ChooseSong"));
                (doc.Setlist[targetIndex], doc.Setlist[selectedIndex]) = (doc.Setlist[selectedIndex], doc.Setlist[targetIndex]);
                nextArmedSong = targetIndex;
                committedDocument = doc;
            }, false);
        }
        finally
        {
            // Recovery I/O can fail after CommitAsync publishes the edit.
            // Keep selection aligned with that publication even on failure,
            // but never move it for a rejected/unpublished edit.
            if (ReferenceEquals(_document, committedDocument))
            {
                _armedSong = nextArmedSong;
                if (!_isClosed) BuildPage();
            }
        }
    }

    // All callers, including keyboard invocation, enter the same gate. Keep
    // ownership until StartAsync has completed, not merely until publication.
    private Task NextSongAsync() => RunStageOperationAsync(StartArmedSongAsync, requiresUnlocked: false);

    private async Task StartArmedSongAsync()
    {
        if (_isBusy) return;
        if (!HasSelectedOutput)
        {
            PromptForOutput();
            return;
        }
        var intent = _transportIntent;
        _isBusy = true;
        StartButton.IsEnabled = false;
        try
        {
            await DocumentPublication.RunAsync(_edits, async published =>
            {
                var selectedIndex = GetArmedSongIndex(_document);
                var next = LocalWorkspace.Clone(_document.Setlist[selectedIndex].Document);
                await ValidateAsync(next);
                if (intent != _transportIntent || _isClosed) return;
                next.Settings.CountInBars = Math.Max(1, next.Settings.CountInBars);
                next.Setlist = LocalWorkspace.Clone(_document).Setlist;
                InvalidateTiming();
                _transport.Stop();
                _undo.Push(_document);
                _redo.Clear();
                _document = next;
                _documentRevision++;
                _editorContentRevision = EditorRevisionPolicy.Advance(_editorContentRevision);
                _hasPendingEdits = false;
                _armedSong = selectedIndex + 1 < next.Setlist.Count ? selectedIndex + 1 : -1;
                published();
                SynchronizeGain();
                await _workspace.SaveRecoveryAsync(LocalWorkspace.Clone(_document));
            }, _ => { if (!_isClosed) BuildPage(); });
        }
        finally { _isBusy = false; RefreshSnapshot(); }
        if (intent == _transportIntent && !_isClosed) await TogglePlaybackAsync();
    }
}
