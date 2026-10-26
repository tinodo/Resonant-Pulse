using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;
using Windows.System;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private BeatStrip? _beatStrip;
    private Button? _tempoEdit;
    private string _tempoEditName = "";
    private double? _tempoRequested;

    private static Style ResonantStyle(string key) => (Style)Application.Current.Resources[key];

    private UIElement BuildPlay()
    {
        var settings = _document.Settings;
        // A program carries its own tempo and meter per section; the single-tempo controls would change nothing audible.
        var followsProgram = _document.Program.Count > 0;
        _tempoRequested = null;

        // The metronome itself: a large dial whose BPM readout opens the exact tempo editor.
        var instrument = new PulseInstrument
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
        };
        _instrument = instrument;
        var tempoPanel = BuildTempoPanel();
        var tempoEdit = new Button
        {
            Flyout = tempoPanel.Flyout, Style = ResonantStyle("GhostButtonStyle"), Padding = new Thickness(0),
            MinWidth = 0, MinHeight = 0, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        Identify(tempoEdit, "TempoEdit", Text.Get("TempoEditHint"));
        ToolTipService.SetToolTip(tempoEdit, Text.Get("TempoEditHint"));
        // The current value is part of the button's name (kept current by RefreshSnapshot), so a screen reader
        // that lands here hears the tempo, not only the instruction.
        _tempoEditName = "";
        _tempoEdit = followsProgram ? null : tempoEdit;
        tempoEdit.KeyDown += (_, args) =>
        {
            var step = args.Key switch
            {
                VirtualKey.Up => 1, VirtualKey.Down => -1, VirtualKey.PageUp => 10, VirtualKey.PageDown => -10, _ => 0
            };
            if (step == 0) return;
            NudgeTempo(step);
            args.Handled = true;
        };
        var dial = new Grid { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        dial.Children.Add(instrument);
        if (!followsProgram) dial.Children.Add(tempoEdit);
        dial.PointerWheelChanged += (_, args) =>
        {
            var delta = args.GetCurrentPoint(dial).Properties.MouseWheelDelta;
            if (delta == 0) return;
            NudgeTempo(Math.Sign(delta) * (args.KeyModifiers.HasFlag(VirtualKeyModifiers.Control) ? 10 : 1));
            args.Handled = true;
        };
        var hero = new Grid();
        hero.Children.Add(dial);

        // Page tools. They act on the session being played (practice limits, saving and opening it, help), so they belong on
        // this page rather than in Settings, but stay secondary to the instrument: one quiet strip in the corner under the
        // Settings item, in the same full-contrast text.
        var practice = BuildPracticePanel();
        var project = BuildProjectPanel();
        var help = BuildHelpPanel();
        var rail = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top
        };
        Identify(rail, "PlaySettingsStrip", Text.Get("PlayTools"));
        var railLabels = new List<TextBlock>();
        foreach (var (panel, key, id, glyph, hintKey) in new[]
        {
            (practice, "PracticeTab", "PracticeTimer", "\uE916", "PracticeTimer"),
            (project, "ProjectTab", "ProjectOptions", "\uE8B7", "ProjectOptions"),
            (help, "HelpTab", "PlayHelp", "\uE897", "Help")
        })
        {
            var button = new Button { Flyout = panel.Flyout, Style = ResonantStyle("ToolButtonStyle") };
            LabelButton(button, key, glyph);
            if (button.Content is StackPanel { Children: [_, TextBlock label] }) railLabels.Add(label);
            Identify(button, id, Text.Get(key));
            // The tooltip carries the full name, which matters most when only the icon is shown.
            ToolTipService.SetToolTip(button, Text.Get(hintKey));
            rail.Children.Add(button);
        }
        var railInRow = false;
        var railLabelsShown = true;
        double railLabelledWidth = 0, railIconsWidth = 0;
        double MeasureRail(bool labels)
        {
            foreach (var label in railLabels) label.Visibility = labels ? Visibility.Visible : Visibility.Collapsed;
            rail.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            return rail.DesiredSize.Width - rail.Margin.Left - rail.Margin.Right;
        }
        void PlaceRail()
        {
            if (hero.ActualWidth <= 0 || hero.ActualHeight <= 0) return;
            if (railLabelledWidth <= 0)
            {
                railLabelledWidth = MeasureRail(true);
                railIconsWidth = MeasureRail(false);
            }
            // The room the dial would have if the strip took none of it. Taking a row of its own shrinks the hero, so that
            // height is added back; otherwise the answer could flip every time the strip moved.
            var room = hero.ActualHeight + (railInRow ? Math.Max(PlayLayout.StripHeight, rail.ActualHeight) : 0);
            var plan = PlayLayout.Choose(hero.ActualWidth, room, railLabelledWidth, railIconsWidth);
            foreach (var label in railLabels) label.Visibility = plan.ShowLabels ? Visibility.Visible : Visibility.Collapsed;
            railLabelsShown = plan.ShowLabels;
            railInRow = plan.Placement == RailPlacement.Row;
            Grid.SetRow(rail, railInRow ? 0 : 1);
            // The buttons' own padding hangs outside the page edge, so the last label (or icon) ends exactly where the
            // Settings item's text ends.
            rail.Margin = new Thickness(0, 0, plan.ShowLabels ? -12 : -14, 0);
        }
        // The widths are measured once per page, so a change of Windows text size (which resizes the labels but not the
        // page) would leave them stale. The strip's own width then no longer matches the measurement for its current
        // mode: measure again and place it again.
        rail.SizeChanged += (_, args) =>
        {
            if (!PlayLayout.StripWidthIsStale(railLabelsShown ? railLabelledWidth : railIconsWidth, args.NewSize.Width)) return;
            railLabelledWidth = railIconsWidth = 0;
            PlaceRail();
        };

        void SizeInstrument()
        {
            PlaceRail();
            var size = PlayLayout.DialSize(hero.ActualWidth, hero.ActualHeight);
            dial.Width = dial.Height = size;
            var scale = size / 440d;
            // The readout and unit label sit at fixed canvas coordinates inside the dial.
            tempoEdit.Width = 290 * scale;
            tempoEdit.Height = 140 * scale;
            tempoEdit.Margin = new Thickness(0, 128 * scale, 0, 0);
        }
        hero.SizeChanged += (_, _) => SizeInstrument();

        // Tempo: slower, tap, faster. Always live.
        var slower = Stepper("\uE738", "TempoDown", "TempoSlower", -1);
        var faster = Stepper("\uE710", "TempoUp", "TempoFaster", 1);
        var tapCaption = new TextBlock
        {
            Text = Text.Get("TapCaption"), Style = ResonantStyle("CaptionStyle"), TextWrapping = TextWrapping.NoWrap,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        var tapTitle = new TextBlock
        {
            Text = Text.Get("TapShort"), FontSize = 16, FontWeight = FontWeights.SemiBold,
            Style = ResonantStyle("BodyStyle"), HorizontalAlignment = HorizontalAlignment.Center
        };
        var tapStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tapStack.Children.Add(tapTitle);
        tapStack.Children.Add(tapCaption);
        var tap = new Button { Style = ResonantStyle("TapButtonStyle"), Content = tapStack };
        Identify(tap, "TapTempo", Text.Get("Tap"));
        ToolTipService.SetToolTip(tap, Text.Get("TapHelp"));
        AutomationProperties.SetHelpText(tap, Text.Get("TapHelp"));
        var tapIdle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
        tapIdle.Tick += (_, _) => { tapIdle.Stop(); tapCaption.Text = Text.Get("TapCaption"); };
        tap.Click += (_, _) =>
        {
            var estimate = _tap.Tap((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
            tapCaption.Text = _tap.CurrentBpm is { } bpm
                ? Text.Format("TapProgress", _tap.AcceptedIntervals + 1, bpm.ToString("0.#", Text.Culture))
                : Text.Get("TapAgain");
            tapIdle.Stop();
            tapIdle.Start();
            if (TempoEdits.TryApplyTap(_tempoRequested ?? _document.Settings.Tempo, estimate, out var proposed))
                RequestTempo(proposed);
        };
        var tempoCluster = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center
        };
        tempoCluster.Children.Add(slower);
        tempoCluster.Children.Add(tap);
        tempoCluster.Children.Add(faster);
        Identify(tempoCluster, "PlayQuickControls", Text.Get("Tempo"));
        var unitNotice = !followsProgram && MeterPresets.UnitNeedsFix(settings) ? BuildUnitNotice(settings) : null;
        var tempoArea = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        if (unitNotice is not null) tempoArea.Children.Add(unitNotice);
        tempoArea.Children.Add(tempoCluster);
        if (followsProgram)
        {
            slower.IsEnabled = faster.IsEnabled = tap.IsEnabled = false;
            var programNote = new TextBlock
            {
                Text = Text.Get("ProgramControlsNote"), Style = ResonantStyle("CaptionStyle"), TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, MaxWidth = 420
            };
            Identify(programNote, "ProgramControlsNote", programNote.Text);
            tempoArea.Children.Add(programNote);
        }

        // Time signature (sheet-music quick picks; the full builder is behind "Custom…"), beat strength, sound.
        var rhythm = BuildRhythmPanel();
        var meter = BuildMeterPicker(rhythm);
        meter.IsEnabled = !followsProgram;
        var sound = Select("Sound", "MainSound", settings.Sound, "Wood", "Glass", "Click", "Kick", "HiHat");
        var restoringSound = false;
        sound.Header = null;
        sound.Width = 152;
        sound.Height = 44;
        sound.SelectionChanged += async (_, _) =>
        {
            if (restoringSound) return;
            var selectedSound = Selected(sound);
            await GuardAsync(async () =>
            {
                try
                {
                    await CommitAsync(doc => doc.Settings.Sound = selectedSound,
                        editorChange: EditorChangeKind.SoundAndAccentsOnly);
                }
                finally
                {
                    if (Selected(sound) != _document.Settings.Sound)
                    {
                        restoringSound = true;
                        try { sound.SelectedItem = sound.Items.OfType<Choice>().Single(choice => choice.Value == _document.Settings.Sound); }
                        finally { restoringSound = false; }
                    }
                }
            });
        };
        AutomationProperties.SetHelpText(sound, Text.Get("LiveSoundHint"));
        ToolTipService.SetToolTip(sound, Text.Get("LiveSoundHint"));

        var pulses = PrimaryAccentCount(settings);
        var strip = new BeatStrip { Describe = DescribeBeat };
        _beatStrip = strip;
        strip.Rebuild(pulses, GroupStartPulses(settings, pulses), 44, AccentPatterns.ExpandEffectiveMain(settings, pulses));
        strip.BeatInvoked += async (_, index) => await GuardAsync(() => ChangeAccentAsync(index, 1, wrap: true));
        strip.BeatNudged += async (_, args) => await GuardAsync(() => ChangeAccentAsync(args.Index, args.Delta, wrap: false));
        var stripScroll = new ScrollViewer
        {
            Content = strip, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Identify(stripScroll, "PulseAccentStrip", Text.Get("PulseStrengths"));
        var cue = new TextBlock
        {
            Text = Text.Get("MorePulses"), Style = ResonantStyle("CaptionStyle"), Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Identify(cue, "PlayScrollHint", Text.Get("ScrollControlsHint"));
        stripScroll.SizeChanged += (_, args) =>
        {
            var available = Math.Max(0, args.NewSize.Width - 8);
            var cell = Math.Clamp(available / Math.Max(1, pulses), 32, 56);
            strip.SetCellWidth(cell);
            // Judged from the data, not from the scrollbar the cue itself could create.
            cue.Visibility = pulses * 32 > available ? Visibility.Visible : Visibility.Collapsed;
        };

        var meterColumn = Captioned("TimeSignature", meter);
        var strengthText = new TextBlock
        {
            Text = Text.Get("BeatStrengthCaption"), Style = ResonantStyle("CaptionStyle"),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
        };
        var strengthMenu = BuildStrengthMenu();
        var strengthCaption = new Grid { ColumnSpacing = 6 };
        strengthCaption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        strengthCaption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        strengthCaption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cue.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(cue, 1);
        Grid.SetColumn(strengthMenu, 2);
        strengthCaption.Children.Add(strengthText);
        strengthCaption.Children.Add(cue);
        strengthCaption.Children.Add(strengthMenu);
        var stripColumn = new StackPanel { Spacing = 6 };
        stripColumn.Children.Add(strengthCaption);
        stripColumn.Children.Add(stripScroll);
        var soundColumn = Captioned("Sound", sound);
        var context = new Grid { ColumnSpacing = 20, RowSpacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch, MaxWidth = 720 };
        context.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        context.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        context.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        context.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        context.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        context.Children.Add(meterColumn);
        context.Children.Add(stripColumn);
        context.Children.Add(soundColumn);
        void ArrangeContext(bool narrow)
        {
            sound.Width = narrow ? 128 : 152;
            Grid.SetRow(meterColumn, 0); Grid.SetColumn(meterColumn, 0); Grid.SetColumnSpan(meterColumn, narrow ? 2 : 1);
            Grid.SetRow(soundColumn, 0); Grid.SetColumn(soundColumn, 2);
            Grid.SetRow(stripColumn, narrow ? 1 : 0); Grid.SetColumn(stripColumn, narrow ? 0 : 1);
            Grid.SetColumnSpan(stripColumn, narrow ? 3 : 1);
            meterColumn.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            soundColumn.HorizontalAlignment = narrow ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        }
        ArrangeContext(false);

        var page = new Grid { RowSpacing = 10 };
        page.SizeChanged += (_, args) =>
        {
            ArrangeContext(args.NewSize.Width < 620);
            if (unitNotice is not null)
            {
                // The notice only appears when the dial keeps a usable size; a wide row lets the action sit beside the title.
                unitNotice.Visibility = PlayLayout.NoticeFits(args.NewSize.Width, args.NewSize.Height)
                    ? Visibility.Visible : Visibility.Collapsed;
                unitNotice.Width = Math.Min(640, args.NewSize.Width);
            }
            SizeInstrument();
        };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // The strip comes first so keyboard order follows the screen: tools, instrument, tempo, then the settings below.
        Grid.SetRow(rail, 1);
        page.Children.Add(rail);
        Grid.SetRow(hero, 1);
        page.Children.Add(hero);
        Grid.SetRow(tempoArea, 2);
        page.Children.Add(tempoArea);
        Grid.SetRow(context, 3);
        page.Children.Add(context);
        return page;
    }

    private static StackPanel Captioned(string captionKey, FrameworkElement control)
    {
        var column = new StackPanel { Spacing = 6 };
        column.Children.Add(new TextBlock { Text = Text.Get(captionKey), Style = ResonantStyle("CaptionStyle") });
        column.Children.Add(control);
        return column;
    }

    private RepeatButton Stepper(string glyph, string id, string helpKey, int direction)
    {
        var button = new RepeatButton
        {
            Style = ResonantStyle("StepperButtonStyle"), Delay = 450, Interval = 80,
            Content = new FontIcon { Glyph = glyph, FontSize = 18 }
        };
        Identify(button, id, Text.Get(helpKey));
        ToolTipService.SetToolTip(button, Text.Get(helpKey));
        button.Click += (_, _) =>
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            NudgeTempo(direction * (ctrl ? 10 : 1));
        };
        return button;
    }

    private void NudgeTempo(double delta) => RequestTempo((_tempoRequested ?? _document.Settings.Tempo) + delta);

    private bool _tempoPumpRunning;

    /// <summary>
    /// Shows the requested tempo immediately and commits it. While a commit is running, further requests only
    /// update the wanted value; the running pump then commits the newest one directly. Holding a stepper or tapping
    /// quickly therefore never queues a backlog, never reorders, and the last value always wins.
    /// </summary>
    private void RequestTempo(double bpm)
    {
        if (_document.Program.Count > 0) return;
        var requested = Math.Clamp(Math.Round(bpm, 1), 1, 1000);
        if (!TempoEdits.IsChange(_tempoRequested ?? _document.Settings.Tempo, requested)) return;
        _tempoRequested = requested;
        AnnounceTempo(requested);
        RefreshSnapshot();
        if (_tempoPumpRunning) return;
        _tempoPumpRunning = true;
        _ = GuardAsync(async () =>
        {
            try
            {
                var committed = double.NaN;
                while (_tempoRequested is { } wanted && wanted != committed)
                {
                    committed = wanted;
                    await CommitAsync(doc => doc.Settings.Tempo = wanted, editorChange: EditorChangeKind.GlobalTempoOnly);
                }
            }
            finally
            {
                _tempoPumpRunning = false;
                _tempoRequested = null;
                if (!_isClosed) RefreshSnapshot();
            }
        });
    }

    private string DescribeBeat(int index, Accent level) =>
        Text.Format("BeatName", index + 1, Text.Get("Level" + level));

    /// <summary>
    /// Tells assistive technology the new tempo without moving focus. Only the most recent announcement is spoken,
    /// so holding a stepper reads the final value, not every step.
    /// </summary>
    private void AnnounceTempo(double bpm)
    {
        if (_tempoEdit is not { } edit) return;
        var peer = FrameworkElementAutomationPeer.FromElement(edit) ?? FrameworkElementAutomationPeer.CreatePeerForElement(edit);
        peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.MostRecent,
            Text.Format("TempoAnnouncement", PulseInstrument.FormatTempo(bpm)), "TempoChanged");
    }

    private async Task ChangeAccentAsync(int index, int delta, bool wrap)
    {
        try
        {
            await CommitAsync(doc =>
            {
                var count = Math.Max(doc.Settings.Accents.Length, PrimaryAccentCount(doc.Settings));
                var chosen = AccentPatterns.ExpandEffectiveMain(doc.Settings, count)[index];
                var pattern = AccentPatterns.ExpandMain(doc.Settings.Accents, count);
                var next = (int)chosen + delta;
                pattern[index] = (Accent)(wrap ? (next % 4 + 4) % 4 : Math.Clamp(next, 0, 3));
                doc.Settings.Accents = pattern;
                doc.Settings.ManualAccentIndices = (doc.Settings.ManualAccentIndices ?? []).Append(index).Distinct().Order().ToArray();
            }, editorChange: EditorChangeKind.SoundAndAccentsOnly);
        }
        finally { RefreshBeatStrength(); }
    }

    private void RefreshBeatStrength()
    {
        if (_beatStrip is not { } strip) return;
        strip.SetLevels(AccentPatterns.ExpandEffectiveMain(_document.Settings,
            Math.Max(strip.Count, PrimaryAccentCount(_document.Settings))));
    }

    /// <summary>Commands that act on the whole strip. They sit outside the rhythm form, so each applies at once.</summary>
    private Button BuildStrengthMenu()
    {
        var flyout = new Flyout { Placement = FlyoutPlacementMode.Top };
        var items = new StackPanel { Spacing = 4, MinWidth = 190 };
        foreach (var (key, id, allNormal) in new[] { ("AllNormal", "AllNormalAccents", true), ("DefaultAccents", "DefaultAccents", false) })
        {
            var item = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left };
            LabelButton(item, key);
            Identify(item, id, Text.Get(key));
            item.Click += async (_, _) =>
            {
                flyout.Hide();
                await GuardAsync(() => ResetStrengthAsync(allNormal));
            };
            items.Children.Add(item);
        }
        items.KeyDown += OnKeyDown;
        flyout.Content = items;
        flyout.Opening += (_, _) => items.RequestedTheme = Root.RequestedTheme;
        // The tap target is 28 DIP tall, but its slot matches the caption line so the dial keeps its height.
        var button = new Button
        {
            Style = ResonantStyle("GhostButtonStyle"), MinHeight = 28, Height = 28, MinWidth = 36, Padding = new Thickness(0),
            Margin = new Thickness(0, -6, 0, -6), Content = new FontIcon { Glyph = "\uE712", FontSize = 14 }, Flyout = flyout
        };
        Identify(button, "StrengthMenu", Text.Get("ResetStrengths"));
        ToolTipService.SetToolTip(button, Text.Get("ResetStrengths"));
        return button;
    }

    private async Task ResetStrengthAsync(bool allNormal)
    {
        try
        {
            await CommitAsync(doc =>
            {
                if (allNormal)
                {
                    doc.Settings.Accents = Enumerable.Repeat(Accent.Normal, PrimaryAccentCount(doc.Settings)).ToArray();
                    doc.Settings.AccentOverridesGroups = true;
                }
                else
                {
                    doc.Settings.Accents = MeterPresets.DefaultAccents(PrimaryAccentCount(doc.Settings));
                    doc.Settings.AccentOverridesGroups = false;
                    doc.Settings.ManualAccentIndices = null;
                }
            }, editorChange: EditorChangeKind.SoundAndAccentsOnly);
        }
        finally { RefreshBeatStrength(); }
    }

    /// <summary>Pulse indices at which an additive group starts (for spacing the beat strip).</summary>
    private static HashSet<int> GroupStartPulses(PulseSettings settings, int count)
    {
        var starts = new HashSet<int>();
        var unit = Rational.FromDouble(settings.BeatUnitQuarters);
        var boundary = 0;
        foreach (var group in settings.Groups)
        {
            var position = new Rational(4 * boundary, settings.BeatDenominator) / unit;
            if (position.Denominator.IsOne && position.Numerator >= 0 && position.Numerator < count)
                starts.Add((int)position.Numerator);
            boundary += group;
        }
        return starts;
    }

    private static int PrimaryAccentCount(PulseSettings settings) => MeterPresets.PulsesPerBar(settings);

    private static StackPanel TimeSignatureGlyph(int beats, int denominator, double size)
    {
        // Tight line bounds trim each numeral to its cap height and baseline, so the pair is centred by what is drawn. The
        // default bounds add empty space below the digits, which made them ride high in their chip.
        var glyph = new StackPanel
        {
            Spacing = size * 0.16, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var value in new[] { beats, denominator })
            glyph.Children.Add(new TextBlock
            {
                Text = value.ToString(CultureInfo.InvariantCulture), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Bahnschrift"),
                FontWeight = FontWeights.SemiBold, FontSize = size, TextLineBounds = TextLineBounds.Tight,
                TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                Style = ResonantStyle("BodyStyle")
            });
        return glyph;
    }

    /// <summary>
    /// Shown when the saved BPM unit does not divide the bar evenly (an older or imported project), because the clicks then
    /// drift against the dial. One click applies the unit that matches the written beats. It is kept short (the reason is
    /// in its tooltip and help text), only appears while the dial keeps a usable size (<see cref="PlayLayout.NoticeFits"/>)
    /// and can be dismissed for this view.
    /// </summary>
    private InfoBar BuildUnitNotice(PulseSettings settings)
    {
        var fix = new Button { Content = Text.Format("UnitMismatchFix", PulseInstrument.NoteUnit(4d / settings.BeatDenominator)) };
        Identify(fix, "UnitMismatchFix", (string)fix.Content);
        fix.Click += async (_, _) => await GuardAsync(() =>
        {
            var current = _document.Settings;
            return ApplyMeterPresetAsync(new MeterPreset(current.BeatsPerBar, current.BeatDenominator, current.Groups));
        });
        var title = Text.Get("UnitMismatchTitle");
        var reason = Text.Format("UnitMismatchMessage", PulseInstrument.NoteUnit(settings.BeatUnitQuarters),
            settings.BeatsPerBar, settings.BeatDenominator);
        var notice = new InfoBar
        {
            Severity = InfoBarSeverity.Warning, IsOpen = true, IsClosable = true, Title = title, ActionButton = fix,
            MaxWidth = 640, HorizontalAlignment = HorizontalAlignment.Center
        };
        Identify(notice, "UnitMismatchNotice", title);
        AutomationProperties.SetHelpText(notice, reason);
        ToolTipService.SetToolTip(notice, reason);
        return notice;
    }

    private Button BuildMeterPicker(OptionPanel rhythm)
    {
        var settings = _document.Settings;
        var chip = new Button { Style = ResonantStyle("ChipButtonStyle"), Width = 96, Height = 64, Padding = new Thickness(0) };
        var face = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        face.Children.Add(TimeSignatureGlyph(settings.BeatsPerBar, settings.BeatDenominator, 24));
        face.Children.Add(new FontIcon { Glyph = "\uE70D", FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        chip.Content = face;
        Identify(chip, "QuickMeter", $"{Text.Get("TimeSignature")}: {settings.BeatsPerBar}/{settings.BeatDenominator}");
        ToolTipService.SetToolTip(chip, Text.Get("MeterChange"));

        var picker = new Flyout { Placement = FlyoutPlacementMode.Top };
        var title = new TextBlock
        {
            Text = Text.Get("TimeSignature"), FontSize = 16, FontWeight = FontWeights.SemiBold, Style = ResonantStyle("BodyStyle")
        };
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        const int columns = 4;
        for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var row = 0; row < (MeterPresets.All.Count + columns - 1) / columns; row++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var index = 0; index < MeterPresets.All.Count; index++)
        {
            var preset = MeterPresets.All[index];
            var cell = new Button
            {
                Style = ResonantStyle("ChipButtonStyle"), Width = 68, Height = 68, Padding = new Thickness(0),
                Content = TimeSignatureGlyph(preset.Beats, preset.Denominator, 22)
            };
            Identify(cell, $"MeterPreset{preset.Beats}_{preset.Denominator}", $"{preset.Beats}/{preset.Denominator}");
            if (MeterPresets.IsCurrent(settings, preset))
            {
                cell.BorderThickness = new Thickness(2);
                cell.Loaded += (_, _) => cell.BorderBrush = ThemeBrushes.Resolve(cell, "PulseBrush");
            }
            cell.Click += async (_, _) =>
            {
                picker.Hide();
                await GuardAsync(() => ApplyMeterPresetAsync(preset));
            };
            Grid.SetColumn(cell, index % columns);
            Grid.SetRow(cell, index / columns);
            grid.Children.Add(cell);
        }
        var openCustomWhenClosed = false;
        var custom = ActionButton("MeterCustom", "MeterCustom", () =>
        {
            openCustomWhenClosed = true;
            picker.Hide();
        });
        // Showing the builder only once this flyout has fully closed keeps two flyouts on one anchor from racing.
        picker.Closed += (_, _) =>
        {
            if (!openCustomWhenClosed) return;
            openCustomWhenClosed = false;
            DispatcherQueue.TryEnqueue(() =>
            {
                // The page may have been rebuilt (or the window closed) since the picker closed.
                if (!_isClosed && chip.XamlRoot is not null) rhythm.Flyout.ShowAt(chip);
            });
        };
        custom.HorizontalAlignment = HorizontalAlignment.Stretch;
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(title);
        content.Children.Add(grid);
        content.Children.Add(custom);
        content.KeyDown += OnKeyDown;
        picker.Content = content;
        picker.Opening += (_, _) => content.RequestedTheme = Root.RequestedTheme;
        chip.Flyout = picker;
        return chip;
    }

    private async Task ApplyMeterPresetAsync(MeterPreset preset)
    {
        if (MeterPresets.IsApplied(_document.Settings, preset)) return;
        var applied = false;
        await CommitAsync(doc =>
        {
            MeterPresets.Apply(doc.Settings, preset.Beats, preset.Denominator, preset.Groups);
            applied = true;
        }, editorChange: EditorChangeKind.PlayStructure);
        if (applied) BuildPage();
    }
}
