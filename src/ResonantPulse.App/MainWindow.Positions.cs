using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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

    /// <summary>Counts the rows of lists of positions that were built, so that every row has an identity that never changes.</summary>
    private int _positionRowSerial;

    /// <summary>
    /// One row of a lane's list of positions. A row is told apart by where its click is (<see cref="Place"/>), not by its number in
    /// the list: edits wait in a queue, and one that was asked for earlier can put a position past another or remove one, so by the
    /// time a later edit is applied that number can belong to another position. Only moving the position changes its place, and that
    /// edit says so the moment the project holds it. The row stays on screen as the list changes around it, so what is in it stays
    /// too (a draft in its box, its focus, whether it is open); only its number changes.
    /// </summary>
    private sealed class PositionRow(Rational place, int serial)
    {
        /// <summary>Where the click is, in quarter notes.</summary>
        internal Rational Place { get; set; } = place;

        /// <summary>Its number in the list, from 0.</summary>
        internal int Number { get; set; }

        /// <summary>Never changes, however the position moves: what edits of this position are told apart by for Undo.</summary>
        internal int Serial { get; } = serial;

        internal Expander Element { get; set; } = null!;

        /// <summary>The controls of the row that are switched off while an editor operation runs, so they can be forgotten with it.</summary>
        internal List<Control> Controls { get; } = [];

        /// <summary>Gives the row's controls the ids and names that belong to its number, and registers them as focus targets.</summary>
        internal Action Relabel { get; set; } = static () => { };

        /// <summary>Takes the row's controls out of the focus targets (the ones that are still theirs).</summary>
        internal Action Unregister { get; set; } = static () => { };
    }

    /// <summary>A control of a row, with what its id ends with and what its accessible name says (which depend on the row's number).</summary>
    private sealed class PositionPart(Control control, string suffix, Func<string>? name)
    {
        internal Control Control { get; } = control;
        internal string Suffix { get; } = suffix;
        internal Func<string>? Name { get; } = name;
        internal string? Key { get; set; }
    }

    /// <summary>
    /// A position that an earlier change moved away or removed, found by an edit that was asked for before that change was
    /// applied. Nothing is changed; the list is made to show what is there.
    /// </summary>
    private sealed class PositionGoneException() : UserMessageException(Text.Get("PositionGone"));

    /// <summary>
    /// What is inside a lane's "Custom beat positions". A lane that plays an even pattern is converted to positions with one
    /// command; one that already has positions lists them, each as a row that shows where it is and how strong, and opens to its
    /// fields. The fields of a row are built when it is opened, so a lane with many positions costs nothing until one is looked at.
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
        // The page this list belongs to. A page that was built again (for any reason) has lists of its own; this one is then
        // gone from the screen and must not touch what the page that replaced it registers.
        var generation = _pageGeneration;
        // The rows, in the order they are on screen.
        var rows = new List<PositionRow>();
        // The place of a row that is built open: the one that was just added.
        Rational? opening = null;

        // The row that has the keyboard focus, if it is one of these.
        PositionRow? FocusedRow()
        {
            if (rows.Count == 0 || Root.XamlRoot is null) return null;
            if (FocusManager.GetFocusedElement(Root.XamlRoot) is not DependencyObject focused) return null;
            for (DependencyObject? node = focused; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is Expander expander)
                    foreach (var candidate in rows)
                        if (ReferenceEquals(candidate.Element, expander)) return candidate;
            return null;
        }

        PositionRow BuildRow(Rational place, int number)
        {
            var row = new PositionRow(place, ++_positionRowSerial) { Number = number };
            BuildPositionRow(laneIndex, row, revision, opening == place, Reconcile);
            return row;
        }

        // A row that is gone no longer answers for its ids, nor is it switched on and off with the editor operations.
        void Forget(PositionRow row)
        {
            row.Unregister();
            foreach (var control in row.Controls) ForgetEditorControl(control);
            row.Controls.Clear();
        }

        // Makes the list show the project: a row for every position (the same row for a position that is where it was, or that
        // has moved), in the project's order, numbered from 1. Rows are not built again, so what is in them stays. The row that has
        // the focus is the one that does not move; the others move around it.
        void Reconcile()
        {
            // The page was replaced since this list was built (an undo, a project that was opened, another page and back): the
            // list is not on screen any more and must not touch what is.
            if (generation != _pageGeneration || revision != _editorContentRevision || laneIndex >= _document.Lanes.Count) return;
            var events = _document.Lanes[laneIndex].Events;
            var keep = FocusedRow();
            var wanted = new List<PositionRow>(events.Count);
            for (var number = 0; number < events.Count; number++)
            {
                var place = events[number].PositionQuarters;
                wanted.Add(rows.Find(candidate => candidate.Place == place) ?? BuildRow(place, number));
            }
            for (var number = 0; number < wanted.Count; number++) wanted[number].Number = number;
            foreach (var gone in rows.Where(candidate => !wanted.Contains(candidate)).ToList()) Forget(gone);
            rows.Clear();
            rows.AddRange(wanted);
            // Every row lets go of its ids before any takes one: two rows can swap numbers.
            foreach (var row in wanted) row.Unregister();
            foreach (var row in wanted) row.Relabel();
            PutInOrder(list.Children, wanted.Select(row => (UIElement)row.Element).ToList(),
                keep is not null && wanted.Contains(keep) ? keep.Element : null);
            opening = null;
        }
        Reconcile();

        async Task AddPositionAsync()
        {
            var added = Rational.Zero;
            await CommitPositionsAsync(revision, null, doc =>
            {
                var target = doc.Lanes[laneIndex];
                if (target.Events.Count >= PositionEdits.MaxPositions) throw new UserMessageException(Text.Get("PositionLimit"));
                added = PositionEdits.Midpoint(Rational.FromDouble(target.CycleQuarters), target.Events.Select(item => item.PositionQuarters));
                if (!PositionEdits.FitsProject(added)) throw new UserMessageException(Text.Get("PositionPrecise"));
                target.Events.Add(new RhythmEvent { PositionQuarters = added });
                PositionEdits.Sort(target.Events);
            }, () => opening = added, Reconcile);
            // The new position is open, with its box selected, so typing replaces the suggested place.
            Reconcile();
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

    /// <summary>
    /// <see cref="CommitStudioAsync"/> for a change to a list of positions. Once the project holds the change the list has to show
    /// it, even if the recovery file could not be written afterwards (that error is still reported): <paramref name="reconcile"/>
    /// then runs before the error goes on. A change that was refused or not applied leaves the list as it is.
    /// </summary>
    private async Task CommitPositionsAsync(int revision, string? coalesceKey, Action<PulseDocument> edit, Action? onPublished,
        Action reconcile)
    {
        var published = false;
        try
        {
            await CommitStudioAsync(revision, coalesceKey, edit, () =>
            {
                onPublished?.Invoke();
                published = true;
            });
        }
        catch (Exception) when (published)
        {
            reconcile();
            throw;
        }
    }

    /// <summary>
    /// Puts the elements of a list in the given order without taking out and putting back the one that has the focus (see
    /// <see cref="RowOrder"/>): elements that are not wanted go, new ones come in at the end, and the others move around the one
    /// that is kept.
    /// </summary>
    private static void PutInOrder(UIElementCollection children, IReadOnlyList<UIElement> wanted, UIElement? keep)
    {
        for (var index = children.Count - 1; index >= 0; index--)
            if (!wanted.Contains(children[index])) children.RemoveAt(index);
        foreach (var element in wanted)
            if (!children.Contains(element)) children.Add(element);
        foreach (var (from, to) in RowOrder.Moves(children.ToList(), wanted, keep)) children.Move((uint)from, (uint)to);
    }

    /// <summary>
    /// One position as an expander: the summary ("Position 2 · 1.5 · Medium") on its header, the fields inside, built when it is
    /// first opened. The row is built once. As positions come and go around it its number changes, and
    /// <see cref="PositionRow.Relabel"/> puts its ids and names right.
    /// </summary>
    private void BuildPositionRow(int laneIndex, PositionRow row, int revision, bool open, Action reconcile)
    {
        var item = _document.Lanes[laneIndex].Events.Find(other => other.PositionQuarters == row.Place)!;
        string Id() => $"Lane{laneIndex}Event{row.Number}";
        int IndexIn(PulseDocument document) =>
            laneIndex < document.Lanes.Count ? document.Lanes[laneIndex].Events.FindIndex(other => other.PositionQuarters == row.Place) : -1;
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
        // Edits of one position are one burst for Undo wherever it is: the key does not use its place, which a move changes.
        string Key(string field) => $"Lane{laneIndex}Event#{row.Serial}.{field}";
        async Task Change(string field, Action<RhythmEvent> edit)
        {
            try { await CommitStudioAsync(revision, Key(field), doc => edit(doc.Lanes[laneIndex].Events[Located(doc)])); }
            catch (PositionGoneException)
            {
                reconcile();
                throw;
            }
        }
        string Own(string text) => Text.Format("LaneField", laneIndex + 1, text);
        string Named(string captionKey) => Own($"{Text.Format("PositionName", row.Number + 1)}, {Text.Get(captionKey)}");

        var parts = new List<PositionPart>();
        void Part(Control control, string suffix, Func<string>? name) => parts.Add(new PositionPart(control, suffix, name));

        var expander = new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = open
        };
        row.Element = expander;
        // Where it is, how strong, and anything that sets it apart, so a collapsed list can still be read.
        void ShowSummary()
        {
            var now = Now();
            var summaryParts = new List<string>
            {
                Text.Format("PositionName", row.Number + 1), PositionEdits.Format(now.PositionQuarters, Text.Culture),
                Text.Get("Level" + now.Accent)
            };
            if (now.Tie) summaryParts.Add(Text.Get("PositionTied"));
            var summary = string.Join(" · ", summaryParts);
            expander.Header = summary;
            AutomationProperties.SetName(expander, Own(summary));
        }
        Part(expander, "", null);
        row.Relabel = () =>
        {
            foreach (var part in parts)
            {
                part.Key = Id() + part.Suffix;
                AutomationProperties.SetAutomationId(part.Control, part.Key);
                if (part.Name is not null) AutomationProperties.SetName(part.Control, part.Name());
                _studioFocusTargets[part.Key] = part.Control;
            }
            ShowSummary();
        };
        row.Unregister = () =>
        {
            foreach (var part in parts)
            {
                if (part.Key is not null && _studioFocusTargets.TryGetValue(part.Key, out var owner) && ReferenceEquals(owner, part.Control))
                    _studioFocusTargets.Remove(part.Key);
                part.Key = null;
            }
        };

        UIElement BuildFields()
        {
            // Everything this row registers with the editor gate (its fields, its sliders, its command) is remembered, so that
            // it can be forgotten when the row goes.
            var registered = _editorOperationControls.Count;
            var current = Now();
            // ---- where ----------------------------------------------------------------------------------------------------
            var position = EditorControl(TextInput("PositionField", Id() + "Position", PositionEdits.Format(current.PositionQuarters, Text.Culture)));
            position.MaxWidth = double.PositiveInfinity;
            position.Width = 150;
            position.HorizontalAlignment = HorizontalAlignment.Left;
            CommitText(position, () => PositionEdits.Format(Now().PositionQuarters, Text.Culture), async text =>
            {
                if (!PositionEdits.TryParse(text, Text.Culture, out var wanted)) throw new UserMessageException(Text.Get("PositionInvalid"));
                try
                {
                    await CommitPositionsAsync(revision, Key("Position"), doc =>
                    {
                        var lane = doc.Lanes[laneIndex];
                        var at = Located(doc);
                        // "1.50" is the same place as "1.5": nothing changes, and the box shows it the way the rest of the page does.
                        if (lane.Events[at].PositionQuarters == wanted) throw new OperationCanceledException();
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
                    onPublished: () => row.Place = wanted,
                    reconcile: reconcile);
                }
                catch (PositionGoneException)
                {
                    reconcile();
                    throw;
                }
                // The way the rest of the page writes it ("0.75" for "3/4"), unless something else has been typed in the box since:
                // that is the person's to commit, not this answer's to overwrite.
                if (position.Text == text) position.Text = PositionEdits.Format(wanted, Text.Culture);
                // If it moved past a neighbour the list is put in order again, around the row that has the focus.
                reconcile();
            });
            var positionField = Field(position, "UnitQuarterNotes");
            Part(position, "Position", () => $"{Named("PositionField")} ({Text.Get("UnitQuarterNotes")})");

            var strength = EditorControl(Dropdown("PositionStrength", Id() + "Accent", current.Accent.ToString(), StrengthChoices()));
            CommitChoice(strength, () => Now().Accent.ToString(), async value =>
            {
                try { await Change("Accent", target => target.Accent = Enum.Parse<Accent>(value)); }
                finally { ShowSummary(); }
            });
            var strengthField = Field(strength);
            Part(strength, "Accent", () => Named("PositionStrength"));

            // ---- how loud, how likely, tied ---------------------------------------------------------------------------------
            static string Percent(int value) => Text.Format("PercentValue", value);
            var gainField = SliderField("Volume", Id() + "Gain", Named("Volume"), LevelFormat.ToPercent(current.Gain), 0, 100,
                Percent, [Percent(100)], () => LevelFormat.ToPercent(Now().Gain),
                value => Change("Gain", target => target.Gain = (float)LevelFormat.FromPercent(value)),
                slider => Part(slider, "Gain", () => Named("Volume")));
            var chanceField = SliderField("PositionChance", Id() + "Probability", Named("PositionChance"), LevelFormat.ToPercent(current.Probability),
                0, 100, Percent, [Percent(100)], () => LevelFormat.ToPercent(Now().Probability),
                value => Change("Probability", target => target.Probability = LevelFormat.FromPercent(value)),
                slider => Part(slider, "Probability", () => Named("PositionChance")));
            var tie = EditorControl(Toggle("PositionTie", Id() + "Tie", current.Tie));
            Part(tie, "Tie", () => Named("PositionTie"));
            CommitSwitch(tie, () => Now().Tie, async on =>
            {
                try { await Change("Tie", target => target.Tie = on); }
                finally { ShowSummary(); }
            });

            // ---- remove -----------------------------------------------------------------------------------------------------
            async Task RemoveAsync()
            {
                // Named by where it is now: earlier changes may have given it another number than the one it was built with.
                var number = IndexIn(_document) is var at and >= 0 ? at : row.Number;
                if (!await ConfirmAsync(Text.Format("ConfirmRemove", Text.Format("PositionName", number + 1)), "Remove")) return;
                var last = _document.Lanes[laneIndex].Events.Count <= 1;
                try
                {
                    // The last position makes the lane one without positions, so its card changes shape and the page is built
                    // again; any other leaves the page as it is.
                    if (last) await CommitEditorAsync(doc => PositionEdits.Remove(doc.Lanes[laneIndex], Located(doc)));
                    else await CommitPositionsAsync(revision, null, doc => PositionEdits.Remove(doc.Lanes[laneIndex], Located(doc)), null, reconcile);
                }
                catch (PositionGoneException)
                {
                    reconcile();
                    throw;
                }
                if (last || _document.Lanes[laneIndex].Events.Count == 0)
                {
                    FocusAfterRebuild($"Lane{laneIndex}Positions");
                    RebuildPreservingScroll();
                    return;
                }
                reconcile();
                // The next position takes the place of the one removed; after the last, the one before it.
                FocusPositionRow(laneIndex, Math.Min(number, _document.Lanes[laneIndex].Events.Count - 1));
            }
            var remove = ActionButton("Remove", Id() + "Remove", () => RunEditorOperationAsync(revision, RemoveAsync), "\uE74D",
                ButtonKind.Tertiary);
            EditorControl(remove);
            Part(remove, "Remove", () => Named("Remove"));
            row.Controls.AddRange(_editorOperationControls.Skip(registered).Select(entry => entry.Control));

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
            row.Relabel();
        }
        expander.Expanding += (_, _) => EnsureFields();
        if (open) EnsureFields();
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
