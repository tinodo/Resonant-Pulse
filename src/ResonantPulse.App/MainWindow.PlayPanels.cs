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
        Action? populate, Func<Task<bool>>? apply, string doneKey = "CloseOptions")
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
        // [Cancel] [Done] are one pair: equal cells, each button stretches to its cell, so they are the same size however
        // either one's style or label changes. The cell width is set when the panel opens (FitFooter), so a panel narrower
        // than the pair makes both buttons narrower together instead of clipping one.
        var done = new Button
        {
            Style = ResonantStyle("PrimaryButtonStyle"),
            HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch
        };
        LabelButton(done, doneKey);
        Identify(done, id + "Done", Text.Get(doneKey));
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

        // Rows exist only for what is shown (Grid.RowSpacing is paid between every pair of rows, even empty ones), and the
        // scrolling middle takes whatever height is left, so the footer stays on screen (see SizeOptionsFlyout).
        var panel = new Grid { RowSpacing = 12 };
        var rows = 0;
        void Place(FrameworkElement element, GridLength height)
        {
            panel.RowDefinitions.Add(new RowDefinition { Height = height });
            Grid.SetRow(element, rows++);
            panel.Children.Add(element);
        }
        Place(title, GridLength.Auto);
        if (helpKey is not null) Place(new TextBlock { Text = Text.Get(helpKey), Style = ResonantStyle("CaptionStyle") }, GridLength.Auto);
        Place(scroll, new GridLength(1, GridUnitType.Star));
        // The inline error sits just above the footer; while it is hidden it takes no room.
        Place(new StackPanel { Spacing = 8, Children = { error, footer } }, GridLength.Auto);
        panel.KeyDown += OnKeyDown;

        var flyout = new Flyout { Content = panel };
        var isOpen = false;
        flyout.Opening += (_, _) =>
        {
            SizeOptionsFlyout(flyout, panel, scroll, footer);
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

    /// <summary>
    /// Sizes a panel to the window. A code-built panel (it has a footer) lets its scrolling middle take the height left after
    /// the title, help and footer, and its presenter does not scroll, so Cancel and Done are always on screen wherever Windows
    /// places the flyout: Auto placement above a button low in a small window leaves a fraction of the window, and a fixed
    /// height budget would push the footer out of sight. The Audio panel (built in XAML) keeps its fixed budget.
    /// </summary>
    private void SizeOptionsFlyout(Flyout flyout, Grid panel, ScrollViewer scroll, Grid? footer = null)
    {
        if (footer is null) scroll.MaxHeight = Math.Max(100, Root.ActualHeight - 220);
        panel.Width = Math.Clamp(Root.ActualWidth - 100, 240, 560);
        if (footer is not null)
        {
            var cell = FormMetrics.FooterCell(panel.Width, footer.ColumnSpacing, footer.ColumnDefinitions.Count, FooterButtonWidth);
            foreach (var column in footer.ColumnDefinitions) column.Width = new GridLength(cell);
        }
        panel.RequestedTheme = Root.RequestedTheme;
        var presenter = new Style(typeof(FlyoutPresenter))
        {
            Setters =
            {
                new Setter(FrameworkElement.MaxWidthProperty, Math.Max(280, Root.ActualWidth - 32)),
                new Setter(FrameworkElement.MaxHeightProperty, Math.Max(180, Root.ActualHeight - 32)),
                new Setter(Microsoft.UI.Xaml.Controls.Control.PaddingProperty, new Thickness(16))
            }
        };
        if (footer is not null)
        {
            presenter.Setters.Add(new Setter(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
            presenter.Setters.Add(new Setter(ScrollViewer.VerticalScrollModeProperty, ScrollMode.Disabled));
        }
        flyout.FlyoutPresenterStyle = presenter;
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
        // The digits are drawn at 24 pt, so this box is wider than the 14 pt rule gives.
        var tempo = NumberInput("Tempo", "Tempo", settings.Tempo, 1, 1000, 1, 0, whole: false, atLeast: scale => 200 * scale);
        tempo.FontSize = 24;
        var unit = Dropdown("Unit", "TempoUnit", Invariant(settings.BeatUnitQuarters), TempoUnitChoices(settings));
        var half = ActionButton("HalfTempo", "HalfTempo", () => { tempo.Value = Math.Max(1, Math.Round(tempo.Value / 2, 1)); });
        var twice = ActionButton("DoubleTempo", "DoubleTempo", () => { tempo.Value = Math.Min(1000, Math.Round(tempo.Value * 2, 1)); });
        var tempoContent = new StackPanel { Spacing = 12, Children = { FieldRow(Field(tempo), Field(unit)), CommandRow(half, twice) } };
        return CreateOptionPanel("TempoTab", "TempoOptions", tempoContent, "TempoEditorHelp",
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
                    // The split clicks' strengths are laid out per beat of the old unit.
                    if (unitChanged) doc.Settings.SubdivisionAccents = null;
                    applied = true;
                }, editorChange: unitChanged ? EditorChangeKind.PlayStructure : EditorChangeKind.GlobalTempoOnly);
                if (applied && unitChanged) BuildPage();
                return applied;
            });
    }

    private OptionPanel BuildRhythmPanel()
    {
        var settings = _document.Settings;
        var beats = NumberInput("BeatsTop", "MeterBeats", settings.BeatsPerBar, 1, 64);
        var denominator = Dropdown("BeatNote", "MeterDenominator", settings.BeatDenominator.ToString(CultureInfo.InvariantCulture), NoteChoices());
        var groups = TextInput("GroupingLabel", "MeterGroups", MeterPresets.GroupsText(settings.Groups), 260);
        var division = NumberInput("SubdivisionLabel", "Subdivision", settings.Subdivision, 1, 64);
        var swing = NumberInput("Swing", "Swing", settings.Swing, 0.5, 0.9, 0.01, 2, whole: false);
        var countIn = NumberInput("CountIn", "CountInBars", settings.CountInBars, 0, 32);
        var audible = NumberInput("AudibleBars", "AudibleBars", settings.AudibleBars, 0, 100000);
        var silent = NumberInput("SilentBars", "SilentBars", settings.SilentBars, 0, 100000);
        var blind = Toggle("Blind", "BlindGaps", settings.BlindGaps);
        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                SectionHeading("TimeSignature"), FieldRow(Field(beats), Field(denominator)), Field(groups),
                SectionHeading("SectionFeel"), FieldRow(Field(division), Field(swing)),
                SectionHeading("SectionTraining"), FieldRow(Field(countIn), Field(audible), Field(silent)),
                SettingRow("Blind", null, blind, sideBySideFrom: 280),
                Hint("GroupAccentRule")
            }
        };
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
                blind.IsOn = current.BlindGaps;
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
                var newBlind = blind.IsOn;
                var applied = false;
                await CommitAsync(doc =>
                {
                    var target = doc.Settings;
                    var divisionBefore = target.Subdivision;
                    if (target.BeatsPerBar != newBeats || target.BeatDenominator != newDenominator)
                        MeterPresets.Apply(target, newBeats, newDenominator, newGroups);
                    else target.Groups = newGroups;
                    target.Subdivision = newDivision;
                    // The strengths of the split clicks belong to a split of this size.
                    if (newDivision != divisionBefore) target.SubdivisionAccents = null;
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
        var seconds = NumberInput("PracticeStopAfter", "PracticeSeconds", settings.PracticeSeconds, 0, 86400);
        var beats = NumberInput("PracticeStopAfter", "PracticeBeats", settings.PracticeBeats, 0, 6_400_000);
        var bars = NumberInput("PracticeStopAfter", "PracticeBars", settings.PracticeBars, 0, 100_000);
        var feedback = new TextBlock
        {
            Text = SavedLimits(settings.PracticeSeconds, settings.PracticeBeats, settings.PracticeBars),
            Style = ResonantStyle("NoteStyle")
        };
        Identify(feedback, "PracticeLimitsStatus", feedback.Text);
        AutomationProperties.SetItemStatus(feedback, "0");
        long commitVersion = 0;
        var fields = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                SectionHeading("PracticeStopAfter"),
                UnitRows("PracticeStopAfter", (seconds, "UnitSeconds"), (beats, "UnitBeats"), (bars, "UnitBars")),
                feedback
            }
        };
        return CreateOptionPanel("PracticeTimer", "PracticeTimerOptions", fields, "PracticeTimerHint",
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
                feedback.Text = SavedLimits(newSeconds, newBeats, newBars);
                AutomationProperties.SetName(feedback, feedback.Text);
                AutomationProperties.SetItemStatus(feedback,
                    FormattableString.Invariant($"{++commitVersion}:{newSeconds}:{newBeats}:{newBars}"));
                return true;
            });
    }

    private static string SavedLimits(int seconds, int beats, int bars)
    {
        var off = Text.Get("Off").ToLower(Text.Culture);
        return Text.Format("PracticeLimitsSaved", PracticeFormat.Time(seconds, off, Text.Culture),
            PracticeFormat.Count(beats, off, Text.Culture), PracticeFormat.Count(bars, off, Text.Culture));
    }

    private OptionPanel BuildProjectPanel()
    {
        var name = TextInput("Name", "DocumentName", _document.Name, 360);
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
        }, "\uE74E");
        var open = ActionButton("Open", "PlayOpen", async () => { project!.Flyout.Hide(); await OpenProjectAsync(); }, "\uE8E5");
        project = CreateOptionPanel("ProjectOptions", "ProjectOptionsPanel",
            new StackPanel { Spacing = 12, Children = { Field(name), CommandRow(save, open) } }, null,
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

    private OptionPanel BuildHelpPanel()
    {
        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(SectionHeading("HelpBasics"));
        content.Children.Add(Copy("FirstUseHint"));
        content.Children.Add(SectionHeading("HelpWhilePlaying"));
        content.Children.Add(Copy("LiveSoundHint"));
        content.Children.Add(SectionHeading("HelpShortcuts"));
        var ctrl = Text.Get("KeyCtrl");
        content.Children.Add(Shortcuts(
            (Text.Get("KeySpace"), Text.Get("ShortcutStart")),
            ($"{ctrl} + M", Text.Get("ShortcutMute")),
            ($"{ctrl} + S", Text.Get("Save")),
            ($"{ctrl} + O", Text.Get("Open")),
            ("F", Text.Format("ShortcutHit", Text.Get("Train")))));
        return CreateOptionPanel("HelpTab", "PlayHelpPanel", content, null, null, null);
    }

    /// <summary>
    /// Keyboard shortcuts: each key drawn as a keycap, then what it does. One grid, so the keycap column is as wide as the
    /// widest key at any text size and the descriptions line up; a long description wraps instead of being clipped.
    /// </summary>
    private static Grid Shortcuts(params (string Keys, string Description)[] rows)
    {
        var grid = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < rows.Length; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var chip = KeyChip(rows[index].Keys);
            chip.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetRow(chip, index);
            grid.Children.Add(chip);
            var text = new TextBlock
            {
                Text = rows[index].Description, Style = ResonantStyle("BodyStyle"), FontSize = 14,
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(text, index);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }
        return grid;
    }
}
