using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;
using Windows.Storage.Pickers;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private TextBlock? _libraryStatus;
    private TextBlock? _libraryNoMatch;
    private UIElement? _libraryEmpty;

    /// <summary>
    /// The writes of files that are going (Save, Save to library, Export WAV, Export MIDI, the export of separate tracks, the timing CSV).
    /// Closing the window stops them and waits for them (see <see cref="CloseAfterSavingAsync"/>), so that, when a write stops, no temporary
    /// file stays beside its destination. A command that is dropped because a close was refused shows nothing of its preparation, not even an
    /// error (it would replace the notice that says why the window stayed): the log keeps the error.
    /// </summary>
    private readonly FileWrites _fileWrites = new()
    {
        DroppedError = error => Diagnostics.Write(
            "A command was dropped because the window was being closed; the preparation that it was waiting for ended with an error, which is not shown", error)
    };

    /// <summary>The name of the export of separate tracks among the writes: only one can go at a time, and its command is off while it does.</summary>
    private const string TracksWrite = "tracks";

    /// <summary>The command of the Library page that is on screen (the page is built again each time it is opened).</summary>
    private Button? _stemsButton;

    /// <summary>True once the library has been read successfully. Until then (looking, or unable to) it is not "empty".</summary>
    private bool _libraryScanned;

    private UIElement BuildLibrary()
    {
        _libraryScanned = false;
        _search = TextInput("Search", "LibrarySearch", "", 280);
        _search.TextChanged += (_, _) => FilterLibrary();
        _libraryList = new ListView { MaxHeight = 320, SelectionMode = ListViewSelectionMode.Single };
        Identify(_libraryList, "LibraryProjects", Text.Get("LibraryProjects"));
        _libraryStatus = Note("LibraryLoading");
        _libraryNoMatch = Note("LibraryNoMatch");
        _libraryNoMatch.Visibility = Visibility.Collapsed;
        _libraryEmpty = EmptyState("\uE8F1", "LibraryEmptyTitle", "EmptyLibrary");
        _libraryEmpty.Visibility = Visibility.Collapsed;

        var open = ActionButton("Load", "LoadLibraryProject", OpenSelectedLibraryProjectAsync, "\uE8E5", ButtonKind.Primary);
        open.IsEnabled = false;
        _libraryList.SelectionChanged += (_, _) => open.IsEnabled = _libraryList?.SelectedItem is ListViewItem { Tag: LibraryItem };
        _libraryList.DoubleTapped += async (_, _) => await GuardAsync(OpenSelectedLibraryProjectAsync);
        _libraryList.KeyDown += async (_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.Enter) return;
            args.Handled = true;
            await GuardAsync(OpenSelectedLibraryProjectAsync);
        };
        var save = ActionButton("SaveLibrary", "SaveToLibrary", async () =>
        {
            using (var write = _fileWrites.Begin())
            {
                await HoldPreparationAsync();
                // Notes typed or on their way belong to the project, so they are in the saved copy.
                if (!await FlushStageNotesAsync()) return;
                // No question or dialog follows, so a close that was refused meanwhile does not void the person's command: it is written.
                // The write is told which file it is as soon as the library has named it.
                await _workspace.SaveLibraryAsync(LocalWorkspace.Clone(_document), write.Token, write.BeginWriting);
            }
            // A write that did not stop within the limit of a close ends after the window has gone: nobody is left to tell or to refresh.
            if (IsWindowGoing) return;
            ShowNotice("Saved");
            await RefreshLibraryAsync();
        }, "\uE74E");
        var projects = Card("LibraryProjects", new StackPanel
        {
            Spacing = 12,
            Children = { Field(_search), _libraryStatus, _libraryEmpty, _libraryNoMatch, _libraryList, CommandRow(open, save) }
        }, id: "LibraryProjectsCard");

        var exportBars = NumberInput("ExportBars", "ExportBars", _preferences.ExportBars, 1, 1024);
        // The length applies at once, like every setting: the box shows it and the next export uses it. Remembering it for the
        // next session is best effort; if that fails the error is shown and the value stays, so the box is never put back to a
        // length the next export would not use.
        CommitNumber(exportBars, () => _preferences.ExportBars, async value =>
        {
            _preferences.ExportBars = Integer(value);
            await _workspace.SavePreferencesAsync(_preferences);
        });
        // The tracks command is the third of the row, and wraps to a row of its own where the card is too narrow for three. Its
        // hint is the line under the commands, as the timing check's is. It is off while an export of tracks is going (this page may
        // have been built again meanwhile), so a second one cannot begin.
        var stems = ActionButton("ExportStems", "ExportWavStems", ExportStemsAsync, "\uE896");
        stems.IsEnabled = !_fileWrites.IsGoing(TracksWrite);
        _stemsButton = stems;
        var stemsHint = Note("StemExportHint");
        Identify(stemsHint, "StemExportHint", stemsHint.Text);
        AutomationProperties.GetDescribedBy(stems).Add(stemsHint);
        var export = Card("ExportTitle", new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Note("ExportNotice"), Field(exportBars, "UnitBars"), Note("ExportBarsHelp"),
                CommandRow(ActionButton("ExportWave", "ExportWav", () => ExportAsync(false), "\uE896"),
                    ActionButton("ExportMidi", "ExportMidi", () => ExportAsync(true), "\uE8D6"), stems),
                stemsHint
            }
        }, id: "ExportCard");

        var fresh = ActionButton("New", "NewProject", async () =>
        {
            if (!await ConfirmAsync(Text.Get("Replace"))) return;
            await ReplaceDocumentAsync(new PulseDocument { Name = Text.Get("OpenPractice") });
        }, "\uE710", ButtonKind.Tertiary);
        var openFile = ActionButton("Open", "LibraryImport", OpenProjectAsync, "\uE8E5", ButtonKind.Tertiary);
        var saveFile = ActionButton("Save", "LibraryExportProject", SaveProjectAsync, "\uE74E", ButtonKind.Tertiary);
        _ = GuardAsync(RefreshLibraryAsync);
        return PageScaffold("Library", "LibraryPurpose", CommandRow(fresh, openFile, saveFile),
            TwoPane(projects, export, 360), BuildPresets());
    }

    private async Task OpenSelectedLibraryProjectAsync()
    {
        if (_libraryList?.SelectedItem is not ListViewItem { Tag: LibraryItem item }) return;
        await LoadProjectAsync(item.Path);
    }

    private async Task RefreshLibraryAsync()
    {
        LibraryReadResult result;
        try { result = await _workspace.ListAsync(); }
        catch
        {
            // Nothing is being looked for any more. The page says it could not look, and the caller reports the reason. A
            // library that could not be read is not an empty one: no empty or no-match message, whatever is typed in Search.
            _libraryScanned = false;
            _library = [];
            if (_libraryStatus is not null)
            {
                _libraryStatus.Text = Text.Get("LibraryUnreadable");
                _libraryStatus.Visibility = Visibility.Visible;
            }
            if (_libraryEmpty is not null) _libraryEmpty.Visibility = Visibility.Collapsed;
            if (_libraryNoMatch is not null) _libraryNoMatch.Visibility = Visibility.Collapsed;
            _libraryList?.Items.Clear();
            throw;
        }
        _library = result.Items;
        _libraryScanned = true;
        if (_libraryStatus is not null) _libraryStatus.Visibility = Visibility.Collapsed;
        FilterLibrary();
        if (result.Failures.Count > 0 || result.HasMore)
        {
            foreach (var failure in result.Failures)
                Diagnostics.Write($"Library entry could not be read: {failure.Path}", failure.Error);
            Notice.Severity = InfoBarSeverity.Warning;
            Notice.Title = Text.Get(result.HasMore ? "LibraryLimited" : "LibraryPartial");
            var warnings = new List<string>();
            if (result.HasMore) warnings.Add(Text.Format("LibraryOverflow", LocalWorkspace.LibraryScanLimit));
            if (result.Failures.Count > 0)
                warnings.Add(Text.Format("LibraryFailedPaths", result.Failures.Count,
                    string.Join(Environment.NewLine, result.Failures.Select(failure => failure.Path))));
            Notice.Message = string.Join(Environment.NewLine + Environment.NewLine, warnings);
            Notice.IsOpen = true;
        }
    }

    private void FilterLibrary()
    {
        if (_libraryList is null) return;
        var query = _search?.Text ?? "";
        _libraryList.Items.Clear();
        foreach (var item in _library.Where(item => item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
        {
            var name = new TextBlock
            {
                Text = item.Name, Style = ResonantStyle("BodyStyle"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var saved = new TextBlock
            {
                Text = item.Modified is { } modified ? modified.ToString("d", Text.Culture) : "",
                Style = ResonantStyle("NoteStyle"), VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(saved, 1);
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(name);
            row.Children.Add(saved);
            var entry = new ListViewItem { Content = row, Tag = item, MinHeight = 44, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            Identify(entry, $"LibraryProject{_libraryList.Items.Count}", item.Name);
            _libraryList.Items.Add(entry);
        }
        // Nothing saved: the page says what the library is for. Saved but nothing matches: say so, instead of a blank list. Both
        // are said only about a library that has been read: while it is being looked at, or when it could not be read, the
        // status line is all the page says, however Search is edited.
        var empty = _library.Count == 0;
        _libraryList.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if (_libraryEmpty is not null) _libraryEmpty.Visibility = _libraryScanned && empty ? Visibility.Visible : Visibility.Collapsed;
        if (_libraryNoMatch is not null)
            _libraryNoMatch.Visibility = _libraryScanned && !empty && _libraryList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Every file dialog (where to save a file, which file to open) goes through here, so that all of them behave alike. None is shown
    /// while another dialog or question is open, or while the window is being closed: it ends with no file, as if it had been cancelled
    /// (a question is held back in the same cases). The flag that says a dialog is open
    /// is raised for the whole attempt and put down again however it ends. A person who cancels the dialog is not an error: no file,
    /// no message. A dialog that cannot be opened, or fails, is reported as that and never as an audio problem (see
    /// <see cref="FileDialogFailure"/>): the cause, with its HRESULT, goes to the log, and since nothing was read or written the
    /// command can be used again at once.
    /// </summary>
    private async Task<string?> ShowFileDialogAsync(bool saving, string suggestedFile, Func<Task<string?>> show)
    {
        if (_isDialogOpen || _closeFlushing || _isClosed) return null;
        _isDialogOpen = true;
        try
        {
            string? standIn = null;
            var handled = false;
            StandInForFileDialogForUiTest(saving, suggestedFile, ref handled, ref standIn);
            return handled ? standIn : await show();
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            Diagnostics.Write(saving ? "The window for choosing where to save a file could not be used" : "The window for choosing a file to open could not be used", error);
            throw FileDialogFailure.ForPage(error);
        }
        finally { _isDialogOpen = false; }
    }

    // Lets the local verification build stand in for the file dialog (it fails, is cancelled, or answers with a path), so that the real
    // window can be driven where the system's dialog cannot open (MainWindow.UiTest.cs); shipped builds have no body.
    partial void StandInForFileDialogForUiTest(bool saving, string suggestedFile, ref bool handled, ref string? path);

    /// <summary>
    /// Where a write is prepared (before it asks a question or opens a dialog) the local verification build can hold it, so that a driver
    /// can close the window, or look at the page, meanwhile (MainWindow.UiTest.cs); shipped builds return at once.
    /// </summary>
    private Task HoldPreparationAsync()
    {
        Task? hold = null;
        HoldPreparationForUiTest(ref hold);
        return hold ?? Task.CompletedTask;
    }

    partial void HoldPreparationForUiTest(ref Task? hold);

    private async Task<string?> PickSavePathAsync(string extension, string description, string? suggestedName = null)
    {
        var fileName = SanitizeFileName(suggestedName ?? _document.Name);
        return await ShowFileDialogAsync(saving: true, fileName + extension, async () =>
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = fileName };
            picker.FileTypeChoices.Add(description, [extension]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            return (await picker.PickSaveFileAsync())?.Path;
        });
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(value.Where(character => !invalid.Contains(character)).Take(100).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(name) ? "ResonantPulse" : name;
    }

    private async Task SaveProjectAsync()
    {
        using var write = _fileWrites.Begin();
        await HoldPreparationAsync();
        // The notes being typed belong to the project, so they are in the file. A close that was refused while they were being saved: the person
        // was closing the window, so the dialog is not opened now, and nothing is said if saving the notes failed.
        var (wanted, notesSaved) = await write.PrepareAsync(FlushStageNotesAsync);
        if (!wanted || !notesSaved) return;
        var path = await PickSavePathAsync(".pulse", Text.Get("Save"));
        if (path is null) return;
        write.Token.ThrowIfCancellationRequested();
        write.BeginWriting(path);
        await DocumentStore.SaveAsync(path, LocalWorkspace.Clone(_document), write.Token);
        ShowSavedNotice();
    }

    private async Task OpenProjectAsync()
    {
        if (_isDialogOpen) return;
        if (_isLocked) { ShowNotice("Locked"); return; }
        var path = await ShowFileDialogAsync(saving: false, "", async () =>
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".pulse");
            picker.FileTypeFilter.Add(".json");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            return (await picker.PickSingleFileAsync())?.Path;
        });
        if (path is not null) await LoadProjectAsync(path);
    }

    private async Task LoadProjectAsync(string path)
    {
        if (_isLocked) { ShowNotice("Locked"); return; }
        if (new FileInfo(path).Length > DocumentValidator.MaximumDocumentBytes) throw new InvalidDataException(Text.Get("ImportLimit"));
        var document = await DocumentStore.LoadAsync(path);
        await ValidateAsync(document);
        if (!await ConfirmAsync(Text.Get("Replace"))) return;
        await ReplaceDocumentAsync(document);
    }

    /// <summary>
    /// Replaces the project. It runs as a Stage operation: from before the notes are saved until the page has been built again
    /// the notes box and the setlist are switched off, so nothing can be typed that no save would reach (the old project's
    /// backup, or the new project). Notes typed or on their way belong to the project that is about to go; they are saved into
    /// it first, in order, so a queued save can never reach the replacement, and if one cannot be saved the replacement does not
    /// happen. A replacement that fails leaves the page as it was, working again.
    /// </summary>
    private Task ReplaceDocumentAsync(PulseDocument document) =>
        RunStageOperationAsync(() => ReplaceProjectAsync(document), restructures: true);

    // Lets the local verification build hold a replacement open, so a driver can look at the page meanwhile (MainWindow.UiTest.cs);
    // shipped builds have no body.
    partial void HoldReplacementForUiTest(ref Task? hold);

    private async Task ReplaceProjectAsync(PulseDocument document)
    {
        await DocumentPublication.RunAsync(_edits, async published =>
        {
            // All callers enter without _edits held. Keep validation, backup,
            // replacement and recovery in the same transaction as normal edits
            // and undo, so an older edit cannot publish after this replacement.
            if (_isLocked) { ShowNotice("Locked"); return; }
            // The project that goes is saved to the library first. That is a write of a file like the others, and it begins before the new
            // project is validated (FileWrites.RunAsync): a close that comes meanwhile must see it, or the copy would begin to be saved after
            // the close had counted the writes that are going and nothing would cancel it. Closing the window stops it (the replacement then
            // does not happen; nothing was changed before this point) and waits for it. It is under the edit lock, which closing waits for
            // first, so a close that comes now is refused after 4 seconds and the second attempt goes.
            var replacement = LocalWorkspace.Clone(document);
            await _fileWrites.RunAsync(async () =>
            {
                await HoldPreparationAsync();
                Task? hold = null;
                HoldReplacementForUiTest(ref hold);
                if (hold is not null) await hold;
                await ValidateAsync(replacement);
            }, backup => _workspace.SaveLibraryAsync(LocalWorkspace.Clone(_document), backup.Token, backup.BeginWriting));
            SupersedeTransportIntent();
            InvalidateTiming();
            _transport.Stop();
            RecordUndo();
            _document = replacement;
            _recoveryUnsaved = true;
            _projectGeneration++;
            _documentRevision++;
            _editorContentRevision = EditorRevisionPolicy.Advance(_editorContentRevision);
            _hasPendingEdits = false;
            _armedSong = -1;
            published();
            SynchronizeGain();
            await SaveRecoveryAsync();
        }, _ => { if (!_isClosed) BuildPage(); });
    }

    /// <summary>
    /// Exports the project as a WAV file (with its manifest) or a MIDI file. The MIDI export asks first (what the file can and cannot
    /// hold), and the question comes after the file has been looked at on a background thread. Closing the window stops a write that is
    /// going and waits for it (<see cref="FileWrites"/>), so that, when the write stops, no temporary file stays beside the destination.
    /// </summary>
    private async Task ExportAsync(bool midi)
    {
        var snapshot = LocalWorkspace.Clone(_document);
        // One length for the preflight and the export: it is the length the box shows, applied the moment it was committed.
        var bars = _preferences.ExportBars;
        using var write = _fileWrites.Begin();
        await HoldPreparationAsync();
        if (midi)
        {
            var (wanted, report) = await write.PrepareAsync(() => Task.Run(() => MidiExporter.Preflight(snapshot, bars), write.Token));
            // A close that was refused while the file was being looked at: the person was closing the window, so neither the question nor an
            // error (the file may not be exportable) is shown now.
            if (!wanted) return;
            if (!report.CanExport)
                throw new ArgumentException("MIDI export preflight failed: " + string.Join(" ", report.Errors));
            if (!await ConfirmAsync(Text.Format("MidiPreflight", report.PulsesPerQuarter, report.MaximumTimingErrorSeconds * 1000)))
                return;
        }
        if (!write.StillWanted) return;
        var path = await PickSavePathAsync(midi ? ".mid" : ".wav", Text.Get(midi ? "ExportMidi" : "ExportWave"));
        if (path is null) return;
        write.Token.ThrowIfCancellationRequested();
        // The dialog was the last thing to ask: the person has chosen the file, so from here a refused close leaves the export alone (a WAV
        // is compiled after this point, a MIDI file is written at once).
        write.BeginWriting(path);
        try
        {
            if (midi)
                await Task.Run(() => MidiExporter.ExportAsync(path, snapshot, bars, write.Token), write.Token);
            else
            {
                var plan = await Task.Run(() => ScheduleCompiler.Compile(snapshot, 48000, bars, true), write.Token);
                await WaveExporter.ExportAsync(path, plan, write.Token);
                // Core writes the exact schedule/meter/seed/gain companion
                // manifest atomically. Project exchange remains a separate Save.
            }
            ShowSavedNotice();
        }
        finally { if (!IsWindowGoing) RefreshSnapshot(); }
    }

    /// <summary>
    /// Saves the project as separate tracks in one ZIP. The question comes first, because it says what the tracks take and cancelling
    /// must cost nothing: the plan is compiled on a background thread from a copy of the project as it is now (so what the question
    /// describes is what is saved, whatever is edited meanwhile), the question is asked, and only then is a file chosen and written.
    /// From the press until the end the command is off. Closing the window stops the export and waits for it, like every other write of
    /// a file (<see cref="FileWrites"/>; <see cref="CloseAfterSavingAsync"/> waits at most 4 seconds for them, after the wait for the
    /// save), so that normally no temporary archive stays beside the destination.
    /// </summary>
    private async Task ExportStemsAsync()
    {
        if (_fileWrites.TryBeginExclusive(TracksWrite) is not { } write) return;
        // Pressed with the keyboard, the command has the focus. It goes off now, so the system moves the focus on to the next control, and
        // the question gives it back to that control when it closes: the command takes the focus back when the run has ended, unless the
        // person has moved it meanwhile (see CommandFocus). Everything that can go wrong is inside the try, so the write always ends.
        var command = _stemsButton;
        var hadFocus = false;
        var focusState = FocusState.Programmatic;
        object? left = null;
        try
        {
            if (command is not null)
            {
                hadFocus = ReferenceEquals(FocusedElement(), command);
                // Focus(Unfocused) is not allowed: a command that has the focus but says nothing about how is given it as a program would.
                if (command.FocusState != FocusState.Unfocused) focusState = command.FocusState;
            }
            RefreshStemsButton();
            left = FocusedElement();
            var snapshot = LocalWorkspace.Clone(_document);
            var bars = _preferences.ExportBars;
            var names = snapshot.Lanes.Select(lane => lane.Name).ToArray();
            await HoldPreparationAsync();
            var (wanted, prepared) = await write.PrepareAsync(() => Task.Run(() =>
            {
                var plan = ScheduleCompiler.Compile(snapshot, 48000, bars, true);
                return (Plan: plan, Summary: StemExport.Summarize(plan));
            }, write.Token));
            // A close that was refused while the plan was being compiled: the person was closing the window, so neither the question nor an
            // error (the plan may not compile) is shown now.
            if (!wanted) return;
            if (!await ConfirmAsync(StemExport.ConfirmText(prepared.Summary))) return;
            var path = await PickSavePathAsync(".zip", Text.Get("ExportStems"), StemExport.SuggestedFileName(snapshot.Name));
            if (path is null) return;
            write.Token.ThrowIfCancellationRequested();
            write.BeginWriting(path);
            ShowSavingNotice();
            try { await Task.Run(() => WaveStemExporter.ExportAsync(path, prepared.Plan, names, write.Token), write.Token); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The file that was there is as it was (the export is atomic); the person is told so, and the cause is in the log. When the
                // temporary file could not be removed either, the person is told which file stays.
                Diagnostics.Write("Saving the tracks", error);
                throw new UserMessageException(StemExport.NotSavedText(error));
            }
            ShowSavedNotice();
        }
        finally
        {
            write.End();
            if (!IsWindowGoing)
            {
                RefreshStemsButton();
                if (command is not null && CommandFocus.TakesFocusBack(hadFocus, ReferenceEquals(command, _stemsButton) && command.IsEnabled,
                        ReferenceEquals(FocusedElement(), left)))
                    command.Focus(focusState);
                RefreshSnapshot();
            }
        }
    }

    /// <summary>The element that has the keyboard focus in this window, if any.</summary>
    private object? FocusedElement() => FocusManager.GetFocusedElement(Root.XamlRoot);

    private void RefreshStemsButton()
    {
        if (_stemsButton is not null) _stemsButton.IsEnabled = !_fileWrites.IsGoing(TracksWrite);
    }

    /// <summary>A notice that stays until something replaces it (it says what is going on, so it does not fade like "Saved").</summary>
    private void ShowOngoingNotice(string key)
    {
        _noticeTimer.Stop();
        Notice.Severity = InfoBarSeverity.Informational;
        Notice.Title = Text.Get(key);
        Notice.Message = "";
        Notice.IsOpen = true;
    }

    /// <summary>
    /// The notice that says the tracks are being saved. It does not replace a warning or an error that is open (a failure that still
    /// needs attention); it comes back when the person dismisses that one (see the notice bar's Closed handler) and when a transient
    /// notice that replaced it has faded (see the notice timer's Tick handler).
    /// </summary>
    private void ShowSavingNotice()
    {
        if (SavingNotice.MayReplace(Notice.IsOpen, Notice.Severity == InfoBarSeverity.Informational))
            ShowOngoingNotice("StemExportSaving");
    }

    /// <summary>
    /// The Saved notice of a write that has ended. A write that did not stop within the limit of a close ends, if it ends at all, after the
    /// window has gone: nobody is left to tell.
    /// </summary>
    private void ShowSavedNotice()
    {
        if (!IsWindowGoing) ShowNotice("Saved");
    }
}
