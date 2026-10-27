using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Controls;
using ResonantPulse.App.Localization;
using ResonantPulse.App.Storage;
using ResonantPulse.Core;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    /// <summary>Ready-made rhythms as tiles: a name, one plain sentence and the meter and tempo. Choosing one asks first.</summary>
    private UIElement BuildPresets()
    {
        var tiles = new WrapPanel { HorizontalSpacing = 12, VerticalSpacing = 12 };
        foreach (var preset in BuiltInLibrary.Presets().Concat(BuiltInLibrary.PracticePrograms()))
            tiles.Children.Add(PresetTile(preset));
        return Card("Presets", tiles, "PresetsHelp", id: "PresetsCard");
    }

    private Button PresetTile(LibraryPreset preset)
    {
        var name = Text.Get("Preset_" + preset.Id);
        var description = Text.Get("PresetDesc_" + preset.Id);
        var button = new Button
        {
            Style = ResonantStyle("ChipButtonStyle"),
            Width = 260 * Math.Max(1, Math.Sqrt(TextSettings.TextScaleFactor)),
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(14, 10, 14, 10),
            Content = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = name, Style = ResonantStyle("BodyStyle"), FontSize = 14, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = description, Style = ResonantStyle("NoteStyle") },
                    new TextBlock { Text = PresetSummary.Meta(preset.Document, Text.Culture), Style = ResonantStyle("NoteStyle"), FontSize = 12 }
                }
            }
        };
        Identify(button, "Preset_" + preset.Id, name);
        AutomationProperties.SetHelpText(button, description);
        button.Click += async (_, _) => await GuardAsync(async () =>
        {
            if (_isLocked) { ShowNotice("Locked"); return; }
            if (!await ConfirmAsync(Text.Get("Replace"))) return;
            var document = LocalWorkspace.Clone(preset.Document);
            document.Name = name;
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
        });
        return button;
    }
}
