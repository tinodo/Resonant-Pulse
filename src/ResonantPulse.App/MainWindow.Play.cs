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
    // Width reserved on both sides of the instrument when the secondary rail is docked, so the
    // instrument stays exactly centred.
    private const double RailReserve = 272;
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
        var railDocked = false;
        void SizeInstrument()
        {
            var width = hero.ActualWidth - (railDocked ? RailReserve : 0);
            var size = Math.Clamp(Math.Min(width, hero.ActualHeight), 0, 560);
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
        var tempoArea = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
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

        // Secondary entry points: quiet, icon-led, never competing with the instrument.
        var practice = BuildPracticePanel();
        var project = BuildProjectPanel();
        var help = BuildHelpPanel();
        var rail = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        var railButtons = new List<Button>();
        foreach (var (panel, key, id, glyph) in new[]
        {
            (practice, "PracticeTab", "PracticeTimer", "\uE916"), (project, "ProjectTab", "ProjectOptions", "\uE8B7"),
            (help, "HelpTab", "PlayHelp", "\uE897")
        })
        {
            var button = new Button
            {
                Flyout = panel.Flyout, Style = ResonantStyle("GhostButtonStyle"), MinWidth = 108,
                HorizontalContentAlignment = HorizontalAlignment.Left
            };
            LabelButton(button, key, glyph);
            Identify(button, id, Text.Get(key));
            ToolTipService.SetToolTip(button, Text.Get(key));
            rail.Children.Add(button);
            railButtons.Add(button);
        }
        var railScroll = new ScrollViewer
        {
            Content = rail, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Identify(railScroll, "PlaySettingsStrip", Text.Get("Settings"));

        var page = new Grid { RowSpacing = 10 };
        page.SizeChanged += (_, args) =>
        {
            var width = args.NewSize.Width;
            railDocked = width >= 620;
            rail.Orientation = railDocked ? Orientation.Vertical : Orientation.Horizontal;
            rail.Spacing = railDocked ? 4 : 8;
            railScroll.Width = railDocked ? 124 : double.NaN;
            railScroll.HorizontalAlignment = railDocked ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            railScroll.VerticalAlignment = railDocked ? VerticalAlignment.Center : VerticalAlignment.Stretch;
            railScroll.HorizontalScrollMode = railDocked ? ScrollMode.Disabled : ScrollMode.Enabled;
            railScroll.HorizontalScrollBarVisibility = railDocked ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            railScroll.VerticalScrollMode = railDocked ? ScrollMode.Enabled : ScrollMode.Disabled;
            railScroll.VerticalScrollBarVisibility = railDocked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            Grid.SetRow(railScroll, railDocked ? 1 : 0);
            // On very narrow windows the quiet entry points shrink to their icons (names stay in tooltips and for screen readers).
            foreach (var railButton in railButtons)
            {
                if (railButton.Content is StackPanel { Children: [_, TextBlock label] })
                    label.Visibility = width < 420 ? Visibility.Collapsed : Visibility.Visible;
                railButton.MinWidth = width < 420 ? 44 : 108;
            }
            ArrangeContext(width < 620);
            SizeInstrument();
        };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(hero, 1);
        page.Children.Add(hero);
        page.Children.Add(railScroll);
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
        var glyph = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        foreach (var value in new[] { beats, denominator })
            glyph.Children.Add(new TextBlock
            {
                Text = value.ToString(CultureInfo.InvariantCulture), FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Bahnschrift"),
                FontWeight = FontWeights.SemiBold, FontSize = size, LineHeight = size * 0.95,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, Style = ResonantStyle("BodyStyle")
            });
        return glyph;
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
