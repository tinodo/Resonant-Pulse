using Microsoft.UI.Xaml;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private UIElement BuildPresets()
    {
        var actions = BuiltInLibrary.Presets().Concat(BuiltInLibrary.PracticePrograms()).Select(preset =>
            ActionButton("Preset_" + preset.Id, "Preset_" + preset.Id, async () =>
            {
                if (_isLocked) { ShowNotice("Locked"); return; }
                if (!await ConfirmAsync(Text.Get("Replace"))) return;
                var document = LocalWorkspace.Clone(preset.Document);
                document.Name = Text.Get("Preset_" + preset.Id);
                for (var index = 0; index < document.Lanes.Count; index++)
                {
                    var lane = document.Lanes[index];
                    lane.Name = preset.Id == "paradiddle"
                        ? Text.Get(index == 0 ? "RightHand" : "LeftHand")
                        : $"{Text.Get("Lane")} {index + 1} · {lane.Pulses}/{lane.CycleQuarters.ToString("0.###", Text.Culture)}";
                }
                foreach (var step in document.Program)
                    step.Name = $"{Text.Get("Step")} {document.Program.IndexOf(step) + 1} · {step.StartTempo.ToString("0.0", Text.Culture)} → {step.EndTempo.ToString("0.0", Text.Culture)}";
                await ReplaceDocumentAsync(document);
            })).Cast<UIElement>().ToArray();
        return Column(Heading(Text.Get("Presets")), CommandFlow(actions));
    }
}
