using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
            // Notes typed or on their way belong to the project, so they are in the saved copy.
            if (!await FlushStageNotesAsync()) return;
            await _workspace.SaveLibraryAsync(LocalWorkspace.Clone(_document));
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
        var export = Card("ExportTitle", new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Note("ExportNotice"), Field(exportBars, "UnitBars"), Note("ExportBarsHelp"),
                CommandRow(ActionButton("ExportWave", "ExportWav", () => ExportAsync(false), "\uE896"),
                    ActionButton("ExportMidi", "ExportMidi", () => ExportAsync(true), "\uE8D6"))
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

    private async Task<string?> PickSavePathAsync(string extension, string description)
    {
        if (_isDialogOpen) return null;
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = SanitizeFileName(_document.Name)
        };
        picker.FileTypeChoices.Add(description, [extension]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        _isDialogOpen = true;
        try { return (await picker.PickSaveFileAsync())?.Path; }
        finally { _isDialogOpen = false; }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(value.Where(character => !invalid.Contains(character)).Take(100).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(name) ? "ResonantPulse" : name;
    }

    private async Task SaveProjectAsync()
    {
        // The notes being typed belong to the project, so they are in the file.
        if (!await FlushStageNotesAsync()) return;
        var path = await PickSavePathAsync(".pulse", Text.Get("Save"));
        if (path is null) return;
        await DocumentStore.SaveAsync(path, LocalWorkspace.Clone(_document));
        ShowNotice("Saved");
    }

    private async Task OpenProjectAsync()
    {
        if (_isDialogOpen) return;
        if (_isLocked) { ShowNotice("Locked"); return; }
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".pulse");
        picker.FileTypeFilter.Add(".json");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
        _isDialogOpen = true;
        string? path;
        try { path = (await picker.PickSingleFileAsync())?.Path; }
        finally { _isDialogOpen = false; }
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
            Task? hold = null;
            HoldReplacementForUiTest(ref hold);
            if (hold is not null) await hold;
            var replacement = LocalWorkspace.Clone(document);
            await ValidateAsync(replacement);
            await _workspace.SaveLibraryAsync(LocalWorkspace.Clone(_document));
            _transportIntent++;
            InvalidateTiming();
            _transport.Stop();
            _undo.Push(_document);
            _redo.Clear();
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

    private async Task ExportAsync(bool midi)
    {
        var snapshot = LocalWorkspace.Clone(_document);
        // One length for the preflight and the export: it is the length the box shows, applied the moment it was committed.
        var bars = _preferences.ExportBars;
        if (midi)
        {
            var report = await Task.Run(() => MidiExporter.Preflight(snapshot, bars));
            if (!report.CanExport)
                throw new ArgumentException("MIDI export preflight failed: " + string.Join(" ", report.Errors));
            if (!await ConfirmAsync(Text.Format("MidiPreflight", report.PulsesPerQuarter, report.MaximumTimingErrorSeconds * 1000)))
                return;
        }
        var path = await PickSavePathAsync(midi ? ".mid" : ".wav", Text.Get(midi ? "ExportMidi" : "ExportWave"));
        if (path is null) return;
        try
        {
            if (midi)
                await Task.Run(() => MidiExporter.ExportAsync(path, snapshot, bars));
            else
            {
                var plan = await Task.Run(() => ScheduleCompiler.Compile(snapshot, 48000, bars, true));
                await WaveExporter.ExportAsync(path, plan);
                // Core writes the exact schedule/meter/seed/gain companion
                // manifest atomically. Project exchange remains a separate Save.
            }
            ShowNotice("Saved");
        }
        finally { RefreshSnapshot(); }
    }
}
