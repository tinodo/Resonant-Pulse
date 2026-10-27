using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private readonly StageOperationGate _stageOperations = new();
    private readonly List<(Control Control, Func<bool>? When)> _stageOperationControls = [];
    private readonly List<TextBlock> _stageBadges = [];
    private ListView? _stageList;
    private TextBlock? _stageCue;
    private TextBox? _stageNotes;
    private TextBlock? _stageNotesCaption;
    private TextBlock? _stageLockNote;
    private bool _stageSyncing;
    private readonly StageNotesDraft _stageNotesDraft = new();
    private readonly SaveQueue _stageNotesSaves = new();

    /// <summary>The most characters the project accepts in a song's notes; the box stops typing there.</summary>
    private const int StageNotesLimit = 32768;

    private bool HasArmedSong => _armedSong >= 0 && _armedSong < _document.Setlist.Count;

    /// <summary>
    /// Now playing (the loaded project, large and steady) beside the setlist. The song selected in the setlist is the one that
    /// plays next, and the page says so in words; selecting it never interrupts playback.
    /// </summary>
    private UIElement BuildStage()
    {
        _stageOperationControls.Clear();
        _stageBadges.Clear();
        _stageNotesDraft.Reset();

        // ---- Now playing -----------------------------------------------------------------------------------------------
        var tempo = Heading("");
        tempo.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Bahnschrift");
        tempo.FontSize = 96;
        var meter = Body("");
        meter.FontSize = 22;
        var position = Body("");
        // A stage display is one centred composition: the readouts line up with the dial beneath them.
        foreach (var readout in new[] { tempo, meter, position })
        {
            readout.HorizontalAlignment = HorizontalAlignment.Center;
            readout.TextAlignment = TextAlignment.Center;
        }
        _stageTempo = tempo;
        _stageMeter = meter;
        _position = position;
        _instrument = new PulseInstrument { MaxHeight = 320, ShowReadout = false };
        var nowCaption = Note("NowPlaying");
        nowCaption.HorizontalAlignment = HorizontalAlignment.Center;
        var projectName = new TextBlock
        {
            Text = _document.Name, Style = ResonantStyle("CardTitleStyle"), HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
        };
        _stageCue = new TextBlock
        {
            Style = ResonantStyle("BodyStyle"), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center
        };
        AutomationProperties.SetAutomationId(_stageCue, "StageUpNext");
        var next = StageControl(ActionButton("NextCountIn", "StageNextCountIn", NextSongAsync, "\uE893", ButtonKind.Primary),
            () => HasArmedSong);
        next.HorizontalAlignment = HorizontalAlignment.Center;
        var transition = Note("StageTransition");
        transition.TextAlignment = TextAlignment.Center;
        // The performer's key action stays on the first screen: what plays next and the button that starts it come straight
        // after the readouts, and the dial (the visual pulse) takes whatever room is left below them.
        var nowPlaying = Card(null, new StackPanel
        {
            Spacing = 8,
            Children = { nowCaption, projectName, tempo, meter, position, _stageCue, next, transition, _instrument }
        });
        // A card without a title is named for what it shows, so screen readers (and the checks) can find it.
        Identify(nowPlaying, "NowPlayingCard", Text.Get("NowPlaying"));

        // ---- Setlist ---------------------------------------------------------------------------------------------------
        var songs = _document.Setlist;
        var add = StageControl(ActionButton("AddSong", "AddCurrentSong", () => RunStageOperationAsync(async () =>
        {
            await CommitAsync(doc =>
            {
                var snapshot = LocalWorkspace.Clone(doc);
                snapshot.Setlist.Clear();
                doc.Setlist.Add(new SongEntry { Name = snapshot.Name, Document = snapshot, Notes = "" });
            }, false);
            // The first song added is the one that plays next; later additions never change that.
            if (!HasArmedSong) _armedSong = _document.Setlist.Count - 1;
            RebuildPreservingScroll();
        }), "\uE710"), () => !_isLocked);

        UIElement setlistBody;
        if (songs.Count == 0)
        {
            _stageList = null;
            _stageNotes = null;
            _stageNotesCaption = null;
            setlistBody = EmptyState("\uE7F4", "SetlistEmptyTitle", "EmptySetlist", add);
        }
        else
        {
            _stageList = new ListView { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 320 };
            Identify(_stageList, "StageSetlist", Text.Get("Setlist"));
            for (var index = 0; index < songs.Count; index++)
            {
                var number = new TextBlock
                {
                    Text = (index + 1).ToString(Text.Culture), Style = ResonantStyle("NoteStyle"), MinWidth = 20,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var name = new TextBlock
                {
                    Text = songs[index].Name, Style = ResonantStyle("BodyStyle"), FontSize = 14,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis
                };
                var badge = new TextBlock
                {
                    Text = Text.Get("UpNextBadge"), Style = ResonantStyle("NoteStyle"), FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed
                };
                _stageBadges.Add(badge);
                var row = new Grid { ColumnSpacing = 12 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetColumn(name, 1);
                Grid.SetColumn(badge, 2);
                row.Children.Add(number);
                row.Children.Add(name);
                row.Children.Add(badge);
                var item = new ListViewItem { Content = row, MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Stretch };
                Identify(item, $"StageSong{index}", songs[index].Name);
                _stageList.Items.Add(item);
            }
            _stageList.SelectionChanged += async (_, _) =>
            {
                if (_stageSyncing || _stageList is null || _stageList.SelectedIndex < 0) return;
                var requested = _stageList.SelectedIndex;
                await GuardAsync(async () =>
                {
                    var accepted = await RunStageOperationAsync(() =>
                    {
                        _armedSong = requested;
                        RefreshStageSelection();
                        return Task.CompletedTask;
                    }, requiresUnlocked: false);
                    // A selection that arrived while another operation was running is put back to the real one.
                    if (!accepted) RefreshStageSelection();
                });
            };

            var up = StageControl(IconButton("\uE74A", "MoveUp", "SongMoveUp", () => RunStageOperationAsync(() => MoveSongAsync(-1))),
                () => !_isLocked && _armedSong > 0);
            var down = StageControl(IconButton("\uE74B", "MoveDown", "SongMoveDown", () => RunStageOperationAsync(() => MoveSongAsync(1))),
                () => !_isLocked && HasArmedSong && _armedSong < _document.Setlist.Count - 1);
            var remove = StageControl(ActionButton("Remove", "RemoveSong", () => RunStageOperationAsync(async () =>
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
                        // The next song takes the place of the one removed; at the end of the list, the one before it.
                        _armedSong = _document.Setlist.Count == 0 ? -1 : Math.Min(_armedSong, _document.Setlist.Count - 1);
                        if (!_isClosed) RebuildPreservingScroll();
                    }
                }
            }), "\uE74D", ButtonKind.Tertiary), () => !_isLocked && HasArmedSong);

            _stageNotesCaption = new TextBlock { Style = ResonantStyle("FieldCaptionStyle"), Margin = new Thickness(0, 0, 0, 4) };
            // The box is disabled while a Stage operation runs, so a draft typed in it cannot meet a setlist that is being
            // changed under it.
            _stageNotes = StageControl(new TextBox
            {
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 110, MaxHeight = 220,
                MaxLength = StageNotesLimit, HorizontalAlignment = HorizontalAlignment.Stretch
            }, () => HasArmedSong);
            Identify(_stageNotes, "SongNotes", Text.Get("Notes"));
            AutomationProperties.SetLabeledBy(_stageNotes, _stageNotesCaption);
            // Notes are saved when the box loses focus (or Esc puts back what was saved), never by a separate button. A save is
            // queued behind the ones before it, so a second draft is never refused because the first is still being saved.
            _stageNotes.LostFocus += (sender, _) =>
            {
                // A box that belongs to a page that has been replaced no longer speaks for the notes.
                if (ReferenceEquals(sender, _stageNotes)) QueueStageNotesSave();
            };
            _stageNotes.KeyDown += (_, args) =>
            {
                if (args.Key != Windows.System.VirtualKey.Escape || _stageNotes is null) return;
                _stageNotes.Text = _stageNotesDraft.Restore();
                args.Handled = true;
            };
            setlistBody = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    _stageList,
                    CommandRow(add, up, down, remove),
                    new StackPanel { Children = { _stageNotesCaption, _stageNotes } },
                    Note("NotesSaved")
                }
            };
        }
        _stageLockNote = Note("LockHelp");
        _stageLockNote.Visibility = _isLocked ? Visibility.Visible : Visibility.Collapsed;
        var setlist = Card("Setlist", new StackPanel { Spacing = 12, Children = { setlistBody, _stageLockNote } }, "SetlistHelp",
            id: "SetlistCard");

        // ---- Page ------------------------------------------------------------------------------------------------------
        var lockToggle = StageControl(Toggle("Lock", "StageLock", _isLocked));
        AutomationProperties.SetHelpText(lockToggle, Text.Get("LockHelp"));
        lockToggle.Toggled += async (_, _) =>
        {
            if (_stageSyncing) return;
            var requested = lockToggle.IsOn;
            await GuardAsync(async () =>
            {
                var accepted = await RunStageOperationAsync(() =>
                {
                    _isLocked = requested;
                    RefreshStageSelection();
                    return Task.CompletedTask;
                }, requiresUnlocked: false);
                if (accepted) return;
                _stageSyncing = true;
                lockToggle.IsOn = _isLocked;
                _stageSyncing = false;
            });
        };
        var onTop = Toggle("AlwaysOnTop", "AlwaysOnTop", AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true });
        onTop.Toggled += (_, _) =>
        {
            if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsAlwaysOnTop = onTop.IsOn;
        };
        // What the lock does is a tooltip on the switch; the sentence is shown in the setlist card only while the lock is on,
        // where it explains why the setlist commands are greyed out.
        ToolTipService.SetToolTip(lockToggle, Text.Get("LockHelp"));
        var switches = Wrapped(24, 8, [LabeledSwitch("Lock", lockToggle), LabeledSwitch("AlwaysOnTop", onTop)]);

        var page = PageScaffold("Stage", "StagePurpose", switches, TwoPane(nowPlaying, setlist));
        RefreshStageSelection();
        return page;
    }

    /// <summary>The song that plays next is the selected one; everything that shows it is brought in line, without a rebuild.</summary>
    private void RefreshStageSelection()
    {
        var armed = HasArmedSong ? _armedSong : -1;
        _stageSyncing = true;
        try
        {
            if (_stageList is not null)
            {
                _stageList.SelectedIndex = armed;
                for (var index = 0; index < _stageBadges.Count; index++)
                {
                    _stageBadges[index].Visibility = index == armed ? Visibility.Visible : Visibility.Collapsed;
                    if (_stageList.Items[index] is ListViewItem item)
                        AutomationProperties.SetName(item, index == armed
                            ? $"{_document.Setlist[index].Name}, {Text.Get("UpNextBadge")}" : _document.Setlist[index].Name);
                }
            }
            if (_stageCue is not null)
            {
                _stageCue.Text = armed >= 0 ? Text.Format("ArmedSong", _document.Setlist[armed].Name) : Text.Get("NothingUpNext");
                AutomationProperties.SetName(_stageCue, _stageCue.Text);
            }
            if (_stageNotes is not null)
            {
                // Only a different song loads its saved notes. The same song (a refused selection put back, the lock switched)
                // keeps the draft in the box.
                if (_stageNotesDraft.Show(armed, armed >= 0 ? _document.Setlist[armed].Notes : "") is { } shown)
                    _stageNotes.Text = shown;
                _stageNotes.IsReadOnly = _isLocked;
            }
            if (_stageNotesCaption is not null)
                _stageNotesCaption.Text = armed >= 0 ? Text.Format("NotesFor", _document.Setlist[armed].Name) : Text.Get("Notes");
            if (_stageLockNote is not null) _stageLockNote.Visibility = _isLocked ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _stageSyncing = false; }
        RefreshStageControlState();
    }

    /// <summary>
    /// Asks for what is in the notes box to be saved, if it is not what the project holds or what is already on its way. Saves
    /// are applied in the order they were asked for, none is dropped because another is running, and each is for the song the
    /// box showed when it was asked for. A save that fails leaves the draft in the box and says why.
    /// </summary>
    private void QueueStageNotesSave()
    {
        if (_stageNotes is null || _isClosed) return;
        var text = _stageNotes.Text;
        if (!_stageNotesDraft.NeedsSave(text)) return;
        var song = _stageNotesDraft.Request(text);
        var generation = _projectGeneration;
        _ = _stageNotesSaves.Enqueue(async () =>
        {
            // The project holds the notes once the revised project is published, even if writing the recovery file fails
            // afterwards (that is reported, and written again when the window closes). A save that was refused (too long, a
            // locked project, a project that was replaced) never gets there, and its draft stays unsaved.
            var published = false;
            await GuardAsync(() => CommitAsync(doc =>
            {
                // Queued notes belong to the project they were typed in.
                if (generation != _projectGeneration) throw new InvalidOperationException(Text.Get("NotesProjectChanged"));
                if (song >= doc.Setlist.Count) throw new InvalidOperationException(Text.Get("ChooseSong"));
                doc.Setlist[song].Notes = text;
            }, false, onPublished: () => published = true));
            if (published) _stageNotesDraft.Completed(song, text);
            else _stageNotesDraft.Failed(song, text);
        });
    }

    /// <summary>
    /// Queues the draft in the notes box and waits until every notes save has finished. False when the draft could not be
    /// saved: the error has been shown and the draft is still in the box.
    /// </summary>
    private async Task<bool> FlushStageNotesAsync()
    {
        QueueStageNotesSave();
        await _stageNotesSaves.WhenIdleAsync();
        return _stageNotes is null || !_stageNotesDraft.NeedsSave(_stageNotes.Text);
    }

    private T StageControl<T>(T control, Func<bool>? when = null) where T : Control
    {
        _stageOperationControls.Add((control, when));
        control.IsEnabled = !_stageOperations.IsBusy && (when?.Invoke() ?? true);
        return control;
    }

    private void RefreshStageControlState()
    {
        if (_isClosed) return;
        foreach (var (control, when) in _stageOperationControls)
            control.IsEnabled = !_stageOperations.IsBusy && (when?.Invoke() ?? true);
    }

    private async Task<bool> RunStageOperationAsync(Func<Task> operation, bool requiresUnlocked = true)
    {
        // True only when the operation ran: not when another was running, the project was locked, or the notes could not be saved.
        var ran = false;
        try
        {
            var accepted = await _stageOperations.TryRunAsync(async () =>
            {
                RefreshStageControlState();
                if (_isClosed) return;
                // What was typed in the notes belongs to the song it was typed for. It is saved, in order, before anything
                // selects, moves or removes a song. If it cannot be saved the operation does not run, so the draft stays where
                // it is and the song it belongs to stays selected.
                if (!await FlushStageNotesAsync()) return;
                if (requiresUnlocked && _isLocked) { ShowNotice("Locked"); return; }
                await operation();
                ran = true;
            });
            // An overlapping request is busy, not necessarily editing-locked.
            // No stale keyboard/already-queued action is enqueued.
            if (!accepted && !_isClosed) ShowNotice("StageBusy");
            return ran;
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
                if (!_isClosed) RebuildPreservingScroll();
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
                _recoveryUnsaved = true;
                _projectGeneration++;
                _documentRevision++;
                _editorContentRevision = EditorRevisionPolicy.Advance(_editorContentRevision);
                _hasPendingEdits = false;
                _armedSong = selectedIndex + 1 < next.Setlist.Count ? selectedIndex + 1 : -1;
                published();
                SynchronizeGain();
                await SaveRecoveryAsync();
            }, _ => { if (!_isClosed) BuildPage(); });
        }
        finally { _isBusy = false; RefreshSnapshot(); }
        if (intent == _transportIntent && !_isClosed) await TogglePlaybackAsync();
    }
}
