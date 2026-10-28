using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    /// <summary>Controls a command can put the focus on once the page has been rebuilt, by automation id.</summary>
    private readonly Dictionary<string, Control> _studioFocusTargets = [];
    private string[]? _studioFocusAfterRebuild;

    private void TrackStudioFocus(string id, Control control) => _studioFocusTargets[id] = control;

    /// <summary>
    /// After the next rebuild of the Studio the focus goes to the first of these controls that can take it, so a structural
    /// change does not lose it.
    /// </summary>
    private void FocusAfterRebuild(params string[] ids) => _studioFocusAfterRebuild = ids;

    /// <summary>
    /// Undo and Redo rebuild the page. The focus stays on the command that was used, or goes to the other one when this one
    /// has nothing left to do, so a keyboard user can keep stepping back and forth.
    /// </summary>
    private async Task UndoKeepingFocusAsync(bool redo)
    {
        FocusAfterRebuild(redo ? new[] { "StudioRedo", "StudioUndo" } : new[] { "StudioUndo", "StudioRedo" });
        try { await UndoEditorAsync(redo); }
        finally { _studioFocusAfterRebuild = null; }
    }

    private UIElement BuildStudio()
    {
        _studioFocusTargets.Clear();
        var revision = _editorContentRevision;
        var undo = EditorControl(IconButton("\uE7A7", "Undo", "StudioUndo",
            () => RunEditorOperationAsync(revision, () => UndoKeepingFocusAsync(false))), () => !_isLocked && _undo.Count > 0);
        var redo = EditorControl(IconButton("\uE7A6", "Redo", "StudioRedo",
            () => RunEditorOperationAsync(revision, () => UndoKeepingFocusAsync(true))), () => !_isLocked && _redo.Count > 0);
        TrackStudioFocus("StudioUndo", undo);
        TrackStudioFocus("StudioRedo", redo);
        var euclidean = BuildEuclideanPanel(revision);
        var addLane = BuildAddLaneButton(revision, euclidean);
        TrackStudioFocus("AddLane", addLane);

        // The one command that makes lanes sits in the header, or in the empty state while there is nothing yet to add to.
        var cards = new List<UIElement>();
        if (_isLocked)
        {
            // The page and the switch are named as this language names them.
            var lockedText = Text.Format("StudioLocked", Text.Get("Stage"), Text.Get("Lock"));
            var locked = Card(null, new TextBlock { Text = lockedText, Style = ResonantStyle("NoteStyle") });
            Identify(locked, "StudioLocked", lockedText);
            cards.Add(locked);
        }
        var editable = new List<UIElement>();
        if (_document.Lanes.Count == 0)
        {
            var empty = Card(null, EmptyState("\uE9E9", "StudioEmptyTitle", "StudioEmptyBody", addLane));
            Identify(empty, "StudioEmpty", Text.Get("StudioEmptyTitle"));
            editable.Add(empty);
        }
        for (var index = 0; index < _document.Lanes.Count; index++) editable.Add(BuildLaneCard(index));
        editable.Add(BuildProgramCard());
        editable.Add(BuildAdvancedCard(revision));
        // A locked project can be looked at, not changed (the lock is switched on the Stage page). Only a control can be
        // disabled, and disabling it disables everything inside, so each card sits in one while the lock is on.
        foreach (var card in editable)
            cards.Add(!_isLocked ? card : new ContentControl
            {
                Content = card, IsEnabled = false, IsTabStop = false,
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top
            });

        var actions = _document.Lanes.Count == 0 ? CommandRow(undo, redo) : CommandRow(undo, redo, addLane);
        var page = PageScaffoldText("Studio", Text.Format("StudioPurpose", TextFit.Shorten(_document.Name, 48)), actions,
            cards.ToArray());
        if (_studioFocusAfterRebuild is { } wanted)
        {
            _studioFocusAfterRebuild = null;
            var candidates = wanted.Where(_studioFocusTargets.ContainsKey).Select(key => _studioFocusTargets[key]).ToArray();
            if (candidates.Length > 0)
                // On a later turn, so a panel that is closing does not hand the focus back to the control it opened from.
                candidates[0].Loaded += (_, _) => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (_isClosed) return;
                    // The first that can take it: Undo with nothing left to undo hands the focus to Redo.
                    var target = candidates.FirstOrDefault(item => item.IsEnabled) ?? candidates[0];
                    target.Focus(FocusState.Programmatic);
                    if (target is TextBox box) box.SelectAll();
                });
        }
        return page;
    }

    /// <summary>"Add lane" with the kinds of lane to start from. A lane is added in one step; Euclidean asks for its numbers first.</summary>
    private Button BuildAddLaneButton(int revision, OptionPanel euclidean)
    {
        var button = new Button { Style = ResonantStyle("PrimaryButtonStyle") };
        LabelButton(button, "AddLane", "\uE710");
        if (button.Content is Grid row)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var chevron = new FontIcon { Glyph = "\uE70D", FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(chevron, 2);
            row.Children.Add(chevron);
        }
        Identify(button, "AddLane", Text.Get("AddLane"));

        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string key, string id)
        {
            var item = new MenuFlyoutItem { Text = Text.Get(key) };
            AutomationProperties.SetAutomationId(item, id);
            return item;
        }
        void AddKind(string key, string id, int pulses, double quarters)
        {
            var item = Item(key, id);
            item.Click += async (_, _) => await GuardAsync(() => RunEditorOperationAsync(revision, () => AddLaneAsync(pulses, quarters)));
            menu.Items.Add(item);
        }
        AddKind("LanePlain", "AddLanePlain", 4, 4);
        AddKind("LaneThreeInFour", "AddPolyrhythm", 3, 4);
        AddKind("LaneThreeInThree", "AddPolymeter", 3, 3);
        menu.Items.Add(new MenuFlyoutSeparator());
        var euclideanItem = Item("LaneEuclidean", "AddEuclidean");
        // The menu is still closing when it is clicked; the panel opens on the next turn so the two never fight for the button.
        euclideanItem.Click += (_, _) => DispatcherQueue.TryEnqueue(() => euclidean.Flyout.ShowAt(button));
        menu.Items.Add(euclideanItem);
        button.Flyout = menu;

        if (_isLocked) button.IsEnabled = false;
        else if (_document.Lanes.Count >= LaneEdits.MaxLanes)
        {
            button.IsEnabled = false;
            ToolTipService.SetToolTip(button, Text.Get("LaneLimit"));
        }
        return button;
    }

    private async Task AddLaneAsync(int pulses, double quarters)
    {
        await CommitEditorAsync(doc =>
        {
            if (doc.Lanes.Count >= LaneEdits.MaxLanes) throw new ArgumentException(Text.Get("LaneLimit"));
            doc.Lanes.Add(new RhythmLane
            {
                Name = LaneEdits.NextName(doc.Lanes.Select(lane => lane.Name), Text.Get("Lane")),
                Pulses = pulses, CycleQuarters = quarters,
                Pattern = Enumerable.Repeat(Accent.Normal, pulses).ToArray()
            });
        });
        // The new lane's name is selected, so typing replaces it.
        FocusAfterRebuild($"Lane{_document.Lanes.Count - 1}Name");
        RebuildPreservingScroll();
    }

    /// <summary>
    /// The Euclidean generator as a panel opened from "Add lane": its numbers, one sentence saying what they do, and a preview
    /// of the pattern (● for a click, ○ for a rest). Create adds the lane.
    /// </summary>
    private OptionPanel BuildEuclideanPanel(int revision)
    {
        var hits = NumberInput("EuclideanHits", "EuclideanAttacks", 5, 0, 256);
        var steps = NumberInput("EuclideanSteps", "EuclideanSteps", 8, 1, 256);
        var rotation = NumberInput("Rotation", "EuclideanRotation", 0, -256, 256);
        var cycle = NumberInput("LaneCycle", "EuclideanCycle", 4, 0.125, 256, 0.125, 3, whole: false, trimmed: true);
        var explain = new TextBlock { Style = ResonantStyle("NoteStyle") };
        var previewLabel = new TextBlock { Text = Text.Get("EuclideanPreviewName"), Style = ResonantStyle("FieldCaptionStyle") };
        var preview = new TextBlock { Style = ResonantStyle("BodyStyle"), FontSize = 16, TextWrapping = TextWrapping.Wrap };
        Identify(preview, "EuclideanPreview", Text.Get("EuclideanPreviewName"));
        AutomationProperties.SetLabeledBy(preview, previewLabel);

        // A count that is not a whole number or not valid yet shows no pattern; Create reports it.
        static bool TryWhole(NumberBox box, out int value)
        {
            value = 0;
            return double.IsFinite(box.Value) && WholeNumbers.TryGet(box.Value, out value);
        }
        void Refresh()
        {
            string? problem = null;
            int attacks = 0, count = 0, turn = 0;
            // A blank or fractional box is not a number of hits, steps or rotation at all; hits beyond the steps is another thing.
            if (!TryWhole(hits, out attacks) || !TryWhole(steps, out count) || !TryWhole(rotation, out turn)) problem = "EuclideanWhole";
            else if (attacks < 0 || count < 1 || attacks > count) problem = "EuclideanInvalid";
            var label = Text.Get("EuclideanPreviewName");
            if (problem is not null)
            {
                explain.Text = Text.Get(problem);
                preview.Text = "";
                AutomationProperties.SetName(preview, label);
                return;
            }
            explain.Text = Text.Format("EuclideanExplain", attacks, count);
            preview.Text = EuclideanPreview.Format(RhythmGenerators.Euclidean(attacks, count, 4, turn).Events
                .Select(item => item.Accent != Accent.Silent));
            // A screen reader announces the name and not the glyphs, so the pattern is part of the name.
            AutomationProperties.SetName(preview, $"{label}: {preview.Text}");
        }
        foreach (var box in new[] { hits, steps, rotation }) box.ValueChanged += (_, _) => Refresh();

        var content = new StackPanel
        {
            Spacing = 12,
            Children =
            {
                FieldRow(Field(hits), Field(steps), Field(rotation)),
                Field(cycle, "UnitQuarterNotes"),
                explain,
                new StackPanel { Spacing = 4, Children = { previewLabel, preview } }
            }
        };
        return CreateOptionPanel("Euclidean", "EuclideanOptions", content, null,
            () =>
            {
                hits.Value = 5;
                steps.Value = 8;
                rotation.Value = 0;
                cycle.Value = 4;
                Refresh();
            },
            async () =>
            {
                var attacks = Integer(hits);
                var count = Integer(steps);
                var turn = Integer(rotation);
                var length = Rational.FromDouble(Value(cycle));
                var created = false;
                await RunEditorOperationAsync(revision, async () =>
                {
                    await CommitEditorAsync(doc =>
                    {
                        if (doc.Lanes.Count >= LaneEdits.MaxLanes) throw new ArgumentException(Text.Get("LaneLimit"));
                        var lane = RhythmGenerators.Euclidean(attacks, count, length, turn);
                        lane.Name = $"{Text.Get("Euclidean")} {attacks}/{count}";
                        doc.Lanes.Add(lane);
                    });
                    created = true;
                    FocusAfterRebuild($"Lane{_document.Lanes.Count - 1}Name");
                    RebuildPreservingScroll();
                });
                return created;
            }, "Create");
    }

    /// <summary>The practice program, until its steps get cards of their own: the older editor for each step, inside one card.</summary>
    private UIElement BuildProgramCard()
    {
        var steps = new List<UIElement>();
        for (var index = 0; index < _document.Program.Count; index++) steps.Add(BuildStepEditor(index));
        steps.Add(EditorActionButton("AddStep", "AddProgramStep", async () =>
        {
            await CommitEditorAsync(doc => doc.Program.Add(new ProgramStep
            {
                Name = $"{Text.Get("Step")} {doc.Program.Count + 1}",
                StartTempo = doc.Settings.Tempo,
                EndTempo = doc.Settings.Tempo,
                BeatsPerBar = doc.Settings.BeatsPerBar,
                BeatDenominator = doc.Settings.BeatDenominator
            }));
            RebuildPreservingScroll();
        }));
        return Card("TrainProgramTitle", Column(steps.ToArray()), "TrainProgramHelp", id: "ProgramCard");
    }

    /// <summary>What decides the random pattern behind a lane's Chance, with what it does.</summary>
    private UIElement BuildAdvancedCard(int revision)
    {
        var seed = new TextBox { Text = _document.Seed.ToString(CultureInfo.InvariantCulture), Width = 180, MaxLength = 11, HorizontalAlignment = HorizontalAlignment.Left };
        Identify(seed, "DocumentSeed", Text.Get("SeedLabel"));
        CommitText(seed, () => _document.Seed.ToString(CultureInfo.InvariantCulture), async text =>
        {
            if (!int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException(Text.Get("SeedInvalid"));
            await CommitStudioAsync(revision, "Seed", doc => doc.Seed = value);
        });
        return Card("Advanced", Field(seed), "AdvancedHelp", id: "AdvancedCard");
    }

    private UIElement BuildStepEditor(int index)
    {
        var step = _document.Program[index];
        var id = $"Program{index}";
        var name = Input("Name", id + "Name", step.Name);
        var start = Number("StartTempo", id + "StartTempo", step.StartTempo, 1, 1000, 0.1);
        var end = Number("EndTempo", id + "EndTempo", step.EndTempo, 1, 1000, 0.1);
        var bars = NumberInt("Bars", id + "Bars", step.Bars, 1, 100000);
        var repeats = NumberInt("Repeats", id + "Repeats", step.Repeats, 1, 1024);
        var ramp = Select("Ramp", id + "Ramp", step.Ramp, "Constant", "LinearTime", "LinearBeat");
        var beats = NumberInt("Beats", id + "Beats", step.BeatsPerBar, 1, 64);
        var denominator = Select("Denominator", id + "Denominator", step.BeatDenominator.ToString(CultureInfo.InvariantCulture), "1", "2", "4", "8", "16", "32", "64");
        var audible = NumberInt("AudibleBars", id + "Audible", step.AudibleBars, 0, 100000);
        var silent = NumberInt("SilentBars", id + "Silent", step.SilentBars, 0, 100000);
        var content = Column(Flow(name, start, end, bars, repeats, ramp, beats, denominator, audible, silent),
            CommandFlow(EditorActionButton("Apply", id + "Apply", async () =>
            {
                await CommitEditorAsync(doc =>
                {
                    var edited = doc.Program[index];
                    edited.Name = name.Text;
                    edited.StartTempo = Value(start);
                    edited.EndTempo = Value(end);
                    edited.Bars = Integer(bars);
                    edited.Repeats = Integer(repeats);
                    edited.Ramp = Selected(ramp);
                    edited.BeatsPerBar = Integer(beats);
                    edited.BeatDenominator = int.Parse(Selected(denominator), CultureInfo.InvariantCulture);
                    edited.AudibleBars = Integer(audible);
                    edited.SilentBars = Integer(silent);
                });
                RebuildPreservingScroll();
            }),
            EditorActionButton("MoveUp", id + "MoveUp", async () =>
            {
                if (index == 0) return;
                await CommitEditorAsync(doc => (doc.Program[index - 1], doc.Program[index]) = (doc.Program[index], doc.Program[index - 1]));
                RebuildPreservingScroll();
            }),
            EditorActionButton("MoveDown", id + "MoveDown", async () =>
            {
                if (index >= _document.Program.Count - 1) return;
                await CommitEditorAsync(doc => (doc.Program[index + 1], doc.Program[index]) = (doc.Program[index], doc.Program[index + 1]));
                RebuildPreservingScroll();
            }),
            EditorActionButton("Remove", id + "Remove", async () =>
            {
                if (!await ConfirmAsync(Text.Format("ConfirmRemove", step.Name), "Remove")) return;
                await CommitEditorAsync(doc => doc.Program.RemoveAt(index));
                RebuildPreservingScroll();
            })));
        var expander = new Expander { Header = $"{index + 1}. {step.Name}", Content = content, HorizontalAlignment = HorizontalAlignment.Stretch };
        Identify(expander, id + "Editor", step.Name);
        return expander;
    }
}
