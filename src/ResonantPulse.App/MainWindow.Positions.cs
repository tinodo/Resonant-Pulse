using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private static Choice[] StrengthChoices() =>
    [
        new("Strong", Text.Get("LevelStrong")), new("Normal", Text.Get("LevelNormal")),
        new("Soft", Text.Get("LevelSoft")), new("Silent", Text.Get("LevelSilent"))
    ];

    /// <summary>What the rows of one lane's list of positions share: which build of the rows is on screen, and their controls.</summary>
    private sealed class PositionRows
    {
        /// <summary>Counts how often the rows were built again; a row that was built before the last time is not on screen.</summary>
        internal int Generation;

        /// <summary>
        /// The controls of the rows that are on screen that are switched off while an editor operation runs, so they can be
        /// forgotten when the rows are replaced.
        /// </summary>
        internal List<Control> Controls { get; } = [];
    }

    /// <summary>
    /// A position that an earlier change moved away or removed, found by an edit that was asked for before that change was
    /// applied. Nothing is changed; the list is built again so it shows what is there.
    /// </summary>
    private sealed class PositionGoneException() : UserMessageException(Text.Get("PositionGone"));

    /// <summary>
    /// What is inside a lane's "Custom beat positions". A lane that plays an even pattern is converted to positions with one
    /// command; one that already has positions lists them, each as a row that shows where it is and how strong, and opens to its
    /// fields. The rows are built when they are opened, so a lane with many positions costs nothing until one is looked at.
    /// </summary>
    private UIElement BuildPositions(int laneIndex, int revision)
    {
        var id = $"Lane{laneIndex}";
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(Note("PositionsHelp"));
        if (_document.Lanes[laneIndex].Events.Count == 0)
        {
            var convert = ActionButton("PositionsConvert", id + "ConvertEvents",
                () => RunEditorOperationAsync(revision, () => ConvertToPositionsAsync(laneIndex)));
            EditorControl(convert);
            panel.Children.Add(Note("PositionsConvertHelp"));
            panel.Children.Add(CommandRow(convert));
            return panel;
        }

        var list = new StackPanel { Spacing = 8 };
        // The positions whose rows are open, by position: an index changes when a position is moved past another.
        var open = new HashSet<Rational>();
        var rows = new PositionRows();
        void Fill()
        {
            // The page was replaced since this list was built (an undo, a project that was opened): the list is not on screen
            // any more and must not touch what is.
            if (revision != _editorContentRevision) return;
            // Rows that are gone no longer answer for their ids, nor are their controls switched on and off with the others.
            foreach (var key in _studioFocusTargets.Keys.Where(key => key.StartsWith(id + "Event", StringComparison.Ordinal)).ToList())
                _studioFocusTargets.Remove(key);
            foreach (var control in rows.Controls) ForgetEditorControl(control);
            rows.Controls.Clear();
            rows.Generation++;
            list.Children.Clear();
            if (laneIndex >= _document.Lanes.Count) return;
            var events = _document.Lanes[laneIndex].Events;
            for (var index = 0; index < events.Count; index++)
                list.Children.Add(BuildPositionRow(laneIndex, index, revision, open, rows, Fill));
        }
        Fill();

        async Task AddPositionAsync()
        {
            var added = Rational.Zero;
            await CommitStudioAsync(revision, null, doc =>
            {
                var target = doc.Lanes[laneIndex];
                if (target.Events.Count >= PositionEdits.MaxPositions) throw new UserMessageException(Text.Get("PositionLimit"));
                added = PositionEdits.Midpoint(Rational.FromDouble(target.CycleQuarters), target.Events.Select(item => item.PositionQuarters));
                if (!PositionEdits.FitsProject(added)) throw new UserMessageException(Text.Get("PositionPrecise"));
                target.Events.Add(new RhythmEvent { PositionQuarters = added });
                PositionEdits.Sort(target.Events);
            });
            // The new position is open, with its box selected, so typing replaces the suggested place.
            open.Add(added);
            Fill();
            FocusPositionBox(laneIndex, _document.Lanes[laneIndex].Events.FindIndex(item => item.PositionQuarters == added));
        }
        var add = ActionButton("PositionsAdd", id + "AddEvent", () => RunEditorOperationAsync(revision, AddPositionAsync), "\uE710");
        bool HasRoom()
        {
            var room = laneIndex < _document.Lanes.Count && _document.Lanes[laneIndex].Events.Count < PositionEdits.MaxPositions;
            // Why the command is off is said on it, and only while it is.
            ToolTipService.SetToolTip(add, room ? null : Text.Get("PositionLimit"));
            return room;
        }
        EditorControl(add, HasRoom);
        TrackStudioFocus(id + "AddEvent", add);
        panel.Children.Add(list);
        panel.Children.Add(CommandRow(add));
        return panel;
    }

    /// <summary>One position as an expander: the summary ("Position 2 · 1.5 · Medium") on its header, the fields inside.</summary>
    private UIElement BuildPositionRow(int laneIndex, int eventIndex, int revision, HashSet<Rational> open, PositionRows rows,
        Action refill)
    {
        var id = $"Lane{laneIndex}Event{eventIndex}";
        var item = _document.Lanes[laneIndex].Events[eventIndex];
        var built = rows.Generation;

        // A position is told apart by where it is, not by its place in the list. Edits wait in a queue, and one that was asked
        // for earlier can put a position past another or remove one, so by the time a later edit is applied that place in the
        // list can belong to another position. Moving this position is the only thing that changes where it is, and that edit
        // says so.
        var place = item.PositionQuarters;
        int IndexIn(PulseDocument document) =>
            laneIndex < document.Lanes.Count ? document.Lanes[laneIndex].Events.FindIndex(other => other.PositionQuarters == place) : -1;
        int Located(PulseDocument document)
        {
            var at = IndexIn(document);
            return at >= 0 ? at : throw new PositionGoneException();
        }
        // The project can move on before this page is replaced, so reads never assume the position is still there.
        RhythmEvent Now()
        {
            var at = IndexIn(_document);
            return at >= 0 ? _document.Lanes[laneIndex].Events[at] : item;
        }
        string Key(string field) => $"Lane{laneIndex}Event@{place}.{field}";
        // The list was built again while an edit of this row waited its turn, from a project that did not have the edit yet.
        bool Replaced() => built != rows.Generation;
        async Task Change(string field, Action<RhythmEvent> edit)
        {
            try { await CommitStudioAsync(revision, Key(field), doc => edit(doc.Lanes[laneIndex].Events[Located(doc)])); }
            catch (PositionGoneException)
            {
                refill();
                throw;
            }
            if (Replaced()) refill();
        }
        string Own(string text) => Text.Format("LaneField", laneIndex + 1, text);
        string Named(string captionKey) => Own($"{Text.Format("PositionName", eventIndex + 1)}, {Text.Get(captionKey)}");

        var expander = new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = open.Contains(item.PositionQuarters)
        };
        // Where it is, how strong, and anything that sets it apart, so a collapsed list can still be read.
        void ShowSummary()
        {
            var now = Now();
            var parts = new List<string>
            {
                Text.Format("PositionName", eventIndex + 1), PositionEdits.Format(now.PositionQuarters, Text.Culture),
                Text.Get("Level" + now.Accent)
            };
            if (now.Tie) parts.Add(Text.Get("PositionTied"));
            var summary = string.Join(" · ", parts);
            expander.Header = summary;
            AutomationProperties.SetName(expander, Own(summary));
        }
        AutomationProperties.SetAutomationId(expander, id);
        TrackStudioFocus(id, expander);
        ShowSummary();

        // The control of this row that has the focus, by the end of its id ("Position", "Accent"); empty for the row itself.
        string? FocusedPart()
        {
            if (FocusManager.GetFocusedElement(Root.XamlRoot) is not DependencyObject focused) return null;
            var name = AutomationProperties.GetAutomationId(focused);
            if (!name.StartsWith(id, StringComparison.Ordinal)) return null;
            return name.Length == id.Length || !char.IsAsciiDigit(name[id.Length]) ? name[id.Length..] : null;
        }

        UIElement BuildFields()
        {
            // Everything this row registers with the editor gate (its fields, its sliders, its command) is remembered, so that
            // it can be forgotten when the rows are replaced.
            var registered = _editorOperationControls.Count;
            // ---- where ----------------------------------------------------------------------------------------------------
            var position = EditorControl(TextInput("PositionField", id + "Position", PositionEdits.Format(item.PositionQuarters, Text.Culture)));
            position.MaxWidth = double.PositiveInfinity;
            position.Width = 150;
            position.HorizontalAlignment = HorizontalAlignment.Left;
            CommitText(position, () => PositionEdits.Format(Now().PositionQuarters, Text.Culture), async text =>
            {
                if (!PositionEdits.TryParse(text, Text.Culture, out var wanted)) throw new UserMessageException(Text.Get("PositionInvalid"));
                var before = place;
                try
                {
                    await CommitStudioAsync(revision, Key("Position"), doc =>
                    {
                        var lane = doc.Lanes[laneIndex];
                        var at = Located(doc);
                        before = lane.Events[at].PositionQuarters;
                        // "1.50" is the same place as "1.5": nothing changes, and the box shows it the way the rest of the page does.
                        if (before == wanted) throw new OperationCanceledException();
                        var cycle = Rational.FromDouble(lane.CycleQuarters);
                        var others = lane.Events.Where((_, index) => index != at).Select(other => other.PositionQuarters);
                        if (PositionEdits.Problem(wanted, cycle, others) is { } problem)
                            throw new UserMessageException(problem == "PositionRange"
                                ? Text.Format(problem, PositionEdits.Format(cycle, Text.Culture)) : Text.Get(problem));
                        lane.Events[at].PositionQuarters = wanted;
                        PositionEdits.Sort(lane.Events);
                    },
                    // The position is where it was typed from the moment the project holds it, whatever happens to the recovery file
                    // after that, and not before: edits that were asked for after this one (this position's strength) look for it
                    // where it is then, and a position that the project refused, or that was already there, has not moved.
                    onPublished: () => place = wanted);
                }
                catch (PositionGoneException)
                {
                    refill();
                    throw;
                }
                if (open.Remove(before)) open.Add(wanted);
                var now = _document.Lanes[laneIndex].Events.FindIndex(other => other.PositionQuarters == wanted);
                if (now == eventIndex && !Replaced())
                {
                    // The way the rest of the page writes it ("0.75" for "3/4"), unless something else has been typed in the box since:
                    // that is the person's to commit, not this answer's to overwrite.
                    if (position.Text == text) position.Text = PositionEdits.Format(wanted, Text.Culture);
                    ShowSummary();
                    return;
                }
                // It moved past a neighbour, so the list is put in order again. The focus follows it, wherever in the row it was:
                // a person who typed a position and pressed Tab is now on the strength.
                var focusedPart = FocusedPart();
                refill();
                if (focusedPart is null || now < 0) return;
                var moved = $"Lane{laneIndex}Event{now}";
                FocusStudioTarget(_studioFocusTargets.ContainsKey(moved + focusedPart) ? moved + focusedPart : moved + "Position");
            });
            var positionField = Field(position, "UnitQuarterNotes");
            AutomationProperties.SetName(position, $"{Named("PositionField")} ({Text.Get("UnitQuarterNotes")})");
            TrackStudioFocus(id + "Position", position);

            var strength = EditorControl(Dropdown("PositionStrength", id + "Accent", item.Accent.ToString(), StrengthChoices()));
            CommitChoice(strength, () => Now().Accent.ToString(), async value =>
            {
                try { await Change("Accent", target => target.Accent = Enum.Parse<Accent>(value)); }
                finally { ShowSummary(); }
            });
            var strengthField = Field(strength);
            AutomationProperties.SetName(strength, Named("PositionStrength"));
            TrackStudioFocus(id + "Accent", strength);

            // ---- how loud, how likely, tied ---------------------------------------------------------------------------------
            static string Percent(int value) => Text.Format("PercentValue", value);
            var gainField = SliderField("Volume", id + "Gain", Named("Volume"), LevelFormat.ToPercent(item.Gain), 0, 100,
                Percent, [Percent(100)], () => LevelFormat.ToPercent(Now().Gain),
                value => Change("Gain", target => target.Gain = (float)LevelFormat.FromPercent(value)));
            var chanceField = SliderField("PositionChance", id + "Probability", Named("PositionChance"), LevelFormat.ToPercent(item.Probability),
                0, 100, Percent, [Percent(100)], () => LevelFormat.ToPercent(Now().Probability),
                value => Change("Probability", target => target.Probability = LevelFormat.FromPercent(value)));
            var tie = EditorControl(Toggle("PositionTie", id + "Tie", item.Tie));
            AutomationProperties.SetName(tie, Named("PositionTie"));
            CommitSwitch(tie, () => Now().Tie, async on =>
            {
                try { await Change("Tie", target => target.Tie = on); }
                finally { ShowSummary(); }
            });
            TrackStudioFocus(id + "Tie", tie);

            // ---- remove -----------------------------------------------------------------------------------------------------
            async Task RemoveAsync()
            {
                // Named by where it is now: earlier changes may have given it another number than the one this row was built with.
                var number = IndexIn(_document) is var at and >= 0 ? at + 1 : eventIndex + 1;
                if (!await ConfirmAsync(Text.Format("ConfirmRemove", Text.Format("PositionName", number)), "Remove")) return;
                var removed = place;
                var last = _document.Lanes[laneIndex].Events.Count <= 1;
                try
                {
                    // The last position makes the lane one without positions, so its card changes shape and the page is built
                    // again; any other leaves the page as it is.
                    if (last) await CommitEditorAsync(doc => PositionEdits.Remove(doc.Lanes[laneIndex], Located(doc)));
                    else await CommitStudioAsync(revision, null, doc => PositionEdits.Remove(doc.Lanes[laneIndex], Located(doc)));
                }
                catch (PositionGoneException)
                {
                    refill();
                    throw;
                }
                if (last || _document.Lanes[laneIndex].Events.Count == 0)
                {
                    FocusAfterRebuild($"Lane{laneIndex}Positions");
                    RebuildPreservingScroll();
                    return;
                }
                open.Remove(removed);
                refill();
                // The next position takes the place of the one removed; after the last, the one before it.
                FocusPositionRow(laneIndex, Math.Min(eventIndex, _document.Lanes[laneIndex].Events.Count - 1));
            }
            var remove = ActionButton("Remove", id + "Remove", () => RunEditorOperationAsync(revision, RemoveAsync), "\uE74D",
                ButtonKind.Tertiary);
            EditorControl(remove);
            AutomationProperties.SetName(remove, Named("Remove"));
            TrackStudioFocus(id + "Remove", remove);
            rows.Controls.AddRange(_editorOperationControls.Skip(registered).Select(entry => entry.Control));

            return new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    FieldRow(positionField, strengthField), FieldRow(gainField, chanceField),
                    SettingRow("PositionTie", null, tie), CommandRow(remove)
                }
            };
        }

        var fieldsBuilt = false;
        void EnsureFields()
        {
            if (fieldsBuilt) return;
            fieldsBuilt = true;
            expander.Content = BuildFields();
        }
        expander.Expanding += (_, _) =>
        {
            EnsureFields();
            open.Add(Now().PositionQuarters);
        };
        expander.Collapsed += (_, _) => open.Remove(Now().PositionQuarters);
        if (expander.IsExpanded) EnsureFields();
        return expander;
    }

    private async Task ConvertToPositionsAsync(int laneIndex)
    {
        await CommitEditorAsync(doc =>
        {
            var lane = doc.Lanes[laneIndex];
            lane.Events = PositionEdits.FromPattern(lane);
            lane.Pattern = [];
        });
        FocusAfterRebuild($"Lane{laneIndex}Positions");
        RebuildPreservingScroll();
    }

    /// <summary>Puts the focus on a position's box once it is on screen (a row that was just built has not been laid out yet).</summary>
    private void FocusPositionBox(int laneIndex, int eventIndex) =>
        FocusStudioTarget($"Lane{laneIndex}Event{eventIndex}Position");

    /// <summary>The focus goes to the row of a position: its box if the row is open, otherwise its header.</summary>
    private void FocusPositionRow(int laneIndex, int eventIndex)
    {
        var box = $"Lane{laneIndex}Event{eventIndex}Position";
        FocusStudioTarget(_studioFocusTargets.ContainsKey(box) ? box : $"Lane{laneIndex}Event{eventIndex}");
    }

    private void FocusStudioTarget(string id)
    {
        if (!_studioFocusTargets.TryGetValue(id, out var target)) return;
        void Focus()
        {
            if (_isClosed) return;
            target.Focus(FocusState.Programmatic);
            if (target is TextBox box) box.SelectAll();
        }
        // On a later turn, so the control has been laid out and a panel that is closing does not take the focus back.
        void Later() => DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, Focus);
        if (target.IsLoaded) Later();
        else target.Loaded += (_, _) => Later();
    }
}
