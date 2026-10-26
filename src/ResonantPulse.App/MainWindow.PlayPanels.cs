using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private sealed record OptionPanel(Flyout Flyout);

    /// <summary>Width of each footer button; Cancel and Done always share it.</summary>
    internal const double FooterButtonWidth = 120;

    /// <summary>
    /// One consistent panel: title, optional help, scrolling fields, then [Cancel] [Done] side by side.
    /// Done applies and closes; Cancel (or clicking away) discards. Fields are refilled from the document
    /// every time the panel opens, so a cancelled draft never lingers.
    /// </summary>
    private OptionPanel CreateOptionPanel(string titleKey, string id, UIElement content, string? helpKey,
        Action? populate, Func<Task<bool>>? apply)
    {
        var title = new TextBlock
        {
            Text = Text.Get(titleKey), FontSize = 20, FontWeight = FontWeights.SemiBold,
            Style = ResonantStyle("BodyStyle")
        };
        var scroll = Scroll(content);
        scroll.Padding = new Thickness(0, 0, 12, 0);
        var error = new TextBlock { Style = ResonantStyle("ErrorTextStyle"), Visibility = Visibility.Collapsed };
        Identify(error, id + "Error", Text.Get("Error"));
        // [Cancel] [Done] are one pair: equal fixed-width cells, and each button stretches to the cell, so they are the
        // same size however either one's style or label changes.
        var done = new Button
        {
            Style = ResonantStyle("PrimaryButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
        };
        LabelButton(done, "CloseOptions");
        Identify(done, id + "Done", Text.Get("CloseOptions"));
        var footer = new Grid { ColumnSpacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        Button? cancel = null;
        if (apply is not null)
        {
            cancel = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            LabelButton(cancel, "Cancel");
            Identify(cancel, id + "Cancel", Text.Get("Cancel"));
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FooterButtonWidth) });
            footer.Children.Add(cancel);
        }
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(FooterButtonWidth) });
        Grid.SetColumn(done, footer.ColumnDefinitions.Count - 1);
        footer.Children.Add(done);

        var panel = new Grid { RowSpacing = 12 };
        for (var row = 0; row < 5; row++) panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.Children.Add(title);
        if (helpKey is not null)
        {
            var help = new TextBlock { Text = Text.Get(helpKey), Style = ResonantStyle("CaptionStyle") };
            Grid.SetRow(help, 1);
            panel.Children.Add(help);
        }
        Grid.SetRow(scroll, 2);
        panel.Children.Add(scroll);
        Grid.SetRow(error, 3);
        panel.Children.Add(error);
        Grid.SetRow(footer, 4);
        panel.Children.Add(footer);
        panel.KeyDown += OnKeyDown;

        var flyout = new Flyout { Content = panel };
        var isOpen = false;
        flyout.Opening += (_, _) =>
        {
            SizeOptionsFlyout(flyout, panel, scroll);
            error.Visibility = Visibility.Collapsed;
            populate?.Invoke();
        };
        flyout.Opened += (_, _) => isOpen = true;
        flyout.Closed += (_, _) => isOpen = false;
        if (cancel is not null) cancel.Click += (_, _) => flyout.Hide();
        done.Click += async (_, _) =>
        {
            error.Visibility = Visibility.Collapsed;
            try
            {
                if (apply is null || await apply()) flyout.Hide();
            }
            catch (Exception exception)
            {
                Diagnostics.Write("Option panel", exception);
                if (isOpen)
                {
                    error.Text = ErrorText.Explain(exception);
                    error.Visibility = Visibility.Visible;
                }
                // Dismissed (or the page rebuilt) while the change was being applied: an inline message would be
                // on a hidden panel, so the failure goes to the window-level notice instead.
                else if (!_isClosed) ShowError(exception);
            }
        };
        return new OptionPanel(flyout);
    }

    private void SizeOptionsFlyout(Flyout flyout, Grid panel, ScrollViewer scroll)
    {
        scroll.MaxHeight = Math.Max(100, Root.ActualHeight - 220);
        panel.Width = Math.Clamp(Root.ActualWidth - 100, 240, 560);
        panel.RequestedTheme = Root.RequestedTheme;
        flyout.FlyoutPresenterStyle = new Style(typeof(FlyoutPresenter))
        {
            Setters =
            {
                new Setter(FrameworkElement.MaxWidthProperty, Math.Max(280, Root.ActualWidth - 32)),
                new Setter(FrameworkElement.MaxHeightProperty, Math.Max(180, Root.ActualHeight - 32)),
                new Setter(Microsoft.UI.Xaml.Controls.Control.PaddingProperty, new Thickness(16))
            }
        };
    }

    private static TextBlock SectionHeading(string key) => new()
    {
        Text = Text.Get(key), FontSize = 14, FontWeight = FontWeights.SemiBold,
        Style = ResonantStyle("BodyStyle"), Margin = new Thickness(0, 4, 0, -4)
    };

    private static TextBlock Hint(string key) => new() { Text = Text.Get(key), Style = ResonantStyle("CaptionStyle") };

    private static Choice[] NoteChoices() =>
    [
        new("1", $"{Text.Get("Whole")} (1)"), new("2", $"{Text.Get("Half")} (2)"), new("4", $"{Text.Get("Quarter")} (4)"),
        new("8", $"{Text.Get("Eighth")} (8)"), new("16", $"{Text.Get("Sixteenth")} (16)"),
        new("32", $"{Text.Get("ThirtySecond")} (32)"), new("64", $"{Text.Get("SixtyFourth")} (64)")
    ];

    private static Choice[] TempoUnitChoices() =>
    [
        new("0.25", Text.Get("Sixteenth")), new("0.5", Text.Get("Eighth")), new("1", Text.Get("Quarter")),
        new("1.5", Text.Get("DottedQuarter")), new("2", Text.Get("Half")), new("3", Text.Get("DottedHalf")),
        new("4", Text.Get("Whole"))
    ];

    private static Choice[] TempoUnitChoices(PulseSettings settings)
    {
        // Only units that fit the bar a whole number of times keep the strip, the accents and the clicks in step.
        var current = Invariant(settings.BeatUnitQuarters);
        return TempoUnitChoices()
            .Where(choice => choice.Value == current || MeterPresets.UnitFitsBar(settings.BeatsPerBar, settings.BeatDenominator,
                double.Parse(choice.Value, CultureInfo.InvariantCulture)))
            .ToArray();
    }

    private static void SelectChoice(ComboBox combo, string value)
    {
        if (combo.Items.OfType<Choice>().FirstOrDefault(choice => choice.Value == value) is { } match)
            combo.SelectedItem = match;
    }

    private static string Invariant(double value) => value.ToString(CultureInfo.InvariantCulture);

    private OptionPanel BuildTempoPanel()
    {
        var settings = _document.Settings;
        var tempo = Number("Tempo", "Tempo", settings.Tempo, 1, 1000, 1, 0);
        tempo.FontSize = 24;
        var unit = SelectChoices("Unit", "TempoUnit", Invariant(settings.BeatUnitQuarters), TempoUnitChoices(settings));
        var half = ActionButton("HalfTempo", "HalfTempo", () => { tempo.Value = Math.Max(1, Math.Round(tempo.Value / 2, 1)); });
        var twice = ActionButton("DoubleTempo", "DoubleTempo", () => { tempo.Value = Math.Min(1000, Math.Round(tempo.Value * 2, 1)); });
        return CreateOptionPanel("TempoTab", "TempoOptions", Column(tempo, unit, CommandFlow(half, twice)), "TempoEditorHelp",
            () =>
            {
                tempo.Value = _tempoRequested ?? _document.Settings.Tempo;
                SelectChoice(unit, Invariant(_document.Settings.BeatUnitQuarters));
            },
            async () =>
            {
                var bpm = Math.Round(Value(tempo), 1);
                var unitValue = double.Parse(Selected(unit), CultureInfo.InvariantCulture);
                var unitChanged = unitValue != _document.Settings.BeatUnitQuarters;
                var applied = false;
                await CommitAsync(doc =>
                {
                    doc.Settings.Tempo = bpm;
                    doc.Settings.BeatUnitQuarters = unitValue;
                    applied = true;
                }, editorChange: unitChanged ? EditorChangeKind.PlayStructure : EditorChangeKind.GlobalTempoOnly);
                if (applied && unitChanged) BuildPage();
                return applied;
            });
    }

    private OptionPanel BuildRhythmPanel()
    {
        var settings = _document.Settings;
        var beats = NumberInt("BeatsTop", "MeterBeats", settings.BeatsPerBar, 1, 64);
        var denominator = SelectChoices("BeatNote", "MeterDenominator", settings.BeatDenominator.ToString(CultureInfo.InvariantCulture), NoteChoices());
        var groups = Input("GroupingLabel", "MeterGroups", MeterPresets.GroupsText(settings.Groups));
        var division = NumberInt("SubdivisionLabel", "Subdivision", settings.Subdivision, 1, 64);
        var swing = Number("Swing", "Swing", settings.Swing, 0.5, 0.9, 0.01, 2);
        var countIn = NumberInt("CountIn", "CountInBars", settings.CountInBars, 0, 32);
        var audible = NumberInt("AudibleBars", "AudibleBars", settings.AudibleBars, 0, 100000);
        var silent = NumberInt("SilentBars", "SilentBars", settings.SilentBars, 0, 100000);
        var blind = Check("Blind", "BlindGaps", settings.BlindGaps);
        var content = Column(
            SectionHeading("TimeSignature"), Flow(beats, denominator), groups,
            SectionHeading("SectionFeel"), Flow(division, swing),
            SectionHeading("SectionTraining"), Flow(countIn, audible, silent), blind,
            Hint("GroupAccentRule"));
        return CreateOptionPanel("RhythmEditorTitle", "RhythmOptions", content, "RhythmEditorHelp",
            () =>
            {
                var current = _document.Settings;
                beats.Value = current.BeatsPerBar;
                SelectChoice(denominator, current.BeatDenominator.ToString(CultureInfo.InvariantCulture));
                groups.Text = MeterPresets.GroupsText(current.Groups);
                division.Value = current.Subdivision;
                swing.Value = current.Swing;
                countIn.Value = current.CountInBars;
                audible.Value = current.AudibleBars;
                silent.Value = current.SilentBars;
                blind.IsChecked = current.BlindGaps;
            },
            async () =>
            {
                var newBeats = Integer(beats);
                var newDenominator = int.Parse(Selected(denominator), CultureInfo.InvariantCulture);
                if (!MeterPresets.TryParseGroups(groups.Text, newBeats, out var newGroups))
                    throw new ArgumentException(Text.Get("GroupsInvalid"));
                var newDivision = Integer(division);
                var newSwing = Math.Round(Value(swing), 2);
                var newCountIn = Integer(countIn);
                var newAudible = Integer(audible);
                var newSilent = Integer(silent);
                var newBlind = blind.IsChecked == true;
                var applied = false;
                await CommitAsync(doc =>
                {
                    var target = doc.Settings;
                    if (target.BeatsPerBar != newBeats || target.BeatDenominator != newDenominator)
                        MeterPresets.Apply(target, newBeats, newDenominator, newGroups);
                    else target.Groups = newGroups;
                    target.Subdivision = newDivision;
                    target.Swing = newSwing;
                    target.CountInBars = newCountIn;
                    target.AudibleBars = newAudible;
                    target.SilentBars = newSilent;
                    target.BlindGaps = newBlind;
                    applied = true;
                }, editorChange: EditorChangeKind.PlayStructure);
                if (applied) BuildPage();
                return applied;
            });
    }

    private OptionPanel BuildPracticePanel()
    {
        var settings = _document.Settings;
        var seconds = NumberInt("PracticeSeconds", "PracticeSeconds", settings.PracticeSeconds, 0, 86400);
        var beats = NumberInt("PracticeBeats", "PracticeBeats", settings.PracticeBeats, 0, 6_400_000);
        var bars = NumberInt("PracticeBars", "PracticeBars", settings.PracticeBars, 0, 100_000);
        var feedback = Body(Text.Format("PracticeLimitsSaved", settings.PracticeSeconds, settings.PracticeBeats, settings.PracticeBars));
        feedback.FontSize = 12;
        Identify(feedback, "PracticeLimitsStatus", feedback.Text);
        AutomationProperties.SetItemStatus(feedback, "0");
        long commitVersion = 0;
        return CreateOptionPanel("PracticeTimer", "PracticeTimerOptions",
            Column(seconds, beats, bars, Hint("PracticeTimerHint"), Hint("CountedStopHint"), feedback), null,
            () =>
            {
                var current = _document.Settings;
                seconds.Value = current.PracticeSeconds;
                beats.Value = current.PracticeBeats;
                bars.Value = current.PracticeBars;
            },
            async () =>
            {
                var newSeconds = Integer(seconds);
                var newBeats = Integer(beats);
                var newBars = Integer(bars);
                var applied = false;
                await CommitAsync(doc =>
                {
                    doc.Settings.PracticeSeconds = newSeconds;
                    doc.Settings.PracticeBeats = newBeats;
                    doc.Settings.PracticeBars = newBars;
                    applied = true;
                }, editorChange: EditorChangeKind.PracticeTimerOnly);
                if (!applied) return false; // A locked document reports its notice without publishing.
                feedback.Text = Text.Format("PracticeLimitsSaved", newSeconds, newBeats, newBars);
                AutomationProperties.SetName(feedback, feedback.Text);
                AutomationProperties.SetItemStatus(feedback,
                    FormattableString.Invariant($"{++commitVersion}:{newSeconds}:{newBeats}:{newBars}"));
                return true;
            });
    }

    private OptionPanel BuildProjectPanel()
    {
        var name = Input("Name", "DocumentName", _document.Name);
        OptionPanel? project = null;
        // The name is a draft until Done; saving the project must save what the field shows, not the old name.
        async Task CommitNameAsync()
        {
            var newName = name.Text;
            if (newName != _document.Name) await CommitAsync(doc => doc.Name = newName, false);
        }
        var save = ActionButton("Save", "PlaySave", async () =>
        {
            await CommitNameAsync();
            project!.Flyout.Hide();
            await SaveProjectAsync();
        });
        var open = ActionButton("Open", "PlayOpen", async () => { project!.Flyout.Hide(); await OpenProjectAsync(); });
        project = CreateOptionPanel("ProjectOptions", "ProjectOptionsPanel", Column(name, CommandFlow(save, open)), null,
            () => name.Text = _document.Name,
            async () =>
            {
                var applied = false;
                var newName = name.Text;
                await CommitAsync(doc => { doc.Name = newName; applied = true; }, false);
                return applied;
            });
        return project;
    }

    private OptionPanel BuildHelpPanel() => CreateOptionPanel("Help", "PlayHelpPanel",
        Column(Copy("FirstUseHint"), Copy("LiveSoundHint"), Copy("PlayEditBoundary"), Copy("PresentationLimits"),
            Copy("KeyboardHelp"), Copy("Horizon")), null, null, null);
}
