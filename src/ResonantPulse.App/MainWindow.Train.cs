using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private bool _isTimingArmed;
    private long _timingStartTimestamp;
    private long _timingStartFrame;
    private PlaybackRevision? _timingRevision;

    /// <summary>
    /// The project's name when the check began. A rename is not an edit that reaches the audio, so the playing revision can still
    /// hold the name from when playback last started: the check goes by the name the person sees.
    /// </summary>
    private string _timingProjectName = "";
    private int _timingLane = -1;
    private double _timingTolerance = 0.15;
    private bool _includeSilentTargets = true;
    private ComboBox? _timingTarget;
    private NumberBox? _timingToleranceInput;
    private ToggleSwitch? _timingSilentTargets;
    private Button? _timingBegin;
    private Button? _timingHit;
    private Button? _timingFinish;
    private readonly List<double> _observed = [];
    private string _lastTimingReport = "";

    /// <summary>
    /// The one finished check that can be exported: kept as it was measured (the project may change after it), until another check
    /// starts, the run that is going is ended by a change of playback or project, or an error ends one.
    /// </summary>
    private TimingSession? _lastTimingSession;
    private Button? _timingExport;
    private readonly TempoCurveState _tempoCurveState = new();

    private TrainProgramView _trainProgramView;

    /// <summary>
    /// The program card's content. It is swapped in place when what it says changes, so the rest of the page (the timing check
    /// above all) keeps its controls and the choices not yet used.
    /// </summary>
    private ContentControl? _programHost;

    /// <summary>
    /// What the program card says: the readouts of a program (status, progress, chart), a note when the program that plays is
    /// not the one in the project, and the project's steps; or, with no program anywhere, what a program is for.
    /// </summary>
    private UIElement BuildProgramBody(TrainProgramView view)
    {
        _trainProgramView = view;
        // The chart, progress bar and step status only mean something when there is a program to follow.
        _tempoCurve = view.Readouts ? new TempoCurve() : null;
        _progress = view.Readouts ? new ProgressBar { Minimum = 0, Maximum = 1, Value = 0 } : null;
        _programStatus = view.Readouts ? Body("") : null;
        if (_progress is not null) Identify(_progress, "ProgramProgress", Text.Get("Program"));
        if (!view.Readouts)
        {
            return EmptyState("\uEC4A", "ProgramEmptyTitle", "ProgramEmpty",
                ActionButton("BuildProgram", "EditProgram", () => SelectView("Studio"), "\uE9E9", ButtonKind.Primary),
                ActionButton("Presets", "OpenPresets", () => SelectView("Library"), "\uE8F1"));
        }
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(_programStatus!);
        body.Children.Add(_progress!);
        body.Children.Add(Note("TempoChartAxis"));
        body.Children.Add(_tempoCurve!);
        // The readouts above follow what plays; the steps below are the project's program. When they differ, the page says so.
        if (view.Note != ProgramNote.None)
            body.Children.Add(Note(view.Note == ProgramNote.Removed ? "TrainProgramRemoved" : "TrainProgramChanged"));
        if (_document.Program.Count > 0) body.Children.Add(SettingRows(_document.Program.Select(ProgramStepRow).ToArray()));
        body.Children.Add(CommandRow(ActionButton("EditInStudio", "EditProgram", () => SelectView("Studio"), "\uE70F")));
        return body;
    }

    private UIElement BuildTrain()
    {
        // Without a cap the dial's viewbox scales to the full page width inside the scrolling page and fills several screens.
        _instrument = new PulseInstrument { MaxHeight = 320, HorizontalAlignment = HorizontalAlignment.Center };
        _position = Body("");
        _position.HorizontalAlignment = HorizontalAlignment.Center;
        _position.TextAlignment = TextAlignment.Center;
        // What plays can be a program the project no longer holds (a Studio edit is heard from the next Start), so the readouts
        // follow the revision that plays as well as the project.
        var current = _transport.Snapshot();
        _programHost = new ContentControl
        {
            IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
            Content = BuildProgramBody(TrainProgramView.Of(_document.Program, current.Revision?.Document.Program,
                current.State == TransportState.Completed))
        };
        var program = Card("TrainProgramTitle", _programHost, "TrainProgramHelp", id: "ProgramCard");
        var live = Card("NowPlaying", new StackPanel { Spacing = 8, Children = { _instrument, _position } }, id: "LiveCard");

        var targets = new List<Choice> { new("-1", Text.Get("MainTarget")) };
        var targetDocument = _isTimingArmed && _timingRevision is not null ? _timingRevision.Document : _document;
        if (!_isTimingArmed && !TimingRunRules.IsValidTarget(_timingLane, targetDocument.Lanes.Count))
            _timingLane = -1;
        for (var index = 0; index < targetDocument.Lanes.Count; index++)
            targets.Add(new(index.ToString(System.Globalization.CultureInfo.InvariantCulture), targetDocument.Lanes[index].Name));
        var target = Dropdown("Target", "TimingTarget", _timingLane.ToString(System.Globalization.CultureInfo.InvariantCulture), targets);
        var tolerance = NumberInput("Tolerance", "TimingTolerance", _timingTolerance * 1000, 10, 500, 5);
        var includeSilent = Toggle("IncludeSilentTargets", "TimingSilentTargets", _includeSilentTargets);
        _timingTarget = target;
        _timingToleranceInput = tolerance;
        _timingSilentTargets = includeSilent;
        _timingBegin = ActionButton("BeginTiming", "BeginKeyboardTiming", () =>
        {
            var snapshot = _transport.Snapshot();
            if (snapshot.State != TransportState.Playing || snapshot.Revision is null)
                throw new InvalidOperationException(Text.Get("TimingRequiresPlayback"));
            var selectedLane = int.Parse(Selected(target), System.Globalization.CultureInfo.InvariantCulture);
            if (!TimingRunRules.CanArm(_hasPendingEdits, selectedLane, snapshot.Revision.Document.Lanes.Count))
                throw new InvalidOperationException(Text.Get(_hasPendingEdits ? "TimingPendingEdits" : "TimingTargetChanged"));
            _timingLane = selectedLane;
            _timingTolerance = Value(tolerance) / 1000;
            _includeSilentTargets = includeSilent.IsOn;
            _timingRevision = snapshot.Revision;
            _timingProjectName = _document.Name;
            _timingStartFrame = snapshot.ProcessedFrame;
            _timingStartTimestamp = Stopwatch.GetTimestamp();
            _observed.Clear();
            _lastTimingReport = "";
            _lastTimingSession = null;
            SetTimingArmed(true);
            BuildPage();
            ShowNotice("TimingArmed");
        }, "\uE768", ButtonKind.Primary);
        _timingHit = ActionButton("Hit", "KeyboardAttack", RecordAttack, "\uE815");
        _timingFinish = ActionButton("FinishTiming", "FinishKeyboardTiming", FinishTiming, "\uE73E");
        _timingExport = ActionButton("ExportTimingCsv", "ExportTimingCsv", ExportTimingCsvAsync, "\uE896");
        RefreshTimingControlState();
        _timingStatus = Body(_isTimingArmed ? Text.Get("TimingArmed") : _lastTimingReport);
        // Once a check has finished, a line says what an export would hold: the project and the rhythm as the check had them, and
        // when it ended. The hint under it says what the file is, and is there before that too, as what the command is for.
        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Note("TimingLimits"),
                FieldRow(Field(target), Field(tolerance, "UnitMs")),
                SettingRow("IncludeSilentTargets", "IncludeSilentHelp", includeSilent),
                CommandRow(_timingBegin, _timingHit, _timingFinish, _timingExport),
                _timingStatus
            }
        };
        if (_lastTimingSession is { } finished && !_isTimingArmed)
        {
            var ready = Body(TimingExport.ReadyText(finished));
            Identify(ready, "TimingCsvReady", ready.Text);
            content.Children.Add(ready);
        }
        var hint = Note("TimingCsvHint");
        Identify(hint, "TimingCsvHint", hint.Text);
        // A screen reader that reaches the command hears what it saves, also while the command is off.
        AutomationProperties.GetDescribedBy(_timingExport).Add(hint);
        content.Children.Add(hint);
        content.Children.Add(Note("TimingBudget"));
        var timing = Card("TimingLab", content, id: "TimingCard");
        return PageScaffold("Train", "TrainPurpose", null, TwoPane(program, live, 380), timing);
    }

    private void EnsureTempoPreview()
    {
        var revision = _documentRevision;
        if (!_tempoCurveState.TryBegin(revision)) return;
        _ = GuardAsync(async () =>
        {
            try
            {
                // Clone on the UI thread before awaiting. Completion only
                // updates the preview cache, never the document or audio plan.
                var document = Storage.LocalWorkspace.Clone(_document);
                var plan = await Task.Run(() => ScheduleCompiler.Compile(document, 48000, 16, true));
                _tempoCurveState.TryComplete(revision, _documentRevision, plan);
            }
            catch (Exception exception)
            {
                _tempoCurveState.TryComplete(revision, _documentRevision, null);
                if (revision == _documentRevision) throw;
                Diagnostics.Write("Discarded stale tempo-preview compilation failure", exception);
            }
            finally
            {
                // Refresh the current page only; never capture an old curve.
                // A stale completion also permits the latest revision to start.
                if (!_isClosed && _tempoCurve is not null) RefreshSnapshot();
            }
        });
    }

    private void RecordAttack()
    {
        if (!_isTimingArmed) { ShowNotice("TimingRequiresPlayback"); return; }
        if (_observed.Count >= 10000) { FinishTiming(); return; }
        _observed.Add(Stopwatch.GetElapsedTime(_timingStartTimestamp).TotalSeconds);
        // Deliberately no audible preview or beat-rate UIA live announcements.
    }

    private void FinishTiming() => FinishTiming(null);

    private void FinishTiming(long? completedFrame)
    {
        try { FinishTimingCore(completedFrame); }
        catch (Exception exception)
        {
            SetTimingArmed(false);
            _lastTimingReport = "";
            // An error ends the check: there is nothing finished to export.
            _lastTimingSession = null;
            RefreshTimingControlState();
            if (_timingStatus is not null) _timingStatus.Text = "";
            ShowError(exception);
        }
    }

    private void FinishTimingCore(long? completedFrame)
    {
        if (!_isTimingArmed || _timingRevision is null) return;
        var revision = _timingRevision;
        SetTimingArmed(false);
        // The run's frames are absolute device frames; the revision converts them to its own timeline.
        var endFrame = TimingRunRules.EndFrame(_timingStartFrame,
            Stopwatch.GetElapsedTime(_timingStartTimestamp).TotalSeconds, revision.Initial.SampleRate,
            revision.EndFrameAbsolute, completedFrame);
        var seconds = (double)(endFrame - _timingStartFrame) / revision.Initial.SampleRate;
        var expected = revision.TargetSeconds(_timingLane, _includeSilentTargets, _timingStartFrame, endFrame);
        // What was measured is kept as it was: the project as it was named when the check began and the rhythm of the revision that
        // played, not the project as it is when the file is written.
        var context = new TimingSessionContext(_timingProjectName,
            TimingExport.TargetName(_timingLane, _timingLane < 0 ? "" : revision.Document.Lanes[_timingLane].Name),
            _timingLane, revision.Initial.SampleRate, _timingStartFrame, endFrame, _includeSilentTargets);
        var session = new TimingSession(context, expected, _observed.Where(time => time < seconds).ToArray(), _timingTolerance);
        _lastTimingSession = session;
        _lastTimingReport = TimingExport.ReportText(session.Report);
        RefreshTimingControlState();
        if (_timingStatus is not null) _timingStatus.Text = _lastTimingReport;
        if (_view == "Train") BuildPage();
    }

    private void InvalidateTiming()
    {
        if (!_isTimingArmed) return;
        SetTimingArmed(false);
        _timingRevision = null;
        _observed.Clear();
        _lastTimingSession = null;
        _lastTimingReport = Text.Get("TimingInvalidated");
        RefreshTimingControlState();
        if (_timingStatus is not null) _timingStatus.Text = _lastTimingReport;
    }

    private void SetTimingArmed(bool isArmed)
    {
        var wasArmed = _isTimingArmed;
        _isTimingArmed = isArmed;
        RefreshTimingControlState();
        if (wasArmed && !isArmed && _timingTarget is not null)
        {
            // Pending edits may have changed lane names/order while the run
            // retained its original snapshot. Refresh only the picker on exit,
            // not the entire page from the transport refresh loop.
            // This is a UI selection only. Do not change the measured lane
            // while FinishTimingCore is still producing its original report.
            var selectedLane = TimingRunRules.IsValidTarget(_timingLane, _document.Lanes.Count) ? _timingLane : -1;
            var targets = new List<Choice> { new("-1", Text.Get("MainTarget")) };
            for (var index = 0; index < _document.Lanes.Count; index++)
                targets.Add(new(index.ToString(System.Globalization.CultureInfo.InvariantCulture), _document.Lanes[index].Name));
            _timingTarget.ItemsSource = targets;
            _timingTarget.SelectedIndex = selectedLane + 1;
        }
    }

    private void RefreshTimingControlState()
    {
        var isEnabled = TimingRunRules.CanEditArmingOptions(_isTimingArmed);
        if (_timingTarget is not null) _timingTarget.IsEnabled = isEnabled;
        if (_timingToleranceInput is not null) _timingToleranceInput.IsEnabled = isEnabled;
        if (_timingSilentTargets is not null) _timingSilentTargets.IsEnabled = isEnabled;
        // A check starts, takes presses, then finishes: only the command that makes sense now is available.
        if (_timingBegin is not null) _timingBegin.IsEnabled = !_isTimingArmed;
        if (_timingHit is not null) _timingHit.IsEnabled = _isTimingArmed;
        if (_timingFinish is not null) _timingFinish.IsEnabled = _isTimingArmed;
        // The result of a finished check can be exported, and nothing else can: not while a check runs, not after one was ended.
        if (_timingExport is not null) _timingExport.IsEnabled = TimingExport.CanExport(_isTimingArmed, _lastTimingSession);
    }

    private async Task ExportTimingCsvAsync()
    {
        var session = _lastTimingSession;
        if (!TimingExport.CanExport(_isTimingArmed, session))
            throw new InvalidOperationException(Text.Get("TimingCsvUnavailable"));
        using var write = _fileWrites.Begin();
        await HoldPreparationAsync();
        if (!write.StillWanted) return;
        var path = await PickSavePathAsync(".csv", Text.Get("ExportTimingCsv"), TimingExport.SuggestedFileName(session!.Context.ProjectName));
        if (path is null) return;
        write.Token.ThrowIfCancellationRequested();
        write.BeginWriting(path);
        await Task.Run(() => TimingCsvExporter.ExportAsync(path, session, write.Token), write.Token);
        ShowSavedNotice();
    }

    /// <summary>A program step: its name, then tempo, length, meter and how the tempo changes, in one short line.</summary>
    private static UIElement ProgramStepRow(ProgramStep step)
    {
        string Tempo(double value) => TempoFormat.Format(value, Text.Culture);
        var tempo = step.StartTempo == step.EndTempo ? $"{Tempo(step.StartTempo)} BPM" : $"{Tempo(step.StartTempo)} → {Tempo(step.EndTempo)} BPM";
        var length = $"{step.Bars} {Text.Get("Bars").ToLower(Text.Culture)}" + (step.Repeats > 1 ? $" × {step.Repeats}" : "");
        var detail = $"{tempo} · {length} · {step.BeatsPerBar}/{step.BeatDenominator}" +
            (step.Ramp == "Constant" ? "" : $" · {Text.Get(step.Ramp)}");
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = step.Name, Style = ResonantStyle("BodyStyle"), FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                new TextBlock { Text = detail, Style = ResonantStyle("NoteStyle") }
            }
        };
    }
}
