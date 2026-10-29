using System.Diagnostics;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using ResonantPulse.App.Audio;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace ResonantPulse.App;

// "Tap to start" (docs/CAPABILITIES.md). The person switches it on, taps the tempo on TAP (or with the T key), and after the chosen
// number of steady taps the tempo is saved and the metronome starts. The counting rules live in TapInSession and the rules of
// what may be pressed and what is said in TapStartRules; this file is the window's side of it: the controls, the order in which
// things happen, and everything that ends it.
public sealed partial class MainWindow
{
    private readonly TapInSession _tapIn = new();
    private ToggleButton? _tapStart;
    private Button? _tapButton;
    private TextBlock? _tapCaption;
    private DispatcherTimer? _tapIdle;
    private string? _plainTapCaption;
    private Action? _tapOnce;
    private bool _tapStartFollowsProgram;
    private string _tapStartHelp = "";
    private (TapStartPhase Phase, TapStartBlock Block, int Taps, int Valid, int Required, string? Plain)? _tapView;
    private DeviceWatcher? _outputWatch;

    private bool IsTapStartOn => _tapIn.IsArmed || _tapIn.IsPending;

    /// <summary>The page's controls are being replaced: nothing of the old ones is kept, and an armed tap-in ends with its page.</summary>
    private void ReleaseTapControls()
    {
        _tapIdle?.Stop();
        _tapIdle = null;
        _tapStart = null;
        _tapButton = null;
        _tapCaption = null;
        _tapOnce = null;
        _plainTapCaption = null;
        _tapStartHelp = "";
        _tapView = null;
    }

