using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    /// <summary>
    /// The practice program in the Studio: a heading that says what a program is, with <i>Add step</i>, then one card for each
    /// step. With no steps the heading gives way to a card that says what a program is and offers the same command.
    /// </summary>
    private IReadOnlyList<UIElement> BuildProgramSection(int revision)
    {
        var add = ActionButton("AddStep", "AddProgramStep", () => RunEditorOperationAsync(revision, AddStepAsync), "\uE710");
        EditorControl(add, () => _document.Program.Count < ProgramEdits.MaxSteps);
        if (_document.Program.Count >= ProgramEdits.MaxSteps) ToolTipService.SetToolTip(add, Text.Get("StepLimit"));
        TrackStudioFocus("AddProgramStep", add);

        if (_document.Program.Count == 0)
            return [Card("TrainProgramTitle", EmptyState("\uE8FD", "StepsEmptyTitle", "StepsEmptyBody", add), id: "ProgramCard")];

        var title = new TextBlock { Text = Text.Get("TrainProgramTitle"), Style = ResonantStyle("CardTitleStyle") };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level2);
        var heading = new StackPanel { Spacing = 2 };
        heading.Children.Add(title);
        heading.Children.Add(new TextBlock { Text = Text.Get("TrainProgramHelp"), Style = ResonantStyle("NoteStyle") });
        var header = HeaderRow(heading, add);
        if (header is FrameworkElement row) row.Margin = new Thickness(0, 8, 0, 0);

        var section = new List<UIElement> { header };
        for (var index = 0; index < _document.Program.Count; index++) section.Add(BuildStepCard(index, revision));
        return section;
    }

    /// <summary>
    /// One step as a card: its name with the commands that move or change it, then Tempo (steady, or from one tempo to
    /// another), Length (bars and repeats), Time signature and a switch for leaving some bars silent. Every field applies as
    /// it is changed and nothing is rebuilt, so the focus, the caret and the scroll position stay where they are. Moving,
    /// duplicating and removing change which cards are on the page, so the page is built again after them.
    /// </summary>
    private UIElement BuildStepCard(int index, int revision)
    {
        var id = $"Program{index}";
        var step = _document.Program[index];

        // The project can move on (a step removed) before this page is replaced, so reads never assume the step is still there.
        ProgramStep Now() => index < _document.Program.Count ? _document.Program[index] : step;
        Task Change(string field, Action<ProgramStep> edit) =>
            CommitStudioAsync(revision, $"{id}.{field}", doc =>
            {
                edit(doc.Program[index]);
                CheckProgramLimits(doc);
            });
        Task ChangeMeter(string field, Action<ProgramStep> edit) =>
            CommitStudioAsync(revision, $"{id}.{field}", doc =>
            {
                // With additive groups every step has the project's own time signature. The fields are off then; this is a safeguard.
                if (ProgramEdits.MeterIsFixed(doc.Settings)) throw new UserMessageException(Text.Get("StepMeterGrouped"));
                edit(doc.Program[index]);
            });
        string Named(string captionKey) => Text.Format("StepField", index + 1, Text.Get(captionKey));
        // The visible caption stays as it is; the name a screen reader hears says which step the field belongs to.
        void Qualify(FrameworkElement control, string captionKey, string? unitKey = null) =>
            AutomationProperties.SetName(control, unitKey is null ? Named(captionKey) : $"{Named(captionKey)} ({Text.Get(unitKey)})");
        // While the page sets a number box itself, to bring it in line with the project, the box does not submit what it was set to.
        var syncing = false;

        // ---- header: name, move, more -----------------------------------------------------------------------------------
        var name = EditorControl(TextInput("Name", id + "Name", step.Name));
        // A name is as wide as a name needs, and gives way in a narrow card (the field host); not as wide as the card.
        name.MaxWidth = double.PositiveInfinity;
        name.Width = 280;
        name.HorizontalAlignment = HorizontalAlignment.Left;
        name.MaxLength = LaneEdits.MaxNameLength;
        CommitText(name, () => Now().Name, async text =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                ShowNotice("StepNameRequired");
                throw new OperationCanceledException();
            }
            await Change("Name", item => item.Name = text);
        });
        AutomationProperties.SetName(name, Text.Format("StepNameCaption", index + 1));
        var nameField = Field(name);
        AutomationProperties.SetName(name, Named("Name"));
        TrackStudioFocus(id + "Name", name);

        var up = EditorControl(IconButton("\uE70E", "MoveUp", id + "MoveUp",
            () => RunEditorOperationAsync(revision, () => MoveStepAsync(index, -1))), () => index > 0);
        var down = EditorControl(IconButton("\uE70D", "MoveDown", id + "MoveDown",
            () => RunEditorOperationAsync(revision, () => MoveStepAsync(index, 1))), () => index < _document.Program.Count - 1);
        AutomationProperties.SetName(up, Named("MoveUp"));
        AutomationProperties.SetName(down, Named("MoveDown"));
        TrackStudioFocus(id + "MoveUp", up);
        TrackStudioFocus(id + "MoveDown", down);
        var more = MoreActions(id + "More", Named("LaneMore"), revision,
            ("Duplicate", id + "Duplicate", () => DuplicateStepAsync(index)),
            ("Remove", id + "Remove", () => RemoveStepAsync(index)));
        TrackStudioFocus(id + "More", more);
        var commands = CommandRow(up, down, more);
        commands.VerticalAlignment = VerticalAlignment.Bottom;
        var header = Wrapped(16, 8, [nameField, commands]);

        // ---- Tempo: steady, or from one tempo to another -----------------------------------------------------------------
        var ramp = EditorControl(Dropdown("Ramp", id + "Ramp", step.Ramp, ProgramEdits.Ramps.Select(item => new Choice(item, Text.Get(item))).ToArray()));
        // A steady step has one tempo; a ramp has two. Which of the boxes is on screen follows the choice above.
        var steadyTempo = EditorControl(NumberInput("TempoTab", id + "Tempo", step.StartTempo, 1, 1000, 1, 1, whole: false, trimmed: true));
        var startTempo = EditorControl(NumberInput("StartTempo", id + "StartTempo", step.StartTempo, 1, 1000, 1, 1, whole: false, trimmed: true));
        var endTempo = EditorControl(NumberInput("EndTempo", id + "EndTempo", step.EndTempo, 1, 1000, 1, 1, whole: false, trimmed: true));
        // The steady box and the start box show one setting, the start tempo, and only one of them is on screen. They share their
        // submissions, so a value typed in either is the newest value of that setting; when its answer comes back, applied or
        // refused, both boxes are brought in line with what the project holds, or with a newer value that is still on its way.
        var startSubmissions = new SubmissionTracker<double>();
        var endSubmissions = new SubmissionTracker<double>();
        var rampField = Field(ramp);
        var steadyField = Field(steadyTempo, "UnitBpm");
        var startField = Field(startTempo, "UnitBpm");
        var endField = Field(endTempo, "UnitBpm");
        Qualify(ramp, "Ramp");
        Qualify(steadyTempo, "TempoTab", "UnitBpm");
        Qualify(startTempo, "StartTempo", "UnitBpm");
        Qualify(endTempo, "EndTempo", "UnitBpm");
        var rampHelp = new TextBlock
        {
            Text = Text.Format("StepRampHelp", Text.Get("LinearTime"), Text.Get("LinearBeat")), Style = ResonantStyle("NoteStyle")
        };
        // The row holds the fields that apply: the dropdown, then one tempo (steady) or a start and an end tempo (ramp). The
        // dropdown itself is never taken out, so it keeps the focus when its choice changes the others.
        var tempoRow = FieldRow(rampField);
        void Present(FrameworkElement field, bool wanted)
        {
            var present = tempoRow.Children.Contains(field);
            if (wanted && !present) tempoRow.Children.Add(field);
            else if (!wanted && present) tempoRow.Children.Remove(field);
        }
        void ShowTempoFields()
        {
            var current = Now();
            var steady = ProgramEdits.IsSteady(current);
            // A box that has a value on its way shows that value (the project does not hold it yet, and will), not the one the
            // project holds now; overwriting it would leave the box different from the project once that value is applied. A value
            // that was refused is not on its way any more, so both boxes show what the project holds. The steady box and the start
            // box are the same tempo.
            var start = startSubmissions.OnItsWayOr(current.StartTempo);
            syncing = true;
            try
            {
                steadyTempo.Value = start;
                startTempo.Value = start;
                endTempo.Value = endSubmissions.OnItsWayOr(current.EndTempo);
            }
            finally { syncing = false; }
            Present(steadyField, steady);
            Present(startField, !steady);
            Present(endField, !steady);
            rampHelp.Visibility = steady ? Visibility.Collapsed : Visibility.Visible;
        }
        CommitNumber(steadyTempo, () => Now().StartTempo, value => Change("Tempo", item => ProgramEdits.SetStartTempo(item, value)),
            startSubmissions, () => syncing, ShowTempoFields);
        CommitNumber(startTempo, () => Now().StartTempo, value => Change("StartTempo", item => ProgramEdits.SetStartTempo(item, value)),
            startSubmissions, () => syncing, ShowTempoFields);
        CommitNumber(endTempo, () => Now().EndTempo, value => Change("EndTempo", item => ProgramEdits.SetEndTempo(item, value)),
            endSubmissions, () => syncing, ShowTempoFields);
        CommitChoice(ramp, () => Now().Ramp, async value =>
        {
            try { await Change("Ramp", item => ProgramEdits.SetRamp(item, value)); }
            // Shows what the project holds, whether or not the change was applied.
            finally { ShowTempoFields(); }
        });
        ShowTempoFields();

        // ---- Length ------------------------------------------------------------------------------------------------------
        var bars = EditorControl(NumberInput("Bars", id + "Bars", step.Bars, 1, ProgramEdits.MaxBars));
        CommitNumber(bars, () => Now().Bars, value =>
        {
            var count = Integer(value);
            return Change("Bars", item => item.Bars = count);
        });
        var repeats = EditorControl(NumberInput("Repeats", id + "Repeats", step.Repeats, 1, 1024));
        CommitNumber(repeats, () => Now().Repeats, value =>
        {
            var count = Integer(value);
            return Change("Repeats", item => item.Repeats = count);
        });
        var barsField = Field(bars);
        var repeatsField = Field(repeats);
        Qualify(bars, "Bars");
        Qualify(repeats, "Repeats");

        // ---- Time signature ----------------------------------------------------------------------------------------------
        // With additive groups every step has the project's own time signature, so the two fields stay off then, whatever else
        // switches them.
        var meterFixed = ProgramEdits.MeterIsFixed(_document.Settings);
        var beats = EditorControl(NumberInput("BeatsTop", id + "Beats", step.BeatsPerBar, 1, 64), () => !meterFixed);
        TrackStudioFocus(id + "Beats", beats);
        CommitNumber(beats, () => Now().BeatsPerBar, value =>
        {
            var count = Integer(value);
            return ChangeMeter("Beats", item => item.BeatsPerBar = count);
        });
        var denominator = EditorControl(
            Dropdown("BeatNote", id + "Denominator", step.BeatDenominator.ToString(CultureInfo.InvariantCulture), NoteChoices()), () => !meterFixed);
        CommitChoice(denominator, () => Now().BeatDenominator.ToString(CultureInfo.InvariantCulture),
            value => ChangeMeter("Denominator", item => item.BeatDenominator = int.Parse(value, CultureInfo.InvariantCulture)));
        var beatsField = Field(beats);
        var denominatorField = Field(denominator);
        Qualify(beats, "BeatsTop");
        Qualify(denominator, "BeatNote");

        // ---- Practice aids: leave some bars silent -----------------------------------------------------------------------
        var rest = EditorControl(Toggle("StepGapSwitch", id + "Rest", ProgramEdits.HasSilentBars(step)));
        AutomationProperties.SetName(rest, Named("StepGapSwitch"));
        var audible = EditorControl(NumberInput("StepBarsPlayed", id + "Audible", Math.Max(1, step.AudibleBars), 1, 100000));
        var silent = EditorControl(NumberInput("SilentBars", id + "Silent", Math.Max(1, step.SilentBars), 1, 100000));
        Qualify(audible, "StepBarsPlayed");
        Qualify(silent, "SilentBars");
        // "Play [2] bars, then [2] bars silent": the sentence is cut at its places, so a language that puts the numbers in
        // another order gets its boxes in that order.
        var sentence = new List<UIElement>();
        foreach (var part in SentenceTemplate.Split(Text.Get("StepGapSentence")))
            sentence.Add(part.Text is { } words
                ? new TextBlock { Text = words, Style = ResonantStyle("BodyStyle"), FontSize = 14, VerticalAlignment = VerticalAlignment.Center }
                : Bounded(part.Slot == 0 ? audible : silent));
        var gapRow = Wrapped(8, 8, sentence.ToArray());
        // The trackers of the two boxes are made below, once the commands they run are defined; the function after this reads them.
        SubmissionTracker<double> audibleCommit = null!, silentCommit = null!;
        void ShowGapFields()
        {
            var current = Now();
            var on = ProgramEdits.HasSilentBars(current);
            // A box that has a value on its way shows that value, not the one the project holds now (see ShowTempoFields).
            syncing = true;
            try
            {
                audible.Value = audibleCommit.OnItsWayOr(on ? current.AudibleBars : ProgramEdits.DefaultAudibleBars);
                silent.Value = silentCommit.OnItsWayOr(on ? current.SilentBars : ProgramEdits.DefaultSilentBars);
            }
            finally { syncing = false; }
            gapRow.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
        // The two numbers go together: both above zero, or both zero (the project refuses anything else). A change that arrives
        // after the switch was turned off is not for this step any more, so it is left out.
        Task ChangeGap(string field, Action<ProgramStep> edit) => Change(field, item =>
        {
            if (!ProgramEdits.HasSilentBars(item)) throw new OperationCanceledException();
            edit(item);
        });
        audibleCommit = CommitNumber(audible, () => Now().AudibleBars > 0 ? Now().AudibleBars : ProgramEdits.DefaultAudibleBars, value =>
        {
            var count = Integer(value);
            return ChangeGap("Audible", item => item.AudibleBars = count);
        }, silenced: () => syncing);
        silentCommit = CommitNumber(silent, () => Now().SilentBars > 0 ? Now().SilentBars : ProgramEdits.DefaultSilentBars, value =>
        {
            var count = Integer(value);
            return ChangeGap("Silent", item => item.SilentBars = count);
        }, silenced: () => syncing);
        CommitSwitch(rest, () => ProgramEdits.HasSilentBars(Now()), async on =>
        {
            try { await Change("Rest", item => ProgramEdits.SetSilentBars(item, on)); }
            finally { ShowGapFields(); }
        });
        ShowGapFields();

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(header);
        body.Children.Add(SectionHeading("TempoTab"));
        body.Children.Add(tempoRow);
        body.Children.Add(rampHelp);
        body.Children.Add(SectionHeading("StepLength"));
        body.Children.Add(FieldRow(barsField, repeatsField));
        body.Children.Add(SectionHeading("TimeSignature"));
        body.Children.Add(FieldRow(beatsField, denominatorField));
        if (meterFixed)
        {
            body.Children.Add(Note("StepMeterGrouped"));
            // The way out is on the card that explains why the fields are off: Play offers no time signature while there is a program.
            var clear = EditorControl(ActionButton("ClearGrouping", id + "ClearGrouping",
                () => RunEditorOperationAsync(revision, () => ClearGroupingAsync(index))));
            TrackStudioFocus(id + "ClearGrouping", clear);
            body.Children.Add(CommandRow(clear));
        }
        body.Children.Add(SectionHeading("SectionTraining"));
        body.Children.Add(SettingRow("StepGapSwitch", "StepGapHelp", rest));
        body.Children.Add(gapRow);
        var card = Card(null, body);
        Identify(card, id + "Card", $"{Text.Get("Step")} {index + 1}");
        return card;
    }

    /// <summary>The error for a program the project would refuse as too long, in words that say what to change.</summary>
    private static void CheckProgramLimits(PulseDocument document)
    {
        if (ProgramEdits.BrokenLimit(document.Program) is not { } broken) return;
        throw new UserMessageException(broken == "ProgramTooLong"
            ? Text.Format(broken, ProgramEdits.MaxBars.ToString("N0", Text.Culture), ProgramEdits.MaxSections.ToString("N0", Text.Culture))
            : Text.Get(broken));
    }

    /// <summary>
    /// A "More actions" button whose menu holds commands that change what is on the page (duplicate, remove). Each runs through
    /// the editor gate for the page it was built for.
    /// </summary>
    private Button MoreActions(string id, string accessibleName, int revision, params (string Key, string Id, Func<Task> Action)[] commands)
    {
        var more = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 16 }, MinWidth = 44, Padding = new Thickness(0) };
        Identify(more, id, accessibleName);
        ToolTipService.SetToolTip(more, Text.Get("LaneMore"));
        var menu = new MenuFlyout();
        foreach (var (key, commandId, action) in commands)
        {
            var item = new MenuFlyoutItem { Text = Text.Get(key) };
            AutomationProperties.SetAutomationId(item, commandId);
            item.Click += async (_, _) => await GuardAsync(() => RunEditorOperationAsync(revision, action));
            menu.Items.Add(item);
        }
        more.Flyout = menu;
        return more;
    }

    private async Task AddStepAsync()
    {
        await CommitEditorAsync(doc =>
        {
            doc.Program.Add(ProgramEdits.NewStep(doc.Settings, LaneEdits.NextName(doc.Program.Select(item => item.Name), Text.Get("Step"))));
            CheckProgramLimits(doc);
        });
        // The new step's name is selected, so typing replaces it.
        FocusAfterRebuild($"Program{_document.Program.Count - 1}Name");
        RebuildPreservingScroll();
    }

    private async Task MoveStepAsync(int index, int delta)
    {
        var moved = false;
        await CommitEditorAsync(doc => moved = ProgramEdits.Move(doc.Program, index, delta));
        if (!moved) return;
        var now = index + delta;
        // The step keeps the command being used, unless it has reached the end it was moving towards (that command is off then).
        var atEnd = delta < 0 ? now == 0 : now == _document.Program.Count - 1;
        var command = (delta < 0) == atEnd ? "MoveDown" : "MoveUp";
        FocusAfterRebuild($"Program{now}{command}");
        RebuildPreservingScroll();
    }

    private async Task DuplicateStepAsync(int index)
    {
        await CommitEditorAsync(doc =>
        {
            doc.Program.Insert(index + 1, ProgramEdits.Copy(doc.Program[index], Text.Get("LaneCopySuffix")));
            CheckProgramLimits(doc);
        });
        FocusAfterRebuild($"Program{index + 1}Name");
        RebuildPreservingScroll();
    }

    private async Task RemoveStepAsync(int index)
    {
        var name = index < _document.Program.Count ? _document.Program[index].Name : "";
        if (!await ConfirmAsync(Text.Format("ConfirmRemove", name), "Remove")) return;
        await CommitEditorAsync(doc => doc.Program.RemoveAt(index));
        // The next step takes the place of the one removed; after the last, the one before it; with none left, Add step.
        FocusAfterRebuild(_document.Program.Count == 0 ? "AddProgramStep" : $"Program{Math.Min(index, _document.Program.Count - 1)}Name");
        RebuildPreservingScroll();
    }

    /// <summary>
    /// Takes the grouping (2+2+3) off the project and keeps the program: with no grouping every step may have a time signature of
    /// its own, so the fields that were off are on again. Undo puts the grouping back. The focus goes to the time signature of the
    /// step whose command was used.
    /// </summary>
    private async Task ClearGroupingAsync(int index)
    {
        await CommitEditorAsync(doc => doc.Settings.Groups = []);
        FocusAfterRebuild($"Program{index}Beats", $"Program{index}Name");
        RebuildPreservingScroll();
    }
}
