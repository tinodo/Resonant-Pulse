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
        var instrument = new PulseInstrument();
        _instrument = instrument;
        var tempo = Number("Tempo", "Tempo", settings.Tempo, 1, 1000);
        var tempoGuide = Body(TempoGuide.Format(tempo.Value));
        Identify(tempoGuide, "TempoGuide", tempoGuide.Text);
        _playTempo = tempo;
        tempo.FontSize = 32;
        tempo.ValueChanged += async (_, args) =>
        {
            // The document can lag behind queued validation. Compare the UI
            // event pair so a rapid 120 -> 132 -> 120 retains both edits.
            var proposed = args.NewValue;
            tempoGuide.Text = TempoGuide.Format(proposed);
            AutomationProperties.SetName(tempoGuide, tempoGuide.Text);
            if (_isSynchronizingTempo || !TempoEdits.IsChange(args.OldValue, proposed)) return;
            _tempoInputVersion++;
            CancelTapIn();
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
        var tap = ActionButton("Tap", "TapTempo", TapTempoAsync);
        _tapStatus = Body(Text.Get("TapReady"));
        Identify(_tapStatus, "TapStatus", _tapStatus.Text);
        var tapCount = Number("TapInCount", "TapInCount", _tapInRequiredTaps, 2, 16);
        _tapInCount = tapCount;
        tapCount.ValueChanged += (_, _) => CancelTapIn();
        _tapInArm = ActionButton("ArmTapIn", "ArmTapIn", () => ArmTapIn(Integer(tapCount)));
        _tapInCancel = ActionButton("CancelTapIn", "CancelTapIn", CancelTapIn);
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
        var tempoOptions = new Expander
        {
            Header = Text.Get("TempoOptions"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Column(unit, tempoGuide, Copy("TempoGuideHint"), CommandFlow(
                ActionButton("HalfTempo", "HalfTempo", () => { tempo.Value = Math.Max(1, tempo.Value / 2); }),
                ActionButton("DoubleTempo", "DoubleTempo", () => { tempo.Value = Math.Min(1000, tempo.Value * 2); }),
                ActionButton("ResetTap", "ResetTap", () =>
                {
                    CancelTapIn();
                    _tap.Reset();
                    RefreshSnapshot();
                })), tapCount, CommandFlow(_tapInArm, _tapInCancel), Copy("TapInHint"))
        };
        Identify(tempoOptions, "TempoOptions", Text.Get("TempoOptions"));
        var controls = Column(tempo, tempoCommands, _tapStatus, Copy("FirstUseHint"));
        var instrumentArea = instrument;
        var hero = new Grid { ColumnSpacing = 24, RowSpacing = 20 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.Children.Add(instrumentArea);
        Grid.SetColumn(controls, 1);
        hero.Children.Add(controls);
        hero.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 640;
            instrument.MaxHeight = Math.Clamp(Root.ActualHeight * 0.30, 180, 280);
            hero.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(300);
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
        sound.SelectionChanged += async (_, _) => await GuardAsync(() =>
            CommitAsync(doc => doc.Settings.Sound = Selected(sound)));
        var audible = Number("AudibleBars", "AudibleBars", settings.AudibleBars, 0, 100000);
        var silent = Number("SilentBars", "SilentBars", settings.SilentBars, 0, 100000);
        var blind = Check("Blind", "BlindGaps", settings.BlindGaps);
        var accents = AccentFlow();
        for (var beat = 0; beat < PrimaryAccentCount(settings); beat++)
        {
            var index = beat;
            var current = AccentPatterns.MainAt(settings.Accents, index);
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
                    var pattern = AccentPatterns.ExpandMain(doc.Settings.Accents,
                        Math.Max(doc.Settings.Accents.Length, PrimaryAccentCount(doc.Settings)));
                    pattern[index] = (Accent)(((int)pattern[index] + 1) % 4);
                    doc.Settings.Accents = pattern;
                });
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
        var rhythmOptions = new Expander
        {
            Header = Text.Get("RhythmOptions"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Column(Copy("RhythmDraftHint"),
                Flow(beats, denominator, groups, division, swing, countIn, audible, silent),
                blind, apply, Copy("GroupAccentRule"),
                ActionButton("AllNormal", "AllNormalAccents", async () =>
                {
                    await CommitAsync(doc => doc.Settings.Accents = Enumerable.Repeat(Accent.Normal, PrimaryAccentCount(doc.Settings)).ToArray());
                    BuildPage();
                }))
        };
        Identify(rhythmOptions, "RhythmOptions", Text.Get("RhythmOptions"));
        var meter = ActionButton("TimeSignature", "QuickMeter", () =>
        {
            rhythmOptions.IsExpanded = true;
            rhythmOptions.StartBringIntoView();
            beats.Focus(FocusState.Programmatic);
        });
        meter.Content = $"{settings.BeatsPerBar} / {settings.BeatDenominator}";
        meter.FontSize = 20;
        meter.HorizontalAlignment = HorizontalAlignment.Stretch;
        Identify(meter, "QuickMeter", $"{Text.Get("TimeSignature")}: {settings.BeatsPerBar}/{settings.BeatDenominator}");
        var project = new Expander
        {
            Header = Text.Get("ProjectOptions"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Column(name,
                ActionButton("RenameProject", "ApplyDocumentName", () => CommitAsync(doc => doc.Name = name.Text, false)),
                CommandFlow(ActionButton("Save", "PlaySave", SaveProjectAsync), ActionButton("Open", "PlayOpen", OpenProjectAsync)))
        };
        var practiceSeconds = Number("PracticeSeconds", "PracticeSeconds", settings.PracticeSeconds, 0, 86400);
        var practiceBeats = Number("PracticeBeats", "PracticeBeats", settings.PracticeBeats, 0, 6_400_000);
        var practiceBars = Number("PracticeBars", "PracticeBars", settings.PracticeBars, 0, 100_000);
        var practiceFeedback = Body(Text.Format("PracticeLimitsSaved",
            settings.PracticeSeconds, settings.PracticeBeats, settings.PracticeBars));
        Identify(practiceFeedback, "PracticeLimitsStatus", practiceFeedback.Text);
        long practiceCommitVersion = 0;
        AutomationProperties.SetItemStatus(practiceFeedback, "0");
        var practice = new Expander
        {
            Header = Text.Get("PracticeTimer"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Column(practiceSeconds, practiceBeats, practiceBars, Copy("PracticeTimerHint"), Copy("CountedStopHint"),
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
                }))
        };
        Identify(practice, "PracticeTimer", Text.Get("PracticeTimer"));
        var help = new Expander
        {
            Header = Text.Get("Help"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Content = Column(ActionButton("QuickStart", "OpenQuickStart", () => SelectView("Help")),
                Copy("EditBoundary"), Copy("PresentationLimits"), Copy("KeyboardHelp"), Copy("Horizon"))
        };
        var heroCard = new Border { Style = (Style)Application.Current.Resources["InstrumentCardStyle"], Child = hero };
        return Scroll(Column(Heading(Text.Get("Metronome")), heroCard,
            Flow(Column(Copy("TimeSignature"), meter), sound), accents,
            rhythmOptions, tempoOptions, practice, project, help));
    }

    private static int PrimaryAccentCount(PulseSettings settings) =>
        Math.Clamp((int)Math.Ceiling(4d * settings.BeatsPerBar / settings.BeatDenominator / settings.BeatUnitQuarters), 1, 256);
}
