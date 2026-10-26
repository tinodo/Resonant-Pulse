using System.Diagnostics;
using Microsoft.UI.Xaml;
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
    private int _timingLane = -1;
    private double _timingTolerance = 0.15;
    private bool _includeSilentTargets = true;
    private ComboBox? _timingTarget;
    private NumberBox? _timingToleranceInput;
    private CheckBox? _timingSilentTargets;
    private readonly List<double> _observed = [];
    private string _lastTimingReport = "";
    private readonly TempoCurveState _tempoCurveState = new();

    private UIElement BuildTrain()
    {
        _instrument = new PulseInstrument();
        _position = Body("");
        _programStatus = Body("");
        _tempoCurve = new TempoCurve();
        _progress = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0 };
        Identify(_progress, "ProgramProgress", Text.Get("Program"));
        var panel = Column(Heading(Text.Get("Train")), _programStatus, _progress,
            Copy("TempoChartAxis"), _tempoCurve, _instrument, _position, Copy("PresentationLimits"), Copy("ProgramEmpty"));
        foreach (var step in _document.Program)
            panel.Children.Add(Body($"{step.Name} · {step.StartTempo.ToString("0.0", Text.Culture)} → {step.EndTempo.ToString("0.0", Text.Culture)} · {step.Bars} × {step.Repeats} {Text.Get("Bars")} · {step.BeatsPerBar}/{step.BeatDenominator} · {Text.Get(step.Ramp)}"));
        panel.Children.Add(ActionButton("Program", "EditProgram", () => SelectView("Studio")));
        panel.Children.Add(Heading(Text.Get("TimingLab")));
        panel.Children.Add(Copy("TimingLimits"));
        panel.Children.Add(Copy("TimingBudget"));
        var targets = new List<Choice> { new("-1", Text.Get("MainTarget")) };
        var targetDocument = _isTimingArmed && _timingRevision is not null ? _timingRevision.Document : _document;
        if (!_isTimingArmed && !TimingRunRules.IsValidTarget(_timingLane, targetDocument.Lanes.Count))
            _timingLane = -1;
        for (var index = 0; index < targetDocument.Lanes.Count; index++)
            targets.Add(new(index.ToString(System.Globalization.CultureInfo.InvariantCulture), targetDocument.Lanes[index].Name));
        var target = SelectChoices("Target", "TimingTarget", _timingLane.ToString(System.Globalization.CultureInfo.InvariantCulture), targets);
        var tolerance = Number("Tolerance", "TimingTolerance", _timingTolerance * 1000, 10, 500, 5);
        var includeSilent = Check("IncludeSilentTargets", "TimingSilentTargets", _includeSilentTargets);
        _timingTarget = target;
        _timingToleranceInput = tolerance;
        _timingSilentTargets = includeSilent;
        RefreshTimingControlState();
        panel.Children.Add(Flow(target, tolerance));
        panel.Children.Add(includeSilent);
        panel.Children.Add(CommandFlow(
            ActionButton("BeginTiming", "BeginKeyboardTiming", () =>
            {
                var snapshot = _transport.Snapshot();
                if (snapshot.State != TransportState.Playing || snapshot.Revision is null)
                    throw new InvalidOperationException(Text.Get("TimingRequiresPlayback"));
                var selectedLane = int.Parse(Selected(target), System.Globalization.CultureInfo.InvariantCulture);
                if (!TimingRunRules.CanArm(_hasPendingEdits, selectedLane, snapshot.Revision.Document.Lanes.Count))
                    throw new InvalidOperationException(Text.Get(_hasPendingEdits ? "TimingPendingEdits" : "TimingTargetChanged"));
                _timingLane = selectedLane;
                _timingTolerance = Value(tolerance) / 1000;
                _includeSilentTargets = includeSilent.IsChecked == true;
                _timingRevision = snapshot.Revision;
                _timingStartFrame = snapshot.ProcessedFrame;
                _timingStartTimestamp = Stopwatch.GetTimestamp();
                _observed.Clear();
                _lastTimingReport = "";
                SetTimingArmed(true);
                BuildPage();
                ShowNotice("TimingArmed");
            }),
            ActionButton("Hit", "KeyboardAttack", RecordAttack),
            ActionButton("FinishTiming", "FinishKeyboardTiming", FinishTiming)));
        _timingStatus = Body(_isTimingArmed ? Text.Get("TimingArmed") : _lastTimingReport);
        panel.Children.Add(_timingStatus);
        panel.Children.Add(Copy("IntegrationGates"));
        return Scroll(panel);
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
            if (_timingStatus is not null) _timingStatus.Text = "";
            ShowError(exception);
        }
    }

    private void FinishTimingCore(long? completedFrame)
    {
        if (!_isTimingArmed || _timingRevision is null) return;
        var revision = _timingRevision;
        SetTimingArmed(false);
        var endFrame = TimingRunRules.EndFrame(_timingStartFrame,
            Stopwatch.GetElapsedTime(_timingStartTimestamp).TotalSeconds, revision.Initial.SampleRate,
            revision.EndFrame, completedFrame);
        var seconds = (double)(endFrame - _timingStartFrame) / revision.Initial.SampleRate;
        var expected = new List<double>();
        void Append(RenderPlan plan, long origin, long excludeBefore)
        {
            foreach (var onset in plan.Targets)
            {
                if (onset.LaneIndex != _timingLane || onset.Accent == Accent.Silent || (!_includeSilentTargets && !onset.Audible)) continue;
                var frame = origin + onset.Frame;
                if (frame < Math.Max(_timingStartFrame, excludeBefore) || frame >= endFrame) continue;
                expected.Add((double)(frame - _timingStartFrame) / plan.SampleRate);
            }
        }
        Append(revision.Initial, 0, revision.CountInFrames);
        if (!revision.IsFinite)
        {
            var firstCycle = Math.Max(0, (_timingStartFrame - revision.Initial.TotalFrames) / revision.Body.TotalFrames);
            for (var cycle = firstCycle; ; cycle++)
            {
                var origin = revision.Initial.TotalFrames + cycle * revision.Body.TotalFrames;
                if (origin >= endFrame) break;
                Append(revision.Body, origin, origin);
            }
        }
        var report = TimingAnalyzer.Analyze(expected, _observed.Where(time => time < seconds).ToArray(), _timingTolerance);
        _lastTimingReport = report.Matched == 0
            ? Text.Format("TimingNoMatches", report.Missed, report.Extra)
            : Text.Format("TimingReport", report.Matched, report.Missed, report.Extra,
                report.MedianOffsetMilliseconds, report.SpreadMilliseconds, report.Coverage) +
              "\n" + (report.Matched >= 4
                  ? Text.Format("TimingAdditional", report.DriftMillisecondsPerSecond, report.AmbiguousExpected)
                  : Text.Get("TimingDriftUnavailable"));
        if (_timingStatus is not null) _timingStatus.Text = _lastTimingReport;
        if (_view == "Train") BuildPage();
    }

    private void InvalidateTiming()
    {
        if (!_isTimingArmed) return;
        SetTimingArmed(false);
        _timingRevision = null;
        _observed.Clear();
        _lastTimingReport = Text.Get("TimingInvalidated");
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
    }
}
