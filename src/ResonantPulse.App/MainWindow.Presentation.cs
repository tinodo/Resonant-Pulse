using Microsoft.UI.Xaml;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private string _lastStartLabel = "";
    private string _lastMuteLabel = "";

    private void RefreshSnapshot()
    {
        if (!_isReady || _isClosed) return;
        var snapshot = _transport.Snapshot();
        _transport.FinishCompleted();
        var active = snapshot.Revision?.Document ?? _document;
        var plan = snapshot.Plan;
        var frame = plan is null ? 0 : Math.Clamp(snapshot.MusicalFrame, 0, plan.TotalFrames - 1);
        PlaybackPosition? position = plan?.PositionAtFrame(frame);
        var blind = active.Settings.BlindGaps && position?.IsSilent == true;
        var settings = active.Settings;
        // The meter may change by program section. Copy only the UI projection,
        // never mutate the immutable revision seen by the audio callback.
        var section = plan?.SectionBoundaries.FirstOrDefault(boundary => boundary.StartFrame <= frame && frame < boundary.EndFrame);
        var meterBeats = section?.BeatsPerBar ?? settings.BeatsPerBar;
        var meterDenominator = section?.BeatDenominator ?? settings.BeatDenominator;
        var shownSettings = settings with
        {
            BeatsPerBar = meterBeats, BeatDenominator = meterDenominator,
            Groups = meterBeats == settings.BeatsPerBar && meterDenominator == settings.BeatDenominator ? settings.Groups : []
        };
        var phase = position is null ? 0 : (position.BeatIndex + position.BeatPhase) / meterBeats;
        var tempo = position?.Tempo ?? settings.Tempo;
        _instrument?.Present(shownSettings, tempo, phase, plan, frame, blind, _preferences.IsReducedMotion);
        if (_tempoCurve is { } curve)
        {
            EnsureTempoPreview();
            var presentation = _tempoCurveState.Select(_documentRevision, plan, frame, blind);
            curve.Present(presentation.Plan, presentation.Frame, presentation.IsBlind);
        }
        var elapsed = plan is null ? TimeSpan.Zero : TimeSpan.FromSeconds((double)snapshot.ProcessedFrame / plan.SampleRate);
        var time = elapsed.ToString(elapsed.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", Text.Culture);
        long barOffset = 0;
        if (snapshot.Revision is { IsFinite: false } revision && snapshot.ProcessedFrame >= revision.Initial.TotalFrames)
        {
            var barsPerLoop = revision.Body.SectionBoundaries.Sum(boundary => boundary.Bars);
            var completedLoops = 1 + (snapshot.ProcessedFrame - revision.Initial.TotalFrames) / revision.Body.TotalFrames;
            barOffset = completedLoops * barsPerLoop;
        }
        var state = Text.Get(snapshot.State switch
        {
            TransportState.Playing => "Playing", TransportState.Paused => "Paused",
            TransportState.Faulted => "AudioError", TransportState.Completed => "Completed", _ => "Stopped"
        });
        switch (TimingRunRules.Update(_isTimingArmed,
            ReferenceEquals(_timingRevision, snapshot.Revision),
            snapshot.State == TransportState.Playing, snapshot.State == TransportState.Completed))
        {
            case TimingRunUpdate.Finish:
                FinishTiming(snapshot.ProcessedFrame);
                break;
            case TimingRunUpdate.Invalidate:
                InvalidateTiming();
                break;
        }
        var detail = blind ? Text.Get("Silent") :
            position?.IsCountIn == true ? Text.Get("CountInActive") :
            Text.Format("Position", barOffset + (position?.BarIndex ?? 0) + 1, (position?.BeatIndex ?? 0) + 1, time);
        if (_position is not null) _position.Text = detail;
        if (_stageTempo is not null)
        {
            _stageTempo.FontSize = Math.Clamp(Root.ActualWidth / 8, 32, 96);
            _stageTempo.Text = blind ? Text.Get("Silent") : $"{tempo.ToString("0.0", Text.Culture)}\n{PulseInstrument.NoteUnit(settings.BeatUnitQuarters)} · {meterBeats}/{meterDenominator}";
        }
        TransportStatus.Text = $"{state} · {(blind ? Text.Get("Silent") : detail)}" +
            (snapshot.IsMuted ? $" · {Text.Get("Muted")}" : "") +
            (_isOutputMissing ? $"\n{Text.Get("OutputMissing")}" : "") +
            (!HasSelectedOutput && !_isOutputMissing ? $"\n{Text.Get("ChooseOutput")}" : "") +
            (_hasPendingEdits ? $"\n{Text.Get("PendingEdits")}" : "");
        StartButton.IsEnabled = !_isBusy && (snapshot.State == TransportState.Playing || HasSelectedOutput);
        StopButton.IsEnabled = snapshot.State != TransportState.Stopped;
        var startKey = snapshot.State == TransportState.Playing ? "Pause" : "Start";
        if (_lastStartLabel != Text.Get(startKey))
        {
            LabelButton(StartButton, startKey);
            _lastStartLabel = Text.Get(startKey);
        }
        var muteKey = snapshot.IsMuted ? "Unmute" : "Mute";
        if (_lastMuteLabel != Text.Get(muteKey))
        {
            LabelButton(MuteButton, muteKey);
            _lastMuteLabel = Text.Get(muteKey);
        }
        if (_programStatus is not null)
        {
            _programStatus.Text = blind ? Text.Get("Silent") : position is null ? Text.Get("ProgramEmpty") :
                $"{(position.IsCountIn ? Text.Get("CountInActive") : position.SectionName)} · {tempo.ToString("0.0", Text.Culture)} BPM · {meterBeats}/{meterDenominator}\n" +
                Text.Format("Progress", TimeSpan.FromSeconds((double)frame / plan!.SampleRate).ToString(@"hh\:mm\:ss", Text.Culture),
                    TimeSpan.FromSeconds((double)plan.TotalFrames / plan.SampleRate).ToString(@"hh\:mm\:ss", Text.Culture));
            if (!blind && section is not null && plan is not null)
            {
                var next = plan.SectionBoundaries.FirstOrDefault(boundary => boundary.StartFrame >= section.EndFrame);
                if (next is not null) _programStatus.Text += "\n" + Text.Format("NextSection", next.Name);
            }
        }
        if (_progress is not null)
        {
            _progress.Visibility = blind ? Visibility.Collapsed : Visibility.Visible;
            _progress.Value = plan is null ? 0 : Math.Clamp((double)frame / plan.TotalFrames, 0, 1);
        }
        if (_isTimingArmed && System.Diagnostics.Stopwatch.GetElapsedTime(_timingStartTimestamp).TotalMinutes >= 10)
            FinishTiming();
    }
}
