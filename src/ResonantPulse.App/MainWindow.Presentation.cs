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
        // The program card of the Train page follows what plays as well as the project. When that changes (a run ends, so a
        // program that was removed in Studio is no longer playing) the card is built again, in place: the rest of the page,
        // the timing check with the choices made in it, is left alone.
        if (_view == "Train" && _programHost is not null)
        {
            var view = TrainProgramView.Of(_document.Program, snapshot.Revision?.Document.Program,
                snapshot.State == TransportState.Completed);
            if (view != _trainProgramView) _programHost.Content = BuildProgramBody(view);
        }
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
        // The program card describes the program listed beside it. A finished run of a program that Studio has since changed does
        // not, so after it the card shows the project's own program, idle, as after Stop.
        var cardIdle = TrainProgramView.FinishedRunIsStale(_document.Program, snapshot.Revision?.Document.Program,
            snapshot.State == TransportState.Completed);
        var cardPlan = cardIdle ? null : plan;
        var cardFrame = cardIdle ? 0 : frame;
        PlaybackPosition? cardPosition = cardIdle ? null : position;
        var cardSection = cardIdle ? null : section;
        var cardBlind = !cardIdle && blind;
        // The readout shows what the player asked for while nothing plays a program; otherwise the audible tempo, so a
        // running program (or a running open-ended revision) is not misreported after a Studio edit changes the document.
        var tempo = TempoReadout.Select(active.Program.Count > 0, _document.Program.Count > 0, _tempoRequested,
            _document.Settings.Tempo, position?.Tempo, settings.Tempo);
        _instrument?.Present(shownSettings, tempo, phase, plan, frame, blind, _preferences.IsReducedMotion);
        if (_tempoEdit is { } tempoEdit)
        {
            var tempoName = Text.Format("TempoEditName", PulseInstrument.FormatTempo(tempo));
            if (tempoName != _tempoEditName)
            {
                _tempoEditName = tempoName;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tempoEdit, tempoName);
            }
        }
        if (_beatStrip is { } strip && _clickGrid is { } clicks)
        {
            var cell = -1;
            // The strip edits the document's own bar; a program section in another meter has no matching cell to light. The position
            // is the playing audio's, which has not taken a change of unit, subdivision or swing that is still waiting for its
            // splice: until it has, its clicks are not where the strip's cells are, so nothing is lit.
            var stripMeterMatches = meterBeats == _document.Settings.BeatsPerBar && meterDenominator == _document.Settings.BeatDenominator;
            if (stripMeterMatches && clicks.IsPlayedBy(shownSettings) && position is not null && !blind &&
                snapshot.State is TransportState.Playing or TransportState.Paused)
            {
                var withinBar = (position.BeatIndex + position.BeatPhase) * 4d / meterDenominator;
                cell = clicks.CellAt(withinBar, position.IsCountIn);
            }
            strip.Highlight(cell >= strip.Count ? -1 : cell);
        }
        if (_tempoCurve is { } curve)
        {
            EnsureTempoPreview();
            var presentation = _tempoCurveState.Select(_documentRevision, cardPlan, cardFrame, cardBlind);
            curve.Present(presentation.Plan, presentation.Frame, presentation.IsBlind);
        }
        var elapsed = plan is null ? TimeSpan.Zero : TimeSpan.FromSeconds((double)snapshot.ProcessedFrame / plan.SampleRate);
        var time = elapsed.ToString(elapsed.TotalDays >= 1 ? "g" :
            elapsed.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss", Text.Culture);
        long barOffset = 0;
        if (snapshot.Revision is { IsFinite: false } revision && snapshot.PositionFrame >= revision.Initial.TotalFrames)
        {
            var barsPerLoop = revision.Body.SectionBoundaries.Sum(boundary => boundary.Bars);
            var completedLoops = 1 + (snapshot.PositionFrame - revision.Initial.TotalFrames) / revision.Body.TotalFrames;
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
            // Follows the height as well as the width: in a short window a 96 pt number would push the up-next button out of sight.
            _stageTempo.FontSize = Math.Clamp(Math.Min(Root.ActualWidth / 8, Root.ActualHeight / 9), 32, 96);
            _stageTempo.Text = blind ? Text.Get("Silent") : PulseInstrument.FormatTempo(tempo);
            if (_stageMeter is not null)
                _stageMeter.Text = blind ? "" : $"BPM · {PulseInstrument.NoteUnit(settings.BeatUnitQuarters)} · {meterBeats}/{meterDenominator}";
        }
        TransportStatus.Text = $"{state} · {(blind ? Text.Get("Silent") : detail)}" +
            (snapshot.IsMuted ? $" · {Text.Get("Muted")}" : "") +
            (_isOutputMissing ? $"\n{Text.Get("OutputMissing")}" : !HasSelectedOutput ? $"\n{Text.Get("ChooseOutput")}" : "") +
            (_hasPendingEdits ? $"\n{Text.Get("PendingEdits")}" : "");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetItemStatus(TransportStatus,
            $"sound={settings.Sound};accents={string.Join(",", settings.Accents.Select(accent => (int)accent))};override={settings.AccentOverridesGroups};manual={string.Join(",", settings.ManualAccentIndices ?? [])};subdivision={settings.Subdivision};splits={string.Join(",", (settings.SubdivisionAccents ?? []).Select(accent => (int)accent))};tempo={settings.Tempo.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)};meter={meterBeats}/{meterDenominator}");
        if (!blind && snapshot.Revision is { HasPracticeLimit: true } timed &&
            timed.FramesRemainingAt(snapshot.ProcessedFrame) is { } remaining)
        {
            var duration = TimeSpan.FromSeconds(Math.Ceiling((double)remaining / timed.Initial.SampleRate));
            TransportStatus.Text += "\n" + Text.Format("PracticeRemaining", duration.ToString("g", Text.Culture));
        }
        StartButton.IsEnabled = !_isBusy && !_closeFlushing && (snapshot.State == TransportState.Playing || HasSelectedOutput);
        StopButton.IsEnabled = snapshot.State != TransportState.Stopped || IsStartPending;
        var startKey = snapshot.State == TransportState.Playing ? "Pause" : "Start";
        if (_lastStartLabel != Text.Get(startKey))
        {
            LabelButton(StartButton, startKey, startKey == "Pause" ? "\uE769" : "\uE768");
            _lastStartLabel = Text.Get(startKey);
        }
        var muteKey = snapshot.IsMuted ? "Unmute" : "Mute";
        if (_lastMuteLabel != Text.Get(muteKey))
        {
            LabelButton(MuteButton, muteKey, snapshot.IsMuted ? "\uE767" : "\uE74F");
            _lastMuteLabel = Text.Get(muteKey);
        }
        if (_programStatus is not null)
        {
            _programStatus.Text = cardBlind ? Text.Get("Silent") : cardPosition is null ? Text.Get("ProgramIdle") :
                $"{(cardPosition.IsCountIn ? Text.Get("CountInActive") : cardPosition.SectionName)} · {tempo.ToString("0.0", Text.Culture)} BPM · {meterBeats}/{meterDenominator}\n" +
                Text.Format("Progress", TimeSpan.FromSeconds((double)cardFrame / cardPlan!.SampleRate).ToString(@"hh\:mm\:ss", Text.Culture),
                    TimeSpan.FromSeconds((double)cardPlan.TotalFrames / cardPlan.SampleRate).ToString(@"hh\:mm\:ss", Text.Culture));
            if (!cardBlind && cardSection is not null && cardPlan is not null)
            {
                var next = cardPlan.SectionBoundaries.FirstOrDefault(boundary => boundary.StartFrame >= cardSection.EndFrame);
                if (next is not null) _programStatus.Text += "\n" + Text.Format("NextSection", next.Name);
            }
        }
        if (_progress is not null)
        {
            _progress.Visibility = cardBlind ? Visibility.Collapsed : Visibility.Visible;
            _progress.Value = cardPlan is null ? 0 : Math.Clamp((double)cardFrame / cardPlan.TotalFrames, 0, 1);
        }
        if (_isTimingArmed && System.Diagnostics.Stopwatch.GetElapsedTime(_timingStartTimestamp).TotalMinutes >= 10)
            FinishTiming();
    }
}
