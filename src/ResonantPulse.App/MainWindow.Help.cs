using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

public sealed partial class MainWindow
{
    private UIElement BuildHelp()
    {
        TextBlock Paragraph(string key)
        {
            var paragraph = Copy(key);
            Identify(paragraph, key, paragraph.Text);
            return paragraph;
        }

        return Scroll(Column(
            Heading(Text.Get("HelpDestination")),
            Paragraph("HelpIntro"),
            Heading(Text.Get("QuickStart")),
            Paragraph("HelpOutput"),
            Paragraph("HelpStarting"),
            Paragraph("HelpTransport"),
            Paragraph("HelpEditing"),
            Paragraph("HelpPractice"),
            ActionButton("Play", "HelpOpenPlay", () => SelectView("Play")),
            Heading(Text.Get("MusicalReference")),
            Paragraph("HelpBpmUnit"),
            Paragraph("HelpWrittenBeats"),
            Paragraph("HelpSubdivision"),
            Paragraph("HelpAccents"),
            Paragraph("HelpTapping"),
            Copy("TempoGuideHint"),
            Paragraph("HelpKeyboardScope"),
            Copy("KeyboardHelp"),
            Copy("PresentationLimits")));
    }
}
