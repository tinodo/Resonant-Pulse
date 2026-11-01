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
    /// Done applies and closes; Cancel (or clicking away) before Done discards. Once Done has been pressed the change is on its way, and
    /// Cancel or clicking away only closes the panel: a change that takes a moment (the Done of the Tempo panel waits for the nudges that
    /// are on their way) is still applied, as one that is queued behind another edit is. Fields are refilled from the document
    /// every time the panel opens, so a cancelled draft never lingers. A <paramref name="readOnly"/> panel only shows text (Help):
    /// it has nothing to cancel, so it has Done alone, and its scrolling area is a tab stop so that a keyboard can read it.
    /// <paramref name="titleExtra"/> sits at the right of the title and wraps inside its own column. The panel's height is what its
    /// fields share with the footer, so what belongs beside the title costs the fields next to nothing (a line of its own would).
    /// </summary>
    private OptionPanel CreateOptionPanel(string titleKey, string id, UIElement content, string? helpKey,
        Action? populate, Func<Task<bool>>? apply, string doneKey = "CloseOptions", bool readOnly = false,
        FrameworkElement? titleExtra = null)
    {
        var title = new TextBlock
        {
            Text = Text.Get(titleKey), FontSize = 20, FontWeight = FontWeights.SemiBold,
            Style = ResonantStyle("BodyStyle")
        };
        var scroll = Scroll(content);
        // A read-only panel gets room all round, so the focus ring of its scrolling area never touches the text.
        scroll.Padding = readOnly ? new Thickness(6, 6, 12, 6) : new Thickness(0, 0, 12, 0);
        if (readOnly)
        {
            // Text has nothing to focus, so without this a keyboard cannot get to the part of it below the fold. The scrolling area is
            // the one tab stop: its scrolling keys read it (the first-use verifier presses Page Down, End and Home), and Tab goes on
            // to Done.
            scroll.IsTabStop = true;
            scroll.UseSystemFocusVisuals = true;
            Identify(scroll, id + "Scroll", Text.Get(titleKey));
            AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        }
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
        FrameworkElement header = title;
        if (titleExtra is not null)
        {
            // The title and what sits beside it are centred on one line (a taller block grows the line, not the title).
            var line = new Grid { ColumnSpacing = 12 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            title.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(titleExtra, 1);
            line.Children.Add(title);
            line.Children.Add(titleExtra);
            header = line;
        }
        Place(header, GridLength.Auto);
        if (helpKey is not null) Place(new TextBlock { Text = Text.Get(helpKey), Style = ResonantStyle("CaptionStyle") }, GridLength.Auto);
        Place(scroll, new GridLength(1, GridUnitType.Star));
        // The inline error sits just above the footer; while it is hidden it takes no room.
        Place(new StackPanel { Spacing = 8, Children = { error, footer } }, GridLength.Auto);
        panel.KeyDown += OnKeyDown;

        var flyout = new Flyout { Content = panel };
        var isOpen = false;
        var startAtTop = false;
        flyout.Opening += (_, _) =>
        {
            SizeOptionsFlyout(flyout, panel, scroll, footer);
            error.Visibility = Visibility.Collapsed;
            startAtTop = readOnly;
            populate?.Invoke();
        };
        // Help opens at its first line every time, not where it was left. The reset waits for the scrolling area to load, which
        // is once each time the panel opens: asking for it in Opening, before that, left the text where it was.
        if (readOnly)
            scroll.Loaded += (_, _) =>
            {
                if (!startAtTop) return;
                startAtTop = false;
                scroll.ChangeView(null, 0, null, true);
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
        // How many steady taps start the metronome when Tap to start is on. It belongs to the app, not to the project: it is
        // used the next time Tap to start is switched on, and it is remembered for the next session.
        var taps = NumberInput("TapStartCountField", "TapInCount", TapStartRules.ClampTaps(_preferences.TapInCount),
            TapStartRules.MinimumTaps, TapStartRules.MaximumTaps);
        // The tempo guide, beside the panel's title (so it costs the fields below it no height): the approximate Italian markings of the
        // speed that the panel holds. It follows the number (when the box accepts it: Enter, leaving it, a stepper, Half or Double
        // tempo) and the note value as they change, before Done. It only reads the two fields: nothing it works out is written.
        var guide = new TextBlock
        {
            Style = ResonantStyle("CaptionStyle"), TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        Identify(guide, "TempoGuide", "");
        void ShowGuide()
        {
            var unitQuarters = unit.SelectedItem is Choice choice ? double.Parse(choice.Value, CultureInfo.InvariantCulture) : double.NaN;
            var line = TempoGuide.Describe(tempo.Value, unitQuarters);
            guide.Text = line;
            AutomationProperties.SetName(guide, line);
        }
        tempo.ValueChanged += (_, _) => ShowGuide();
        unit.SelectionChanged += (_, _) => ShowGuide();
        ShowGuide();
        // A screen reader that reaches the box hears the guide as its description.
        AutomationProperties.GetDescribedBy(tempo).Add(guide);
        var tempoContent = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                FieldRow(Field(tempo), Field(unit)), CommandRow(half, twice),
                Field(taps, "UnitTaps"), Hint("TapStartCountHelp")
            }
        };
        // What the panel showed when it was opened (set again each time it opens): Done writes only what was changed from it.
        var openedTempo = settings.Tempo;
        var openedUnit = settings.BeatUnitQuarters;
        var opening = 0;
        return CreateOptionPanel("TempoTab", "TempoOptions", tempoContent, "TempoEditorHelp",
            () =>
            {
                opening++;
                openedTempo = _tempoNudges.Requested ?? _document.Settings.Tempo;
                openedUnit = _document.Settings.BeatUnitQuarters;
                tempo.Value = openedTempo;
                SelectChoice(unit, Invariant(openedUnit));
                taps.Value = TapStartRules.ClampTaps(_preferences.TapInCount);
                ShowGuide();
            },
            // A second press of Done in this opening of the panel, while the first is still being applied (it waits for the nudges that
            // are on their way), does nothing.
            TempoEdits.OneAtATime(async () =>
            {
                var bpm = Math.Round(Value(tempo), 1);
                var unitValue = double.Parse(Selected(unit), CultureInfo.InvariantCulture);
                var newTaps = Integer(taps);
                // Only the fields that were changed in the panel are written. A tempo or unit that still shows what it showed when
                // the panel opened would put that value over a newer one (the tempo a Tap to start saved meanwhile, or a nudge
                // that was still on its way), and a number of taps changed alone changes nothing of the project.
                var (tempoEdited, unitEdited) = TempoEdits.PanelEdits(openedTempo, openedUnit, bpm, unitValue);
                if (tempoEdited || unitEdited)
                {
                    // A tempo or unit chosen here by hand ends a Tap to start, and it does so before the commit: a commit that
                    // fails after it has published the change (the recovery file cannot be written) must not leave an intent
                    // that is still listening and would later overwrite what was chosen. A new number of taps only counts for the
                    // next one.
                    CancelTapIn();
                    // The project this was typed for: it can be replaced (Open, New, a ready-made rhythm) while this waits for the nudges,
                    // and what was typed belongs to the one that went, so nothing is written into its replacement.
                    var typedFor = _projectGeneration;
                    var applied = false;
                    var replaced = false;
                    try
                    {
                        // The tempo that is nudged is committed by a loop of its own, and the edit queue is first in, first out: a commit
                        // made now, while the loop is still committing, would be queued between two of the loop's, and the loop would
                        // then commit its older value over this one. So this commit waits for a Done pressed before this one and for the
                        // loop, until no loop is running; Start and Tap to start wait for it as part of the tempo being settled, and closing
                        // does for at most its four seconds: what was nudged before this commit was queued lands first, and the tempo typed
                        // here is the last word.
                        await _tempoNudges.AfterNudgesAsync(() => CommitAsync(doc =>
                        {
                            if (typedFor != _projectGeneration)
                            {
                                replaced = true;
                                throw new OperationCanceledException();
                            }
                            if (tempoEdited) doc.Settings.Tempo = bpm;
                            if (unitEdited)
                            {
                                doc.Settings.BeatUnitQuarters = unitValue;
                                // The split clicks' strengths are laid out per beat of the old unit.
                                doc.Settings.SubdivisionAccents = null;
                            }
                            applied = true;
                        }, editorChange: unitEdited ? EditorChangeKind.PlayStructure : EditorChangeKind.GlobalTempoOnly));
                    }
                    catch (OperationCanceledException) when (replaced) { }
                    if (!applied && !replaced) return false;
                    // The project holds the new unit now, so the page is built for it whether or not the number of taps can be
                    // remembered: a refused write of the preferences must not leave the page of the old unit behind.
                    if (applied && unitEdited) BuildPage();
                }
                if (newTaps != TapStartRules.ClampTaps(_preferences.TapInCount))
                {
                    _preferences.TapInCount = newTaps;
                    // Applied at once, like every setting of the app: remembering it for the next session is best effort, and
                    // if that fails the error is shown and the value stays.
                    await _workspace.SavePreferencesAsync(_preferences);
                }
                return true;
            }, () => opening), titleExtra: guide);
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

    /// <summary>
    /// The Help panel: read-only, on Play (docs/design/pages-design.md). What it says is HelpContent's; this draws it: a heading is a
    /// heading for a screen reader, the quick start is a numbered list, and an example sits in a quiet box under its entry.
    /// </summary>
    private OptionPanel BuildHelpPanel()
    {
        var content = new StackPanel { Spacing = 12 };
        var steps = new List<HelpEntry>();
        void FlushSteps()
        {
            if (steps.Count == 0) return;
            content.Children.Add(HelpSteps(steps));
            steps.Clear();
        }
        foreach (var entry in HelpContent.Entries())
        {
            if (entry.Kind != HelpKind.Step) FlushSteps();
            switch (entry.Kind)
            {
                case HelpKind.Step:
                    steps.Add(entry);
                    break;
                case HelpKind.Heading:
                    content.Children.Add(HelpHeading(entry, 16, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2, content.Children.Count > 0));
                    break;
                case HelpKind.Subheading:
                    content.Children.Add(HelpHeading(entry, 14, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level3, true));
                    break;
                case HelpKind.Example:
                    content.Children.Add(HelpExample(entry));
                    break;
                case HelpKind.Shortcuts:
                    content.Children.Add(HelpShortcuts());
                    break;
                default:
                    var paragraph = Body(entry.Text);
                    Identify(paragraph, entry.Id, entry.Text);
                    content.Children.Add(paragraph);
                    break;
            }
        }
        FlushSteps();
        return CreateOptionPanel("HelpTab", "PlayHelpPanel", content, null, null, null, readOnly: true);
    }

    private static TextBlock HelpHeading(HelpEntry entry, double size, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel level, bool spaceAbove)
    {
        var heading = new TextBlock
        {
            Text = entry.Text, FontSize = size, FontWeight = FontWeights.SemiBold, Style = ResonantStyle("BodyStyle"),
            // A section is told from the one before by the room above it; the first has none.
            Margin = new Thickness(0, spaceAbove ? 8 : 0, 0, -4)
        };
        Identify(heading, entry.Id, entry.Text);
        AutomationProperties.SetHeadingLevel(heading, level);
        return heading;
    }

    /// <summary>The quick start: a number beside each step, so a step that wraps keeps its text clear of the number.</summary>
    private static Grid HelpSteps(IReadOnlyList<HelpEntry> steps)
    {
        var grid = new Grid { ColumnSpacing = 10, RowSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < steps.Count; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var number = new TextBlock
            {
                Text = (index + 1).ToString(Text.Culture), Style = ResonantStyle("StepNumberStyle"), MinWidth = 14,
                TextAlignment = TextAlignment.Right
            };
            // The number is part of the step's own name, so it is not read twice.
            AutomationProperties.SetAccessibilityView(number, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            Grid.SetRow(number, index);
            grid.Children.Add(number);
            var text = Body(steps[index].Text);
            Identify(text, steps[index].Id, $"{index + 1}. {steps[index].Text}");
            Grid.SetRow(text, index);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
        }
        return grid;
    }

    private static Border HelpExample(HelpEntry entry)
    {
        var text = new TextBlock { Text = entry.Text, FontSize = 13, Style = ResonantStyle("BodyStyle") };
        Identify(text, entry.Id, entry.Text);
        return new Border { Style = ResonantStyle("ExampleBoxStyle"), Child = text };
    }

    private static Grid HelpShortcuts()
    {
        var ctrl = Text.Get("KeyCtrl");
        return Shortcuts(
            (Text.Get("KeySpace"), Text.Get("ShortcutStart")),
            ($"{ctrl} + M", Text.Get("ShortcutMute")),
            ($"{ctrl} + S", Text.Get("Save")),
            ($"{ctrl} + O", Text.Get("Open")),
            ("T", Text.Get("ShortcutTap")),
            ("F", Text.Format("ShortcutHit", Text.Get("Train"))));
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
