using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private UIElement BuildExactEvents(int laneIndex)
    {
        var lane = _document.Lanes[laneIndex];
        var panel = Column(Copy("ExactEventHelp"));
        if (lane.Events.Count == 0)
        {
            panel.Children.Add(EditorActionButton("ConvertEvents", $"Lane{laneIndex}ConvertEvents", async () =>
            {
                await CommitEditorAsync(doc =>
                {
                    var edited = doc.Lanes[laneIndex];
                    var accents = AccentPatterns.ExpandLane(edited.Pattern, edited.Pulses);
                    var cycle = Rational.FromDouble(edited.CycleQuarters);
                    edited.Events = Enumerable.Range(0, edited.Pulses).Select(index => new RhythmEvent
                    {
                        PositionQuarters = cycle * new Rational(index, edited.Pulses),
                        Accent = accents[index]
                    }).ToList();
                    edited.Pattern = [];
                });
                RebuildPreservingScroll();
            }));
            return panel;
        }
        for (var index = 0; index < lane.Events.Count; index++)
            panel.Children.Add(BuildEventEditor(laneIndex, index));
        panel.Children.Add(EditorActionButton("AddEvent", $"Lane{laneIndex}AddEvent", async () =>
        {
            await CommitEditorAsync(doc =>
            {
                var edited = doc.Lanes[laneIndex];
                if (edited.Events.Count >= 256) throw new ArgumentException(Text.Get("EventLimit"));
                var cycle = Rational.FromDouble(edited.CycleQuarters);
                var sorted = edited.Events.Select(item => item.PositionQuarters).Order().ToArray();
                var bestStart = Rational.Zero;
                var bestLength = sorted[0];
                for (var index = 0; index < sorted.Length; index++)
                {
                    var length = (index + 1 < sorted.Length ? sorted[index + 1] : cycle) - sorted[index];
                    if (length > bestLength) { bestStart = sorted[index]; bestLength = length; }
                }
                var point = bestStart + bestLength / 2;
                edited.Events.Add(new RhythmEvent { PositionQuarters = point });
                edited.Events.Sort((left, right) => left.PositionQuarters.CompareTo(right.PositionQuarters));
            });
            RebuildPreservingScroll();
        }));
        return panel;
    }

    private UIElement BuildEventEditor(int laneIndex, int eventIndex)
    {
        var item = _document.Lanes[laneIndex].Events[eventIndex];
        var id = $"Lane{laneIndex}Event{eventIndex}";
        var position = Input("ExactPosition", id + "Position", item.PositionQuarters.ToString());
        var accent = Select("Accent", id + "Accent", item.Accent.ToString(), "Silent", "Soft", "Normal", "Strong");
        var gain = Number("EventGain", id + "Gain", item.Gain, 0, 1, 0.05);
        var probability = Number("Probability", id + "Probability", item.Probability, 0, 1, 0.05);
        var tie = Check("Tie", id + "Tie", item.Tie);
        var row = Column(Flow(position, accent, gain, probability), tie,
            CommandFlow(EditorActionButton("Apply", id + "Apply", async () =>
            {
                await CommitEditorAsync(doc =>
                {
                    var edited = doc.Lanes[laneIndex].Events[eventIndex];
                    // Fractions remain exact; invariant syntax is explicitly
                    // labelled, never rounded to a nearest grid position.
                    edited.PositionQuarters = Rational.Parse(position.Text.Trim());
                    edited.Accent = Enum.Parse<Accent>(Selected(accent));
                    edited.Gain = (float)Value(gain);
                    edited.Probability = Value(probability);
                    edited.Tie = tie.IsChecked == true;
                });
                RebuildPreservingScroll();
            }), EditorActionButton("Remove", id + "Remove", async () =>
            {
                if (!await ConfirmAsync(Text.Format("ConfirmRemove", $"{Text.Get("Step")} {eventIndex + 1}"), "Remove")) return;
                await CommitEditorAsync(doc =>
                {
                    var edited = doc.Lanes[laneIndex];
                    edited.Events.RemoveAt(eventIndex);
                    // Preserve silence when removing the final exact event;
                    // an empty Events array means even-pulse mode in Core.
                    if (edited.Events.Count == 0)
                    {
                        edited.Pulses = 1;
                        edited.Pattern = [Accent.Silent];
                    }
                });
                RebuildPreservingScroll();
            })));
        var expander = new Expander
        {
            Header = $"{eventIndex + 1} · {item.PositionQuarters} · {Text.Get(item.Accent.ToString())}",
            Content = row, HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Identify(expander, id, $"{Text.Get("Step")} {eventIndex + 1}");
        return expander;
    }
}
