using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Xaml;
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
        _position = Body("");
        var tempo = Number("Tempo", "Tempo", settings.Tempo, 1, 1000, 0.1);
        tempo.ValueChanged += async (_, args) =>
        {
            // The document can lag behind queued validation. Compare the UI
            // event pair so a rapid 120 -> 132 -> 120 retains both edits.
            var proposed = args.NewValue;
            if (!TempoEdits.IsChange(args.OldValue, proposed)) return;
            await GuardAsync(() => CommitAsync(doc => doc.Settings.Tempo = proposed));
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
        var controls = Column(Heading(Text.Get("Tempo")), Flow(tempo, unit),
            CommandFlow(
                ActionButton("TempoDown", "TempoDown", () => { tempo.Value = Math.Max(1, tempo.Value - 0.1); }),
                ActionButton("TempoUp", "TempoUp", () => { tempo.Value = Math.Min(1000, tempo.Value + 0.1); }),
                tap, ActionButton("ResetTap", "ResetTap", () => _tap.Reset()),
                ActionButton("HalfTempo", "HalfTempo", () => { tempo.Value = Math.Max(1, tempo.Value / 2); }),
                ActionButton("DoubleTempo", "DoubleTempo", () => { tempo.Value = Math.Min(1000, tempo.Value * 2); })));
        var instrumentArea = Column(_instrument, _position, Copy("Horizon"));
        var hero = new Grid { ColumnSpacing = 24, RowSpacing = 20 };
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(410) });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hero.Children.Add(instrumentArea);
        Grid.SetColumn(controls, 1);
        hero.Children.Add(controls);
        hero.SizeChanged += (_, args) =>
        {
            var narrow = args.NewSize.Width < 850;
            // Keep the entire phase dial useful on short hosted/laptop screens;
            // additional native editors remain in the existing vertical scroller.
            instrument.MaxHeight = Math.Clamp(Root.ActualHeight * 0.42, 220, 500);
            hero.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(410);
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
        var audible = Number("AudibleBars", "AudibleBars", settings.AudibleBars, 0, 100000);
        var silent = Number("SilentBars", "SilentBars", settings.SilentBars, 0, 100000);
        var blind = Check("Blind", "BlindGaps", settings.BlindGaps);
        var accents = AccentFlow();
        for (var beat = 0; beat < PrimaryAccentCount(settings); beat++)
        {
            var index = beat;
            var current = AccentPatterns.MainAt(settings.Accents, index);
            var button = new Button { Margin = new Thickness(0, 0, 8, 8) };
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
        var apply = ActionButton("Apply", "ApplyPlaySettings", async () =>
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
        return Scroll(Column(name,
            ActionButton("Apply", "ApplyDocumentName", () => CommitAsync(doc => doc.Name = name.Text, false)),
            hero, Heading(Text.Get("Accent")), Copy("GroupAccentRule"), accents,
            ActionButton("AllNormal", "AllNormalAccents", async () =>
            {
                await CommitAsync(doc => doc.Settings.Accents = Enumerable.Repeat(Accent.Normal, PrimaryAccentCount(doc.Settings)).ToArray());
                BuildPage();
            }),
            Flow(beats, denominator, groups, division, swing, countIn, sound, audible, silent), blind, apply,
            Copy("EditBoundary"), Copy("PresentationLimits"), Copy("KeyboardHelp"),
            CommandFlow(ActionButton("Save", "PlaySave", SaveProjectAsync), ActionButton("Open", "PlayOpen", OpenProjectAsync))));
    }

    private static int PrimaryAccentCount(PulseSettings settings) =>
        Math.Clamp((int)Math.Ceiling(4d * settings.BeatsPerBar / settings.BeatDenominator / settings.BeatUnitQuarters), 1, 256);
}
