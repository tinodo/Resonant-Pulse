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
    private UIElement BuildStage()
    {
        _stageTempo = Heading("");
        _stageTempo.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Bahnschrift");
        _stageTempo.FontSize = 96;
        _position = Body("");
        _instrument = new PulseInstrument { MaxHeight = 320 };
        var panel = Column(Heading(_document.Name), _stageTempo, _position, _instrument);
        panel.Children.Add(CommandFlow(ActionButton(_isLocked ? "Unlock" : "Lock", "StageLock", () =>
        {
            _isLocked = !_isLocked;
            BuildPage();
        })));
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
        Identify(songs, "StageSetlist", Text.Get("Setlist"));
        var cue = Body(_armedSong >= 0 && _armedSong < _document.Setlist.Count
            ? Text.Format("ArmedSong", _document.Setlist[_armedSong].Name) : Text.Get("ChooseSong"));
        var notes = Input("Notes", "SongNotes", _armedSong >= 0 && _armedSong < _document.Setlist.Count ? _document.Setlist[_armedSong].Notes : "");
        notes.AcceptsReturn = true;
        notes.TextWrapping = TextWrapping.Wrap;
        notes.MinHeight = 120;
        notes.IsReadOnly = _isLocked;
        songs.SelectionChanged += (_, _) =>
        {
            if (songs.SelectedIndex < 0) return;
            _armedSong = songs.SelectedIndex;
            // Recreate notes and actions together: their captured indices must
            // always describe the newly armed song, including first selection.
            BuildPage();
        };
        panel.Children.Add(songs);
        panel.Children.Add(cue);
        panel.Children.Add(ActionButton("NextCountIn", "StageNextCountIn", NextSongAsync));
        panel.Children.Add(Copy("StageTransition"));
        panel.Children.Add(notes);
        panel.Children.Add(ActionButton("Apply", "SaveSongNotes", async () =>
        {
            if (_armedSong < 0 || _armedSong >= _document.Setlist.Count)
                throw new InvalidOperationException(Text.Get("ChooseSong"));
            var index = _armedSong;
            await CommitAsync(doc => doc.Setlist[index].Notes = notes.Text, false);
        }));
        panel.Children.Add(ActionButton("AddSong", "AddCurrentSong", async () =>
        {
            await CommitAsync(doc =>
            {
                var snapshot = LocalWorkspace.Clone(doc);
                snapshot.Setlist.Clear();
                doc.Setlist.Add(new SongEntry { Name = snapshot.Name, Document = snapshot, Notes = "" });
            }, false);
            BuildPage();
        }));
        if (_armedSong >= 0 && _armedSong < _document.Setlist.Count)
        {
            var selectedIndex = _armedSong;
            panel.Children.Add(CommandFlow(
                ActionButton("MoveUp", "SongMoveUp", async () =>
                {
                    if (selectedIndex == 0) return;
                    await CommitAsync(doc => (doc.Setlist[selectedIndex - 1], doc.Setlist[selectedIndex]) = (doc.Setlist[selectedIndex], doc.Setlist[selectedIndex - 1]), false);
                    _armedSong--;
                    BuildPage();
                }),
                ActionButton("MoveDown", "SongMoveDown", async () =>
                {
                    if (selectedIndex == _document.Setlist.Count - 1) return;
                    await CommitAsync(doc => (doc.Setlist[selectedIndex + 1], doc.Setlist[selectedIndex]) = (doc.Setlist[selectedIndex], doc.Setlist[selectedIndex + 1]), false);
                    _armedSong++;
                    BuildPage();
                }),
                ActionButton("Remove", "RemoveSong", async () =>
                {
                    if (!await ConfirmAsync(Text.Format("ConfirmRemove", _document.Setlist[selectedIndex].Name), "Remove")) return;
                    await CommitAsync(doc => doc.Setlist.RemoveAt(selectedIndex), false);
                    _armedSong = -1;
                    BuildPage();
                })));
        }
        return Scroll(panel);
    }

    private async Task NextSongAsync()
    {
        if (_isBusy) return;
        if (_armedSong < 0 || _armedSong >= _document.Setlist.Count)
            throw new InvalidOperationException(Text.Get("ChooseSong"));
        var selectedIndex = _armedSong;
        var intent = _transportIntent;
        _isBusy = true;
        StartButton.IsEnabled = false;
        await _edits.WaitAsync();
        try
        {
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
            _hasPendingEdits = false;
            _armedSong = selectedIndex + 1 < next.Setlist.Count ? selectedIndex + 1 : -1;
            SynchronizeGain();
            await _workspace.SaveRecoveryAsync(LocalWorkspace.Clone(_document));
            BuildPage();
        }
        finally { _edits.Release(); _isBusy = false; StartButton.IsEnabled = true; }
        if (intent == _transportIntent && !_isClosed) await TogglePlaybackAsync();
    }
}
