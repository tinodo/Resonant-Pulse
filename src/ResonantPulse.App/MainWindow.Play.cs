using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private UIElement BuildPlay()
    {
        var settings = _document.Settings;
        var name = Input("Name", "DocumentName", _document.Name);
        var instrument = new PulseInstrument
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        _instrument = instrument;
        var tempo = Number("Tempo", "Tempo", settings.Tempo, 1, 1000);
        tempo.FontSize = 24;
        tempo.ValueChanged += async (_, args) =>
        {
            // The document can lag behind queued validation. Compare the UI
            // event pair so a rapid 120 -> 132 -> 120 retains both edits.
            var proposed = args.NewValue;
            if (!TempoEdits.IsChange(args.OldValue, proposed)) return;
            await GuardAsync(() => CommitAsync(doc => doc.Settings.Tempo = proposed,
                editorChange: EditorChangeKind.GlobalTempoOnly));
        };
        var unit = SelectChoices("Unit", "TempoUnit", settings.BeatUnitQuarters.ToString(CultureInfo.InvariantCulture),
        [
            new("0.5", Text.Get("Eighth")), new("1", Text.Get("Quarter")),
            new("1.5", Text.Get("DottedQuarter")), new("2", Text.Get("Half")), new("3", Text.Get("DottedHalf"))
        ]);
        var tap = ActionButton("Tap", "TapTempo", () =>
        {
            var estimate = _tap.Tap((double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);
            // NumberBox.ValueChanged is the single commit path. An unchanged
            // estimate must not add a redundant undo entry or recovery write.
            if (TempoEdits.TryApplyTap(tempo.Value, estimate, out var proposed))
                tempo.Value = proposed;
        });
        var decrease = ActionButton("TempoDown", "TempoDown", () => { tempo.Value = Math.Max(1, tempo.Value - 1); });
        var increase = ActionButton("TempoUp", "TempoUp", () => { tempo.Value = Math.Min(1000, tempo.Value + 1); });
        decrease.Content = "-1";
        increase.Content = "+1";
        decrease.VerticalAlignment = increase.VerticalAlignment = tap.VerticalAlignment = VerticalAlignment.Stretch;
        decrease.Width = increase.Width = 44;
        tap.Width = 112;
        var tempoCommands = new Grid { ColumnSpacing = 8 };
        tempoCommands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tempoCommands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tempoCommands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tempoCommands.Children.Add(decrease);
        Grid.SetColumn(increase, 2);
        tempoCommands.Children.Add(increase);
        Grid.SetColumn(tap, 1);
        tempoCommands.Children.Add(tap);
        var tempoOptions = PlayOption("TempoTab", "TempoOptions",
            Column(tempo, unit, CommandFlow(
                ActionButton("HalfTempo", "HalfTempo", () => { tempo.Value = Math.Max(1, tempo.Value / 2); }),
                ActionButton("DoubleTempo", "DoubleTempo", () => { tempo.Value = Math.Min(1000, tempo.Value * 2); }),
                ActionButton("ResetTap", "ResetTap", () => _tap.Reset()))));
        unit.SelectionChanged += async (_, _) => await GuardAsync(async () =>
        {
            await CommitAsync(doc => doc.Settings.BeatUnitQuarters = double.Parse(Selected(unit), CultureInfo.InvariantCulture));
            tempoOptions.Flyout.Hide();
            BuildPage();
        });
        var hero = new Grid();
        hero.Children.Add(instrument);
        var settingsDocked = false;
        void SizeInstrument()
        {
            var width = hero.ActualWidth - (settingsDocked ? 272 : 0);
            var size = Math.Clamp(Math.Min(width, hero.ActualHeight), 0, 560);
            instrument.Width = instrument.Height = size;
        }
        hero.SizeChanged += (_, _) => SizeInstrument();
        var beats = Number("Beats", "MeterBeats", settings.BeatsPerBar, 1, 64);
        var denominator = Select("Denominator", "MeterDenominator", settings.BeatDenominator.ToString(), "1", "2", "4", "8", "16", "32", "64");
        var groups = Input("Groups", "MeterGroups", string.Join("+", settings.Groups));
        var division = Number("Subdivision", "Subdivision", settings.Subdivision, 1, 64);
        var swing = Number("Swing", "Swing", settings.Swing, 0.5, 0.9, 0.01);
        var countIn = Number("CountIn", "CountInBars", settings.CountInBars, 0, 32);
        var sound = Select("Sound", "MainSound", settings.Sound, "Wood", "Glass", "Click", "Kick", "HiHat");
        var restoringSound = false;
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
        var audible = Number("AudibleBars", "AudibleBars", settings.AudibleBars, 0, 100000);
        var silent = Number("SilentBars", "SilentBars", settings.SilentBars, 0, 100000);
        var blind = Check("Blind", "BlindGaps", settings.BlindGaps);
        var accentButtons = new List<UIElement>();
        var accentLabels = new List<Action<Accent>>();
        void RefreshAccentLabels()
        {
            var pattern = AccentPatterns.ExpandEffectiveMain(_document.Settings,
                Math.Max(accentLabels.Count, PrimaryAccentCount(_document.Settings)));
            for (int index = 0; index < accentLabels.Count; index++) accentLabels[index](pattern[index]);
        }
        var effective = AccentPatterns.ExpandEffectiveMain(settings, PrimaryAccentCount(settings));
        for (var beat = 0; beat < PrimaryAccentCount(settings); beat++)
        {
            var index = beat;
            var current = effective[index];
            var button = new Button
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 52
            };
            void Describe(Accent accent)
            {
                var label = Text.Format("BeatAccent", index + 1, Text.Get(accent.ToString()));
                button.Content = new TextBlock
                {
                    Text = Text.Format("PulseNumber", index + 1) + "\n" + Text.Get(accent.ToString()),
                    TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap
                };
                Identify(button, $"MainAccent{index}", label);
            }
            Describe(current);
            accentLabels.Add(Describe);
            button.Click += async (_, _) => await GuardAsync(async () =>
            {
                try
                {
                    await CommitAsync(doc =>
                    {
                        var count = Math.Max(doc.Settings.Accents.Length, PrimaryAccentCount(doc.Settings));
                        var chosen = AccentPatterns.ExpandEffectiveMain(doc.Settings, count)[index];
                        var pattern = AccentPatterns.ExpandMain(doc.Settings.Accents, count);
                        pattern[index] = (Accent)(((int)chosen + 1) % 4);
                        doc.Settings.Accents = pattern;
                        doc.Settings.ManualAccentIndices = (doc.Settings.ManualAccentIndices ?? []).Append(index).Distinct().Order().ToArray();
                    }, editorChange: EditorChangeKind.SoundAndAccentsOnly);
                }
                finally { RefreshAccentLabels(); }
            });
            accentButtons.Add(button);
        }
        var accents = WrapControls(120, accentButtons.ToArray());
        accents.Width = Math.Min(560, accentButtons.Count * 140);
        accents.HorizontalAlignment = HorizontalAlignment.Center;
        var rhythmContent = Column(Copy("RhythmDraftHint"),
            Flow(beats, denominator, groups, division, swing, countIn, audible, silent), blind);
        var rhythmOptions = PlayOption("RhythmTab", "RhythmOptions", rhythmContent);
        var apply = ActionButton("ApplyRhythm", "ApplyPlaySettings", async () =>
        {
            await CommitAsync(doc =>
            {
                doc.Settings.BeatsPerBar = Integer(beats);
                doc.Settings.BeatDenominator = int.Parse(Selected(denominator), CultureInfo.InvariantCulture);
                doc.Settings.Groups = string.IsNullOrWhiteSpace(groups.Text) ? [] :
                    groups.Text.Split('+', StringSplitOptions.TrimEntries).Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();
                doc.Settings.Subdivision = Integer(division);
                doc.Settings.Swing = Value(swing);
                doc.Settings.CountInBars = Integer(countIn);
                doc.Settings.Sound = Selected(sound);
                doc.Settings.AudibleBars = Integer(audible);
                doc.Settings.SilentBars = Integer(silent);
                doc.Settings.BlindGaps = blind.IsChecked == true;
                // Preserve the authored period, including a repeating [Silent].
                // Changing meter/sound/gaps must not rewrite the accent pattern.
            });
            rhythmOptions.Flyout.Hide();
            BuildPage();
        });
        rhythmContent.Children.Add(apply);
        rhythmContent.Children.Add(Copy("GroupAccentRule"));
        rhythmContent.Children.Add(ActionButton("AllNormal", "AllNormalAccents", async () =>
                {
                    try
                    {
                        await CommitAsync(doc =>
                        {
                            doc.Settings.Accents = Enumerable.Repeat(Accent.Normal, PrimaryAccentCount(doc.Settings)).ToArray();
                            doc.Settings.AccentOverridesGroups = true;
                        }, editorChange: EditorChangeKind.SoundAndAccentsOnly);
                    }
                    finally { RefreshAccentLabels(); }
                }));
        var meter = ActionButton("TimeSignature", "QuickMeter", () =>
        {
            rhythmOptions.Flyout.ShowAt(rhythmOptions);
        });
        meter.Content = $"{settings.BeatsPerBar} / {settings.BeatDenominator}";
        meter.FontSize = 20;
        meter.HorizontalAlignment = HorizontalAlignment.Left;
        meter.MinWidth = 108;
        Identify(meter, "QuickMeter", $"{Text.Get("TimeSignature")}: {settings.BeatsPerBar}/{settings.BeatDenominator}");
        var project = PlayOption("ProjectTab", "ProjectOptions", Column(name,
                ActionButton("RenameProject", "ApplyDocumentName", () => CommitAsync(doc => doc.Name = name.Text, false)),
                CommandFlow(ActionButton("Save", "PlaySave", SaveProjectAsync), ActionButton("Open", "PlayOpen", OpenProjectAsync))));
        var practiceSeconds = Number("PracticeSeconds", "PracticeSeconds", settings.PracticeSeconds, 0, 86400);
        var practiceBeats = Number("PracticeBeats", "PracticeBeats", settings.PracticeBeats, 0, 6_400_000);
        var practiceBars = Number("PracticeBars", "PracticeBars", settings.PracticeBars, 0, 100_000);
        var practiceFeedback = Body(Text.Format("PracticeLimitsSaved",
            settings.PracticeSeconds, settings.PracticeBeats, settings.PracticeBars));
        Identify(practiceFeedback, "PracticeLimitsStatus", practiceFeedback.Text);
        long practiceCommitVersion = 0;
        AutomationProperties.SetItemStatus(practiceFeedback, "0");
        var practice = PlayOption("PracticeTab", "PracticeTimer",
            Column(practiceSeconds, practiceBeats, practiceBars, Copy("PracticeTimerHint"), Copy("CountedStopHint"),
                practiceFeedback,
                ActionButton("Apply", "ApplyPracticeTimer", async () =>
                {
                    var seconds = Integer(practiceSeconds);
                    var beatsToPlay = Integer(practiceBeats);
                    var barsToPlay = Integer(practiceBars);
                    var edited = false;
                    await CommitAsync(doc =>
                    {
                        doc.Settings.PracticeSeconds = seconds;
                        doc.Settings.PracticeBeats = beatsToPlay;
                        doc.Settings.PracticeBars = barsToPlay;
                        edited = true;
                    },
                        editorChange: EditorChangeKind.PracticeTimerOnly);
                    if (!edited) return; // A locked document reports its notice without publishing.
                    practiceFeedback.Text = Text.Format("PracticeLimitsSaved", seconds, beatsToPlay, barsToPlay);
                    AutomationProperties.SetName(practiceFeedback, practiceFeedback.Text);
                    AutomationProperties.SetItemStatus(practiceFeedback,
                        FormattableString.Invariant($"{++practiceCommitVersion}:{seconds}:{beatsToPlay}:{barsToPlay}"));
                })));
        var help = PlayOption("HelpTab", "PlayHelp",
            Column(Copy("FirstUseHint"), Copy("LiveSoundHint"), Copy("EditBoundary"), Copy("PresentationLimits"), Copy("KeyboardHelp"), Copy("Horizon")));
        sound.Header = null;
        sound.Width = 160;
        AutomationProperties.SetHelpText(sound, Text.Get("LiveSoundHint"));
        ToolTipService.SetToolTip(sound, Text.Get("LiveSoundHint"));
        var meterSound = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        meterSound.Children.Add(meter);
        var soundField = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var soundLabel = Copy("Sound");
        soundLabel.VerticalAlignment = VerticalAlignment.Center;
        soundField.Children.Add(soundLabel);
        soundField.Children.Add(sound);
        meterSound.Children.Add(soundField);
        var quick = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center
        };
        quick.Children.Add(tempoCommands);
        quick.Children.Add(meterSound);
        var quickScroll = Scroll(quick);
        Identify(quickScroll, "PlayQuickControls", Text.Get("Settings"));
        var scroll = Scroll(accents);
        var textScale = new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
        scroll.MaxHeight = 60 * textScale;
        Identify(scroll, "PulseAccentStrip", Text.Get("PulseStrengths"));
        var cue = Copy("MorePulses");
        cue.Width = 100;
        cue.VerticalAlignment = VerticalAlignment.Center;
        Identify(cue, "PlayScrollHint", Text.Get("ScrollControlsHint"));
        cue.Visibility = Visibility.Collapsed;
        var shortcuts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var command in new[] { rhythmOptions, tempoOptions, practice, project, help })
        {
            command.MinWidth = 100;
            shortcuts.Children.Add(command);
        }
        var settingsScroll = new ScrollViewer
        {
            Content = shortcuts, HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Identify(settingsScroll, "PlaySettingsStrip", Text.Get("Settings"));
        var pulseControls = new Grid();
        pulseControls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pulseControls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pulseControls.Children.Add(scroll);
        Grid.SetColumn(cue, 1);
        pulseControls.Children.Add(cue);
        scroll.LayoutUpdated += (_, _) =>
        {
            // The hint must not create the overflow that keeps itself visible.
            var unhintedWidth = Math.Min(560, Math.Max(0, pulseControls.ActualWidth - 16));
            var overflow = accentButtons.Count > FlowColumns(unhintedWidth, 120, textScale, accentButtons.Count);
            cue.Visibility = overflow ? Visibility.Visible : Visibility.Collapsed;
            pulseControls.ColumnSpacing = overflow ? 12 : 0;
        };
        scroll.SizeChanged += (_, args) =>
            accents.Width = Math.Min(Math.Min(560, accentButtons.Count * 140), Math.Max(0, args.NewSize.Width - 16));
        var page = new Grid { RowSpacing = 8 };
        page.SizeChanged += (_, args) =>
        {
            settingsDocked = args.NewSize.Width >= 620;
            shortcuts.Orientation = settingsDocked ? Orientation.Vertical : Orientation.Horizontal;
            shortcuts.Spacing = settingsDocked ? 6 : 8;
            settingsScroll.Width = settingsDocked ? 120 : double.NaN;
            settingsScroll.HorizontalAlignment = settingsDocked ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
            settingsScroll.VerticalAlignment = settingsDocked ? VerticalAlignment.Center : VerticalAlignment.Stretch;
            settingsScroll.HorizontalScrollMode = settingsDocked ? ScrollMode.Disabled : ScrollMode.Enabled;
            settingsScroll.HorizontalScrollBarVisibility = settingsDocked ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            settingsScroll.VerticalScrollMode = settingsDocked ? ScrollMode.Enabled : ScrollMode.Disabled;
            settingsScroll.VerticalScrollBarVisibility = settingsDocked ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
            Grid.SetRow(settingsScroll, settingsDocked ? 1 : 0);
            quick.Orientation = args.NewSize.Width < 620 ? Orientation.Vertical : Orientation.Horizontal;
            meterSound.Orientation = args.NewSize.Width < 360 ? Orientation.Vertical : Orientation.Horizontal;
            quickScroll.MaxHeight = args.NewSize.Width < 620 ? 116 : double.PositiveInfinity;
            tempoCommands.HorizontalAlignment = HorizontalAlignment.Center;
            meterSound.HorizontalAlignment = HorizontalAlignment.Center;
            soundField.HorizontalAlignment = HorizontalAlignment.Center;
            SizeInstrument();
        };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(hero, 1);
        page.Children.Add(hero);
        page.Children.Add(settingsScroll);
        Grid.SetRow(quickScroll, 2);
        page.Children.Add(quickScroll);
        Grid.SetRow(pulseControls, 3);
        page.Children.Add(pulseControls);
        return page;
    }

    private Button PlayOption(string key, string id, UIElement content)
    {
        var close = ActionButton("CloseOptions", id + "Close", () => { });
        var scroll = Scroll(content);
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        var panel = new Grid { RowSpacing = 12 };
        panel.KeyDown += OnKeyDown;
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(close);
        Grid.SetRow(scroll, 1);
        panel.Children.Add(scroll);
        var flyout = new Flyout { Content = panel };
        close.Click += (_, _) => flyout.Hide();
        flyout.Opening += (_, _) => SizeOptionsFlyout(flyout, panel, scroll);
        var button = new Button { Flyout = flyout, HorizontalContentAlignment = HorizontalAlignment.Center };
        LabelButton(button, key);
        Identify(button, id, Text.Get(key));
        return button;
    }

    private void SizeOptionsFlyout(Flyout flyout, Grid panel, ScrollViewer scroll)
    {
        scroll.MaxHeight = Math.Max(100, Root.ActualHeight - 200);
        panel.Width = Math.Clamp(Root.ActualWidth - 100, 240, 640);
        flyout.FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
        {
            Setters =
            {
                new Setter(FrameworkElement.MaxWidthProperty, Math.Max(280, Root.ActualWidth - 32)),
                new Setter(FrameworkElement.MaxHeightProperty, Math.Max(180, Root.ActualHeight - 32)),
                new Setter(Control.PaddingProperty, new Thickness(12))
            }
        };
    }

    private static int PrimaryAccentCount(PulseSettings settings) =>
        Math.Clamp((int)Math.Ceiling(4d * settings.BeatsPerBar / settings.BeatDenominator / settings.BeatUnitQuarters), 1, 256);
}
