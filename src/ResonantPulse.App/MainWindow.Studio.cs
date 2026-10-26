using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private UIElement BuildStudio()
    {
        _editorOperationControls.Clear();
        var panel = Column(Heading(_document.Name), Copy("EditBoundary"),
            CommandFlow(EditorActionButton("Undo", "StudioUndo", () => UndoEditorAsync(false)),
                EditorActionButton("Redo", "StudioRedo", () => UndoEditorAsync(true)),
                EditorActionButton("AddLane", "AddLane", () => AddLaneAsync(4, 4)),
                EditorActionButton("Polyrhythm", "AddPolyrhythm", () => AddLaneAsync(3, 4)),
                EditorActionButton("Polymeter", "AddPolymeter", () => AddLaneAsync(3, 3))));
        for (var index = 0; index < _document.Lanes.Count; index++)
            panel.Children.Add(BuildLaneEditor(index));
        panel.Children.Add(BuildGenerator());
        var seed = NumberInt("Seed", "DocumentSeed", _document.Seed, int.MinValue, int.MaxValue);
        panel.Children.Add(seed);
        panel.Children.Add(EditorActionButton("Apply", "ApplySeed", async () =>
        {
            await CommitEditorAsync(doc => doc.Seed = Integer(seed));
            BuildPage();
        }));
        panel.Children.Add(Heading(Text.Get("Program")));
        for (var index = 0; index < _document.Program.Count; index++)
            panel.Children.Add(BuildStepEditor(index));
        panel.Children.Add(EditorActionButton("AddStep", "AddProgramStep", async () =>
        {
            await CommitEditorAsync(doc => doc.Program.Add(new ProgramStep
            {
                Name = $"{Text.Get("Step")} {doc.Program.Count + 1}",
                StartTempo = doc.Settings.Tempo,
                EndTempo = doc.Settings.Tempo,
                BeatsPerBar = doc.Settings.BeatsPerBar,
                BeatDenominator = doc.Settings.BeatDenominator
            }));
            BuildPage();
        }));
        return Scroll(panel);
    }

    private async Task AddLaneAsync(int pulses, double quarters)
    {
        await CommitEditorAsync(doc =>
        {
            if (doc.Lanes.Count >= 8) throw new ArgumentException(Text.Get("LaneLimit"));
            doc.Lanes.Add(new RhythmLane
            {
                Name = $"{Text.Get("Lane")} {doc.Lanes.Count + 1}",
                Pulses = pulses, CycleQuarters = quarters,
                Pattern = Enumerable.Repeat(Accent.Normal, pulses).ToArray()
            });
        });
        BuildPage();
    }

    private UIElement BuildLaneEditor(int index)
    {
        var revision = _editorContentRevision;
        var lane = _document.Lanes[index];
        var id = $"Lane{index}";
        var name = Input("Name", id + "Name", lane.Name);
        var pulses = NumberInt("Pulses", id + "Pulses", lane.Pulses, 1, 256);
        pulses.IsEnabled = lane.Events.Count == 0;
        var cycle = Number("Cycle", id + "Cycle", lane.CycleQuarters, 1d / 1024, 4096, 0.125);
        var offset = Number("Offset", id + "Offset", lane.OffsetQuarters, -4096, 4096, 0.125);
        var volume = Number("Volume", id + "Volume", lane.Volume, 0, 1, 0.05);
        var pan = Number("Pan", id + "Pan", lane.Pan, -1, 1, 0.1);
        var probability = Number("Probability", id + "Probability", lane.Probability, 0, 1, 0.05);
        var sound = Select("Sound", id + "Sound", lane.Sound, "Wood", "Glass", "Click", "Kick", "HiHat");
        var muted = Check("MuteLane", id + "Muted", lane.Muted);
        var solo = Check("Solo", id + "Solo", lane.Solo);
        var pattern = AccentFlow();
        for (var beat = 0; beat < (lane.Events.Count == 0 ? lane.Pulses : 0); beat++)
        {
            var step = beat;
            var accent = AccentPatterns.LaneAt(lane.Pattern, step);
            var button = new Button { Margin = new Thickness(0, 0, 8, 8) };
            void Describe(Accent value)
            {
                var label = $"{lane.Name} · {Text.Format("BeatAccent", step + 1, Text.Get(value.ToString()))}";
                button.Content = new TextBlock { Text = $"{step + 1} · {Text.Get(value.ToString())}", TextWrapping = TextWrapping.Wrap };
                Identify(button, $"{id}Step{step}", label);
            }
            Describe(accent);
            EditorControl(button);
            button.Click += async (_, _) => await GuardAsync(() => RunEditorOperationAsync(revision, async () =>
            {
                await CommitEditorAsync(doc =>
                {
                    var revisedLane = doc.Lanes[index];
                    var accents = AccentPatterns.ExpandLane(revisedLane.Pattern,
                        Math.Max(revisedLane.Pattern.Length, revisedLane.Pulses));
                    accents[step] = (Accent)(((int)accents[step] + 1) % 4);
                    revisedLane.Pattern = accents;
                });
                BuildPage();
            }));
            pattern.Children.Add(button);
        }
        var panel = Column(Flow(name, pulses, cycle, offset, volume, pan, probability, sound), muted, solo,
            EditorActionButton("Apply", id + "Apply", async () =>
            {
                await CommitEditorAsync(doc =>
                {
                    var edited = doc.Lanes[index];
                    edited.Name = name.Text;
                    edited.Pulses = Integer(pulses);
                    edited.CycleQuarters = Value(cycle);
                    edited.OffsetQuarters = Value(offset);
                    edited.Volume = (float)Value(volume);
                    edited.Pan = (float)Value(pan);
                    edited.Probability = Value(probability);
                    edited.Sound = Selected(sound);
                    edited.Muted = muted.IsChecked == true;
                    edited.Solo = solo.IsChecked == true;
                    // Core repeats nonempty patterns and supplies a Strong
                    // downbeat for an empty lane pattern. Preserve both here.
                });
                BuildPage();
            }), pattern, BuildExactEvents(index),
            CommandFlow(
                EditorActionButton("Rotate", id + "Rotate", async () =>
                {
                    await CommitEditorAsync(doc =>
                    {
                        var edited = doc.Lanes[index];
                        if (edited.Events.Count == 0)
                        {
                            var accents = AccentPatterns.ExpandLane(edited.Pattern, edited.Pulses);
                            edited.Pattern = accents.Skip(1).Append(accents[0]).ToArray();
                        }
                        else
                        {
                            var cycle = Rational.FromDouble(edited.CycleQuarters);
                            foreach (var item in edited.Events)
                            {
                                var shifted = item.PositionQuarters + cycle / edited.Pulses;
                                item.PositionQuarters = shifted - new Rational((shifted / cycle).Floor(), 1) * cycle;
                            }
                        }
                    });
                    BuildPage();
                }),
                EditorActionButton("Invert", id + "Invert", async () =>
                {
                    await CommitEditorAsync(doc =>
                    {
                        var edited = doc.Lanes[index];
                        if (edited.Events.Count == 0)
                            edited.Pattern = AccentPatterns.ExpandLane(edited.Pattern, edited.Pulses)
                                .Select(accent => accent == Accent.Silent ? Accent.Normal : Accent.Silent).ToArray();
                        else foreach (var item in edited.Events)
                            item.Accent = item.Accent == Accent.Silent ? Accent.Normal : Accent.Silent;
                    });
                    BuildPage();
                }),
                EditorActionButton("Duplicate", id + "Duplicate", async () =>
                {
                    await CommitEditorAsync(doc =>
                    {
                        if (doc.Lanes.Count >= 8) throw new ArgumentException(Text.Get("LaneLimit"));
                        // Clone the document once rather than sharing pattern arrays.
                        doc.Lanes.Add(LocalWorkspace.Clone(_document).Lanes[index]);
                    });
                    BuildPage();
                }),
                EditorActionButton("Remove", id + "Remove", async () =>
                {
                    if (!await ConfirmAsync(Text.Format("ConfirmRemove", lane.Name), "Remove")) return;
                    await CommitEditorAsync(doc => doc.Lanes.RemoveAt(index));
                    BuildPage();
                })));
        var expander = new Expander { Header = lane.Name, Content = panel, HorizontalAlignment = HorizontalAlignment.Stretch, IsExpanded = true };
        Identify(expander, id + "Editor", lane.Name);
        return expander;
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
                BuildPage();
            }),
            EditorActionButton("MoveUp", id + "MoveUp", async () =>
            {
                if (index == 0) return;
                await CommitEditorAsync(doc => (doc.Program[index - 1], doc.Program[index]) = (doc.Program[index], doc.Program[index - 1]));
                BuildPage();
            }),
            EditorActionButton("MoveDown", id + "MoveDown", async () =>
            {
                if (index >= _document.Program.Count - 1) return;
                await CommitEditorAsync(doc => (doc.Program[index + 1], doc.Program[index]) = (doc.Program[index], doc.Program[index + 1]));
                BuildPage();
            }),
            EditorActionButton("Remove", id + "Remove", async () =>
            {
                if (!await ConfirmAsync(Text.Format("ConfirmRemove", step.Name), "Remove")) return;
                await CommitEditorAsync(doc => doc.Program.RemoveAt(index));
                BuildPage();
            })));
        var expander = new Expander { Header = $"{index + 1}. {step.Name}", Content = content, HorizontalAlignment = HorizontalAlignment.Stretch };
        Identify(expander, id + "Editor", step.Name);
        return expander;
    }
}