    /// <summary>
    /// TAP: a pill with the word and, under it, what the taps have done so far. It sets the tempo as taps come in, or, while Tap to
    /// start is on, counts them towards the start. A project with a program has no tempo of its own to tap, so TAP is only
    /// available while Tap to start listens (see <see cref="TapStartRules.TapEnabled"/>).
    /// </summary>
    private Button BuildTapButton(bool followsProgram)
    {
        _tapStartFollowsProgram = followsProgram;
        var caption = new TextBlock
        {
            Text = Text.Get("TapCaption"), Style = ResonantStyle("CaptionStyle"), TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var title = new TextBlock
        {
            Text = Text.Get("TapShort"), FontSize = 16, FontWeight = FontWeights.SemiBold,
            Style = ResonantStyle("BodyStyle"), HorizontalAlignment = HorizontalAlignment.Center
        };
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(title);
        stack.Children.Add(caption);
        var tap = new Button { Style = ResonantStyle("TapButtonStyle"), Content = stack, IsEnabled = !followsProgram };
        Identify(tap, "TapTempo", Text.Get("Tap"));
        ToolTipService.SetToolTip(tap, Text.Get("TapHelp"));
        AutomationProperties.SetHelpText(tap, Text.Get("TapHelp"));
        var idle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        idle.Tick += (_, _) =>
        {
            idle.Stop();
            _plainTapCaption = null;
            RefreshTapControls();
        };
        _tapIdle = idle;
        _tapCaption = caption;
        _tapButton = tap;
        _tapOnce = TapOnce;
        tap.Click += (_, _) => TapOnce();
        return tap;
    }

    /// <summary>
    /// "Tap to start": a switch beside the tempo controls. Switching it on listens for taps; switching it off (or Stop, Esc, any
    /// other way of starting or changing the tempo, leaving the page, a change of output, sleep or closing the window) ends it.
    /// Whether it is on is never saved, so the metronome always opens stopped and silent.
    /// </summary>
    private ToggleButton BuildTapStart()
    {
        var toggle = new ToggleButton { Style = ResonantStyle("TapStartToggleStyle"), VerticalAlignment = VerticalAlignment.Center };
        LabelButton(toggle, "TapStart", "\uE7C9");
        Identify(toggle, "TapStart", Text.Get("TapStart"));
        toggle.Click += (_, _) =>
        {
            if (toggle.IsChecked == true) SwitchOnTapStart();
            else CancelTapIn();
            // A refused switch must not stay pressed: the controls are brought back in line with what is really going on.
            _tapView = null;
            RefreshSnapshot();
        };
        _tapStart = toggle;
        return toggle;
    }

    /// <summary>
    /// The row under the dial: slower, TAP and faster stay centred, and the switch sits to their right with its words when there
    /// is room beside them, as an icon alone when there is only room for that, and on a row of its own below them otherwise
    /// (<see cref="PlayLayout.ChooseTapStart"/>). Centring is what keeps the dial the one thing the eye lands on.
    /// </summary>
    private Grid BuildTempoRow(StackPanel cluster, ToggleButton toggle)
    {
        var row = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(cluster, 1);
        row.Children.Add(cluster);
        row.Children.Add(toggle);
        var label = toggle.Content is Grid { Children: [_, TextBlock text] } ? text : null;
        double labelledWidth = 0, compactWidth = 0;
        var current = TapStartPlacement.Labelled;
        double MeasureSwitch(bool withLabel)
        {
            if (label is not null) label.Visibility = withLabel ? Visibility.Visible : Visibility.Collapsed;
            toggle.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            // The margin belongs to the placement, which allows for it itself (TapStartGap).
            return toggle.DesiredSize.Width - toggle.Margin.Left - toggle.Margin.Right;
        }
        void Apply(TapStartPlacement placement)
        {
            current = placement;
            if (label is not null)
                label.Visibility = placement is TapStartPlacement.Compact or TapStartPlacement.Appended
                    ? Visibility.Collapsed : Visibility.Visible;
            // Appended: the switch shares the controls' cell and they leave room for it on their right, so that the whole group
            // is what is centred under the dial.
            cluster.Margin = placement == TapStartPlacement.Appended
                ? new Thickness(0, 0, PlayLayout.TapStartSpacing + compactWidth, 0) : new Thickness(0);
            switch (placement)
            {
                case TapStartPlacement.OwnRow:
                    Grid.SetRow(toggle, 1);
                    Grid.SetColumn(toggle, 0);
                    Grid.SetColumnSpan(toggle, 3);
                    toggle.HorizontalAlignment = HorizontalAlignment.Center;
                    toggle.Margin = new Thickness(0, 6, 0, 0);
                    break;
                case TapStartPlacement.Appended:
                    Grid.SetRow(toggle, 0);
                    Grid.SetColumn(toggle, 1);
                    Grid.SetColumnSpan(toggle, 1);
                    toggle.HorizontalAlignment = HorizontalAlignment.Right;
                    toggle.Margin = new Thickness(0);
                    break;
                default:
                    Grid.SetRow(toggle, 0);
                    Grid.SetColumn(toggle, 2);
                    Grid.SetColumnSpan(toggle, 1);
                    toggle.HorizontalAlignment = HorizontalAlignment.Left;
                    toggle.Margin = new Thickness(PlayLayout.TapStartGap, 0, 0, 0);
                    break;
            }
        }
        void Place()
        {
            if (row.ActualWidth <= 0 || cluster.ActualWidth <= 0) return;
            // Both widths are measured once per page and again when the Windows text size changes (the labels resize, the page does not).
            if (labelledWidth <= 0)
            {
                labelledWidth = MeasureSwitch(true);
                compactWidth = MeasureSwitch(false);
            }
            Apply(PlayLayout.ChooseTapStart(row.ActualWidth, cluster.ActualWidth, labelledWidth, compactWidth));
        }
        void FollowTextSize() => row.DispatcherQueue?.TryEnqueue(() =>
        {
            labelledWidth = compactWidth = 0;
            Place();
        });
        row.SizeChanged += (_, _) => Place();
        cluster.SizeChanged += (_, _) => Place();
        // A change of Windows text size resizes the words but not the page, so no page event announces it: a switch whose own
        // width no longer matches what was measured for its form is measured and placed again, as the page tools are.
        toggle.SizeChanged += (_, args) =>
        {
            var cached = current is TapStartPlacement.Compact or TapStartPlacement.Appended ? compactWidth : labelledWidth;
            if (!PlayLayout.StripWidthIsStale(cached, args.NewSize.Width)) return;
            labelledWidth = compactWidth = 0;
            Place();
        };
        row.Loaded += (_, _) =>
        {
            TextScaleChanged += FollowTextSize;
            Place();
        };
        row.Unloaded += (_, _) => TextScaleChanged -= FollowTextSize;
        // The words beside the controls until the first layout says otherwise, as the page's other rows start in their wide form.
        Apply(TapStartPlacement.Labelled);
        return row;
    }

    /// <summary>One tap, from the TAP button or the T key.</summary>
    private void TapOnce()
    {
        if (_isClosed || _closeFlushing || _tapButton is not { IsEnabled: true }) return;
        var now = (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        // The last tap has been taken: the start is on its way and Stop cancels it. Another tap is not a tap of anything.
        if (_tapIn.IsPending) return;
        if (_tapIn.IsArmed)
        {
            TapForStart(now);
            return;
        }
        var estimate = _tap.Tap(now);
        _plainTapCaption = _tap.CurrentBpm is { } bpm
            ? Text.Format("TapProgress", _tap.AcceptedIntervals + 1, bpm.ToString("0.#", Text.Culture))
            : Text.Get("TapAgain");
        _tapIdle?.Stop();
        _tapIdle?.Start();
        RefreshTapControls();
        if (TempoEdits.TryApplyTap(_tempoRequested ?? _document.Settings.Tempo, estimate, out var proposed))
            RequestTempo(proposed);
    }

    private void SwitchOnTapStart()
    {
        if (_isClosed || _closeFlushing) return;
        if (_isLocked)
        {
            ShowNotice("Locked");
            return;
        }
        var idle = _transport.State is TransportState.Stopped or TransportState.Completed;
        switch (TapStartRules.Block(idle, _isBusy, HasSelectedOutput))
        {
            case TapStartBlock.Playing:
                ShowNotice("TapStartStopFirst");
                return;
            case TapStartBlock.NoOutput:
                PromptForOutput();
                return;
            case TapStartBlock.Busy:
                return;
        }
        // Nothing that was about to start may start from here, and a finished run is put back to stopped, so the start after
        // the taps is a fresh one. Taps made before this were plain taps: they are not counted.
        SupersedeTransportIntent();
        InvalidateTiming();
        _transport.Stop();
        _hasPendingEdits = false;
        _tap.Reset();
        _plainTapCaption = null;
        _tapIn.Arm(TapStartRules.ClampTaps(_preferences.TapInCount), _preferences.OutputId);
        if (!StartOutputWatch())
        {
            CancelTapIn();
            ShowNotice("TapStartMonitoring");
            return;
        }
        // The list of outputs is only as fresh as the last time it was read; if the chosen one went away since, the refresh finds
        // it now (and ends the intent), not after the last tap.
        _ = GuardAsync(RefreshOutputsAsync);
    }

    /// <summary>
    /// Ends Tap to start, armed or pending. A start that is still on its way is stopped, so it cannot begin later: the transport's
    /// own startup is cancelled by the same Stop that any other cancelled start gets. A tempo that was already saved stays (it is
    /// one step of Undo); nothing here takes it back. Called from every place that overtakes the intent
    /// (<see cref="SupersedeTransportIntent"/>), so no way of stopping, replacing or leaving can forget it.
    /// </summary>
    private void CancelTapIn(string? noticeKey = null)
    {
        if (!IsTapStartOn) return;
        var wasPending = _tapIn.IsPending;
        _tapIn.Cancel();
        StopOutputWatch();
        if (wasPending)
        {
            // Not SupersedeTransportIntent, which calls this: the start that is on its way is waiting on this very intent.
            _transportIntent++;
            _transport.Stop();
        }
        if (noticeKey is not null && !_isClosed) ShowNotice(noticeKey);
        RefreshSnapshot();
    }

    private void TapForStart(double now)
    {
        // Belt and braces: whatever changed the transport, the project's lock or the output after the switch went on has already
        // ended the intent, but a tap must never count towards a start that could not happen.
        if (_isLocked || !HasSelectedOutput || _transport.State is not (TransportState.Stopped or TransportState.Completed))
        {
            CancelTapIn("TapStartCancelled");
            return;
        }
        var completed = _tapIn.Tap(now);
        var phase = TapStartRules.PhaseOf(_tapIn);
        var counted = TapStartRules.Caption(phase, _tapIn.ValidTaps, _tapIn.RequiredTaps) is { } shown
            ? Text.Format(shown.Key, shown.Args) : "";
        if (_tapIn.Tempo is { } estimate)
            counted += (counted.Length > 0 ? ", " : "") + Text.Format("TempoAnnouncement", PulseInstrument.FormatTempo(estimate));
        AnnounceTap(counted);
        RefreshSnapshot();
        if (completed) _ = GuardAsync(StartFromTapInAsync);
    }

    /// <summary>
    /// The last tap has been taken: the tempo is saved as one edit of the project (and a program is scaled to it), and only then
    /// does the normal start run. Anything that overtakes it on the way (Stop, another start, a change of output, a replaced
    /// project, sleep, closing) ends it before it publishes or before it plays. A tempo that could not be saved, or a project
    /// that is locked, never starts playback.
    /// </summary>
    private async Task StartFromTapInAsync()
    {
        var generation = _tapIn.Generation;
        var intent = _transportIntent;
        var output = _preferences.OutputId;
        var tempo = _tapIn.Tempo ?? throw new InvalidOperationException("A completed tap-in has no tempo.");
        var hasProgram = _document.Program.Count > 0;
        var applied = false;
        bool Wanted() => _tapIn.IsCurrent(generation) && intent == _transportIntent && !_closeFlushing && !_isClosed;
        // Start and a second tap-in wait while this runs; Stop and Esc end it.
        _isBusy = true;
        RefreshSnapshot();
        try
        {
            // The tempo is committed by a pump that submits one step at a time and keeps the newest request outside the edit queue
            // until the step being committed is done: a tempo that was nudged just before the taps could still be on its way and
            // land after the tapped one. A start by hand waits for it first, and so does this one, so the tapped tempo is the last.
            await _tempoPump;
            if (!Wanted()) return;
            await CommitAsync(document =>
            {
                if (!Wanted()) throw new OperationCanceledException();
                TempoEdits.ApplyTapIn(document, tempo);
                applied = true;
            }, editorChange: hasProgram ? EditorChangeKind.Document : EditorChangeKind.GlobalTempoOnly, stillWanted: Wanted);
            // A locked project reports its notice without applying anything, and the start was overtaken if the intent moved on.
            if (!applied || !Wanted()) return;
            if (_transport.State is not (TransportState.Stopped or TransportState.Completed) || !HasSelectedOutput ||
                !string.Equals(output, _preferences.OutputId, StringComparison.Ordinal))
            {
                ShowNotice("TapStartCancelled");
                return;
            }
            if (hasProgram) ShowNoticeText(Text.Format("TapStartScaled", PulseInstrument.FormatTempo(tempo)));
            InvalidateTiming();
            var startRevision = _documentRevision;
            await _transport.StartAsync(_document, output);
            _hasPendingEdits = startRevision != _documentRevision;
        }
        catch (OperationCanceledException) { }
        finally
        {
            // Only the intent that is still this one ends here: after a Stop, a new switch-on may already own the state.
            if (_tapIn.IsCurrent(generation))
            {
                _tapIn.Finish(generation);
                StopOutputWatch();
            }
            _isBusy = false;
            RefreshSnapshot();
        }
    }

    /// <summary>Brings the switch, TAP and what is under it in line with the intent and with what the transport is doing.</summary>
    private void RefreshTapControls()
    {
        if (_tapStart is not { } toggle || _tapButton is not { } tap || _tapCaption is not { } caption) return;
        var phase = TapStartRules.PhaseOf(_tapIn);
        var on = phase != TapStartPhase.Off;
        var idle = _transport.State is TransportState.Stopped or TransportState.Completed;
        var block = TapStartRules.Block(idle, _isBusy, HasSelectedOutput);
        var taps = on ? _tapIn.RequiredTaps : TapStartRules.ClampTaps(_preferences.TapInCount);
        // This runs with every refresh of the window (30 times a second): what is on screen is only touched when what it is made
        // from has changed.
        var view = (phase, block, taps, _tapIn.ValidTaps, _tapIn.RequiredTaps, _plainTapCaption);
        if (_tapView == view) return;
        _tapView = view;
        if ((toggle.IsChecked == true) != on) toggle.IsChecked = on;
        // While it is on it can always be switched off; while it is off it can be switched on when nothing prevents it.
        toggle.IsEnabled = on || block == TapStartBlock.None;
        var help = Text.Format(TapStartRules.HelpKey(phase, block, _tapStartFollowsProgram), taps);
        if (help != _tapStartHelp)
        {
            _tapStartHelp = help;
            ToolTipService.SetToolTip(toggle, help);
            AutomationProperties.SetHelpText(toggle, help);
        }
        tap.IsEnabled = TapStartRules.TapEnabled(_tapStartFollowsProgram, phase is TapStartPhase.Counting or TapStartPhase.Uneven);
        var text = TapStartRules.Caption(phase, _tapIn.ValidTaps, _tapIn.RequiredTaps) is { } shown
            ? Text.Format(shown.Key, shown.Args) : _plainTapCaption ?? Text.Get("TapCaption");
        if (caption.Text != text) caption.Text = text;
        AutomationProperties.SetItemStatus(tap, TapStartRules.Status(phase, _tapIn.ValidTaps, _tapIn.RequiredTaps));
    }

    /// <summary>Tells assistive technology how the count stands, without moving the focus (only the latest is spoken).</summary>
    private void AnnounceTap(string text)
    {
        if (_tapButton is not { } tap || text.Length == 0) return;
        var peer = FrameworkElementAutomationPeer.FromElement(tap) ?? FrameworkElementAutomationPeer.CreatePeerForElement(tap);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent,
            text, "TapStartProgress");
    }

    /// <summary>
    /// True while a panel or a menu is open. Esc closes that first and the T key is not a tap then, so a key meant for a panel
    /// never taps or cancels behind it. A tooltip is not a panel.
    /// </summary>
    private bool HasOpenPanel() =>
        Root.XamlRoot is { } xamlRoot &&
        VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Any(popup => popup.Child is not ToolTip);

    // ---- the output device, watched only while Tap to start is on -------------------------------------------------------

    /// <summary>
    /// While Tap to start is on no audio graph exists yet, so nothing would report that the chosen output went away: the start
    /// would only fail after the last tap, or play on a device that was unplugged and plugged in again. Windows reports a
    /// removed or changed output, and that ends the intent at once; plugging it in again never turns it back on. The watch
    /// lives only as long as the intent. False when Windows cannot watch the outputs, and then Tap to start stays off.
    /// </summary>
    private bool StartOutputWatch()
    {
        StopOutputWatch();
        try
        {
            var watcher = DeviceInformation.CreateWatcher(MediaDevice.GetAudioRenderSelector());
            watcher.Removed += OutputWatchChanged;
            watcher.Updated += OutputWatchChanged;
            watcher.Stopped += OutputWatchStopped;
            _outputWatch = watcher;
            watcher.Start();
            return true;
        }
        catch (Exception exception)
        {
            Diagnostics.Write("Output watch could not start", exception);
            StopOutputWatch();
            return false;
        }
    }

    private void StopOutputWatch()
    {
        var watcher = _outputWatch;
        _outputWatch = null;
        if (watcher is null) return;
        watcher.Removed -= OutputWatchChanged;
        watcher.Updated -= OutputWatchChanged;
        watcher.Stopped -= OutputWatchStopped;
        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
        }
        catch (Exception exception) { Diagnostics.Write("Output watch could not stop", exception); }
    }

    // Both handlers run on a worker thread of Windows: they only hand over to the window's own thread.
    private void OutputWatchChanged(DeviceWatcher sender, DeviceInformationUpdate update) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || !ReferenceEquals(sender, _outputWatch)) return;
            if (_tapIn.IsAffectedByOutput(update.Id)) CancelTapIn("TapStartOutputChanged");
        });

    private void OutputWatchStopped(DeviceWatcher sender, object args) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_isClosed || !ReferenceEquals(sender, _outputWatch)) return;
            // A watch that ended by itself can no longer tell: failing closed means the intent ends, with the reason.
            CancelTapIn("TapStartMonitoring");
        });
}
