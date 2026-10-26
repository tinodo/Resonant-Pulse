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
        var instrument = new PulseInstrument { Width = 128, Height = 128, VerticalAlignment = VerticalAlignment.Center };
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
        unit.SelectionChanged += async (_, _) => await GuardAsync(async () =>
        {
            await CommitAsync(doc => doc.Settings.BeatUnitQuarters = double.Parse(Selected(unit), CultureInfo.InvariantCulture));
            BuildPage();
        });
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
        var tempoCommands = new Grid { ColumnSpacing = 8 };
        tempoCommands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tempoCommands.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        tempoCommands.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        tempoCommands.Children.Add(decrease);
        Grid.SetColumn(increase, 1);
        tempoCommands.Children.Add(increase);
        Grid.SetColumn(tap, 2);
        tap.HorizontalAlignment = HorizontalAlignment.Stretch;
        tempoCommands.Children.Add(tap);
        var tempoOptions = PlayOption("TempoTab", "TempoOptions",
            Column(unit, CommandFlow(
                ActionButton("HalfTempo", "HalfTempo", () => { tempo.Value = Math.Max(1, tempo.Value / 2); }),
                ActionButton("DoubleTempo", "DoubleTempo", () => { tempo.Value = Math.Min(1000, tempo.Value * 2); }),
                ActionButton("ResetTap", "ResetTap", () => _tap.Reset()))));
        var controls = new StackPanel { Spacing = 8, MaxWidth = 300, HorizontalAlignment = HorizontalAlignment.Left };
        controls.Children.Add(tempo);
        controls.Children.Add(tempoCommands);
        var instrumentArea = instrument;
        var hero = new Grid { ColumnSpacing = 24, RowSpacing = 8, MaxWidth = 700 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.Children.Add(instrumentArea);
        Grid.SetColumn(controls, 1);
        hero.Children.Add(controls);
        hero.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 460;
            hero.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(controls, narrow ? 0 : 1);
            Grid.SetRow(controls, narrow ? 1 : 0);
        };
        var beats = Number("Beats", "MeterBeats", settings.BeatsPerBar, 1, 64);
        var denominator = Select("Denominator", "MeterDenominator", settings.BeatDenominator.ToString(), "1", "2", "4", "8", "16", "32", "64");
        var groups = Input("Groups", "MeterGroups", string.Join("+", settings.Groups));
        var division = Number("Subdivision", "Subdivision", settings.Subdivision, 1, 64);
        var swing = Number("Swing", "Swing", settings.Swing, 0.5, 0.9, 0.01);
        var countIn = Number("CountIn", "CountInBars", settings.CountInBars, 0, 32);
        var sound = Select("Sound", "MainSound", settings.Sound, "Wood", "Glass", "Click", "Kick", "HiHat");
        sound.SelectionChanged += async (_, _) =>
        {
            var selectedSound = Selected(sound);
            await GuardAsync(() => CommitAsync(doc => doc.Settings.Sound = selectedSound,
                editorChange: EditorChangeKind.SoundAndAccentsOnly));
        };
        var audible = Number("AudibleBars", "AudibleBars", settings.AudibleBars, 0, 100000);
        var silent = Number("SilentBars", "SilentBars", settings.SilentBars, 0, 100000);
        var blind = Check("Blind", "BlindGaps", settings.BlindGaps);
        var accents = AccentFlow(140, 60);
        var effective = AccentPatterns.ExpandEffectiveMain(settings, PrimaryAccentCount(settings));
        for (var beat = 0; beat < PrimaryAccentCount(settings); beat++)
        {
            var index = beat;
            var current = effective[index];
            var button = new Button { Margin = new Thickness(0, 0, 8, 8), VerticalAlignment = VerticalAlignment.Top };
            void Describe(Accent accent)
            {
                var label = Text.Format("BeatAccent", index + 1, Text.Get(accent.ToString()));
                button.Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap };
                Identify(button, $"MainAccent{index}", label);
            }
            Describe(current);
            button.Click += async (_, _) => await GuardAsync(async () =>
            {
                await CommitAsync(doc =>
                {
                    var pattern = AccentPatterns.ExpandEffectiveMain(doc.Settings,
                        Math.Max(doc.Settings.Accents.Length, PrimaryAccentCount(doc.Settings)));
                    pattern[index] = (Accent)(((int)pattern[index] + 1) % 4);
                    doc.Settings.Accents = pattern;
                    doc.Settings.AccentOverridesGroups = true;
                }, editorChange: EditorChangeKind.SoundAndAccentsOnly);
                Describe(_document.Settings.Accents[index]);
            });
            accents.Children.Add(button);
        }
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
            BuildPage();
        });
        var rhythmOptions = PlayOption("RhythmTab", "RhythmOptions",
            Column(Copy("RhythmDraftHint"),
                Flow(beats, denominator, groups, division, swing, countIn, audible, silent),
                blind, apply, Copy("GroupAccentRule"),
                ActionButton("AllNormal", "AllNormalAccents", async () =>
                {
                    await CommitAsync(doc =>
                    {
                        doc.Settings.Accents = Enumerable.Repeat(Accent.Normal, PrimaryAccentCount(doc.Settings)).ToArray();
                        doc.Settings.AccentOverridesGroups = true;
                    }, editorChange: EditorChangeKind.SoundAndAccentsOnly);
                    BuildPage();
                })));
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
        sound.Width = 220;
        AutomationProperties.SetHelpText(sound, Text.Get("LiveSoundHint"));
        ToolTipService.SetToolTip(sound, Text.Get("LiveSoundHint"));
        var meterSound = new Grid { ColumnSpacing = 16 };
        meterSound.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        meterSound.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        StackPanel Field(string key, UIElement control)
        {
            var field = new StackPanel { Spacing = 8 };
            field.Children.Add(Copy(key));
            field.Children.Add(control);
            return field;
        }
        meterSound.Children.Add(Field("TimeSignature", meter));
        var soundField = Field("Sound", sound);
        Grid.SetColumn(soundField, 1);
        meterSound.Children.Add(soundField);
        meterSound.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 360;
            if (meterSound.RowDefinitions.Count == 0)
            {
                meterSound.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                meterSound.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                meterSound.RowSpacing = 8;
            }
            Grid.SetColumn(soundField, narrow ? 0 : 1);
            Grid.SetRow(soundField, narrow ? 1 : 0);
        };
        var heroCard = new Border
        {
            Style = (Style)Application.Current.Resources["InstrumentCardStyle"], Child = hero,
            Width = 560, HorizontalAlignment = HorizontalAlignment.Left
        };
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(heroCard);
        content.Children.Add(meterSound);
        content.Children.Add(accents);
        var scroll = Scroll(content);
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        var cue = Copy("ScrollControlsHint");
        Identify(cue, "PlayScrollHint", cue.Text);
        cue.Visibility = Visibility.Collapsed;
        scroll.LayoutUpdated += (_, _) =>
        {
            var cueSpace = cue.Visibility == Visibility.Visible ? cue.ActualHeight + 8 : 0;
            cue.Visibility = scroll.ScrollableHeight > cueSpace + 1 ? Visibility.Visible : Visibility.Collapsed;
        };
        var heading = Heading(Text.Get("Metronome"));
        heading.FontSize = 24;
        var shortcuts = WrapControls(115, rhythmOptions, tempoOptions, practice, project, help);
        shortcuts.Width = 760;
        shortcuts.HorizontalAlignment = HorizontalAlignment.Left;
        var header = new StackPanel { Spacing = 8 };
        header.Children.Add(heading);
        header.Children.Add(shortcuts);
        header.Children.Add(cue);
        var page = new Grid { RowSpacing = 12 };
        page.SizeChanged += (_, args) =>
        {
            shortcuts.Width = Math.Min(760, Math.Max(0, args.NewSize.Width));
            heroCard.Width = Math.Min(560, Math.Max(0, args.NewSize.Width - 16));
        };
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        page.Children.Add(header);
        Grid.SetRow(scroll, 1);
        page.Children.Add(scroll);
        return page;
    }

    private Button PlayOption(string key, string id, UIElement content)
    {
        var close = ActionButton("CloseOptions", id + "Close", () => { });
        var scroll = Scroll(content);
        scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
        var panel = new Grid { RowSpacing = 12 };
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(close);
        Grid.SetRow(scroll, 1);
        panel.Children.Add(scroll);
        var flyout = new Flyout { Content = panel };
        close.Click += (_, _) => flyout.Hide();
        flyout.Opening += (_, _) =>
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
        };
        var button = new Button { Flyout = flyout, HorizontalContentAlignment = HorizontalAlignment.Center };
        LabelButton(button, key);
        Identify(button, id, Text.Get(key));
        return button;
    }

    private static int PrimaryAccentCount(PulseSettings settings) =>
        Math.Clamp((int)Math.Ceiling(4d * settings.BeatsPerBar / settings.BeatDenominator / settings.BeatUnitQuarters), 1, 256);
}
