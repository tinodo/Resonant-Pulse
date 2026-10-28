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
        // Studio fields apply in turn, off the page. Edits asked for before this page was built and still being applied are not in
        // the project it is built from, so its cards and its commands stay off until they are, and the page is then built again
        // from the project as it is. Nothing can be typed or clicked meanwhile, so nothing is replaced under the person's hands,
        // and no command is queued with the revision of a page that is about to be replaced. (The edit lock is free again once the
        // last edit has been applied, and the page that follows an edit is built after that.)
        var settling = _edits.CurrentCount == 0;
        var wantedFocus = _studioFocusAfterRebuild;
        var undo = EditorControl(IconButton("\uE7A7", "Undo", "StudioUndo",
            () => RunEditorOperationAsync(revision, () => UndoKeepingFocusAsync(false))), () => !_isLocked && !settling && _undo.Count > 0);
        var redo = EditorControl(IconButton("\uE7A6", "Redo", "StudioRedo",
            () => RunEditorOperationAsync(revision, () => UndoKeepingFocusAsync(true))), () => !_isLocked && !settling && _redo.Count > 0);
        TrackStudioFocus("StudioUndo", undo);
        TrackStudioFocus("StudioRedo", redo);
        var euclidean = BuildEuclideanPanel(revision);
        var addLane = BuildAddLaneButton(revision, euclidean, settling);
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
        editable.AddRange(BuildProgramSection(revision));
        editable.Add(BuildAdvancedCard(revision));
        // A locked project can be looked at, not changed (the lock is switched on the Stage page), and so can a page that is still
        // settling. Only a control can be disabled, and disabling it disables everything inside, so each card sits in one then.
        var inert = _isLocked || settling;
        foreach (var card in editable)
            cards.Add(!inert ? card : new ContentControl
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
        if (settling) _ = GuardAsync(() => SettleStudioAsync(_pageGeneration, wantedFocus));
        return page;
    }

    /// <summary>
    /// Builds the Studio page again, from the project as it is, once the edits that were still being applied when it was built
    /// are in (see <see cref="BuildStudio"/>). Nothing happens if the person has gone to another page, or the page was built
    /// again for another reason, meanwhile: that build decides for itself whether it has to wait.
    /// </summary>
    private async Task SettleStudioAsync(int generation, string[]? focus)
    {
        await WaitForPendingEditsAsync();
        if (_isClosed || generation != _pageGeneration || _view != "Studio") return;
        if (focus is not null) FocusAfterRebuild(focus);
        RebuildPreservingScroll();
    }

    /// <summary>"Add lane" with the kinds of lane to start from. A lane is added in one step; Euclidean asks for its numbers first.</summary>
    private Button BuildAddLaneButton(int revision, OptionPanel euclidean, bool settling)
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
        void AddKind(string key, string id, int pulses, int beats)
        {
            var item = Item(key, id);
            item.Click += async (_, _) => await GuardAsync(() => RunEditorOperationAsync(revision, () => AddLaneAsync(pulses, beats)));
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

        if (_isLocked || settling) button.IsEnabled = false;
        else if (_document.Lanes.Count >= LaneEdits.MaxLanes)
        {
            button.IsEnabled = false;
            ToolTipService.SetToolTip(button, Text.Get("LaneLimit"));
        }
        return button;
    }

    /// <summary>
    /// Adds a lane of <paramref name="pulses"/> clicks over <paramref name="beats"/> beats of the project's time signature (the
    /// labels in the menu say "beats"), so the lane is what its label promises in 6/8 or 3/2 as much as in 4/4.
    /// </summary>
    private async Task AddLaneAsync(int pulses, int beats)
    {
        await CommitEditorAsync(doc =>
        {
            if (doc.Lanes.Count >= LaneEdits.MaxLanes) throw new ArgumentException(Text.Get("LaneLimit"));
            doc.Lanes.Add(new RhythmLane
            {
                Name = LaneEdits.NextName(doc.Lanes.Select(lane => lane.Name), Text.Get("Lane")),
                Pulses = pulses, CycleQuarters = LaneEdits.CycleQuarters(beats, doc.Settings.BeatDenominator),
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

    /// <summary>What decides the random pattern behind a lane's Chance, with what it does.</summary>
    private UIElement BuildAdvancedCard(int revision)
    {
        var seed = EditorControl(new TextBox { Text = _document.Seed.ToString(CultureInfo.InvariantCulture), Width = 180, MaxLength = 11, HorizontalAlignment = HorizontalAlignment.Left });
        Identify(seed, "DocumentSeed", Text.Get("SeedLabel"));
        CommitText(seed, () => _document.Seed.ToString(CultureInfo.InvariantCulture), async text =>
        {
            if (!int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
                throw new ArgumentException(Text.Get("SeedInvalid"));
            await CommitStudioAsync(revision, "Seed", doc => doc.Seed = value);
        });
        return Card("Advanced", Field(seed), "AdvancedHelp", id: "AdvancedCard");
    }
}
