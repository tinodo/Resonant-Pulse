using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

internal enum HelpKind
{
    /// <summary>The title of a section.</summary>
    Heading,
    /// <summary>The title of one entry of the musical reference.</summary>
    Subheading,
    /// <summary>One numbered step of the quick start.</summary>
    Step,
    Paragraph,
    /// <summary>"Example: ..." under an entry of the musical reference.</summary>
    Example,
    /// <summary>The list of keyboard shortcuts, which the window draws as keys (it has no text of its own).</summary>
    Shortcuts
}

internal sealed record HelpEntry(HelpKind Kind, string Id, string Text);

/// <summary>
/// What the Help panel on Play says, in the order it is read: a quick start that takes a beginner from the first launch to a steady
/// click at their own tempo and time signature, Tap to start, what plays at once and what waits for Stop and Start, the keyboard
/// shortcuts and where they work, and a short musical reference with one example for each idea. The panel is read-only.
/// The sentences take the name of every control from the string that the control shows, so Help and window cannot drift apart
/// in any language; HelpFactsTests checks every example and every claim against the engine.
/// packaging/Verify-FirstUse.ps1 finds each entry on the real window by its id.
/// </summary>
internal static class HelpContent
{
    internal static IReadOnlyList<HelpEntry> Entries()
    {
        static string Get(string key) => Text.Get(key);
        static string Format(string key, params string[] labels) => Text.Format(key, labels.Cast<object>().ToArray());

        var start = Get("Start");
        var stop = Get("Stop");
        var space = Get("KeySpace");
        var ctrl = Get("KeyCtrl");
        var custom = Get("MeterCustom");
        var studio = Get("Studio");
        var strong = Get("LevelStrong");
        var medium = Get("LevelNormal");
        var soft = Get("LevelSoft");
        var silent = Get("LevelSilent");
        return
        [
            new(HelpKind.Heading, "HelpQuickStartHeading", Get("HelpQuickStart")),
            new(HelpKind.Step, "HelpStep1", Format("HelpStepOutput", Get("AudioOptions"), start)),
            new(HelpKind.Step, "HelpStep2", Format("HelpStepStart", start, space)),
            new(HelpKind.Step, "HelpStep3", Format("HelpStepTempo", ctrl, Get("TapShort"), Get("CloseOptions"))),
            new(HelpKind.Step, "HelpStep4", Format("HelpStepMeter", custom)),
            new(HelpKind.Step, "HelpStep5", Format("HelpStepStrength", Get("PulseStrengths"), strong, medium, soft, silent)),

            new(HelpKind.Heading, "HelpTapStartHeading", Get("TapStart")),
            new(HelpKind.Paragraph, "HelpTapStartText", Get("HelpTapStart")),
            new(HelpKind.Paragraph, "HelpTapStartLimits", Get("HelpTapStartLimits")),

            new(HelpKind.Heading, "HelpPlayingHeading", Get("HelpWhilePlaying")),
            new(HelpKind.Paragraph, "HelpPlaying", Format("HelpPlaying", Get("Pause"), start, stop, Get("Mute"))),
            new(HelpKind.Paragraph, "HelpLive", Format("HelpLive", stop, start, Get("PracticeTab"), studio)),
            new(HelpKind.Paragraph, "HelpProgram", Format("HelpProgram", studio)),

            new(HelpKind.Heading, "HelpShortcutsHeading", Get("HelpShortcuts")),
            new(HelpKind.Shortcuts, "HelpShortcuts", ""),
            new(HelpKind.Paragraph, "HelpShortcutScope", Format("HelpShortcutScope", space, ctrl, Get("Play"))),

            new(HelpKind.Heading, "HelpReferenceHeading", Get("HelpReference")),
            new(HelpKind.Subheading, "HelpBpmTitle", Get("HelpBpmTitle")),
            new(HelpKind.Paragraph, "HelpBpm", Format("HelpBpm", Get("Unit"))),
            new(HelpKind.Example, "HelpBpmExample", Get("HelpBpmExample")),
            new(HelpKind.Subheading, "HelpMeterTitle", Get("HelpMeterTitle")),
            new(HelpKind.Paragraph, "HelpMeter", Get("HelpMeter")),
            new(HelpKind.Example, "HelpMeterExample", Get("HelpMeterExample")),
            new(HelpKind.Subheading, "HelpFeelTitle", Get("HelpFeelTitle")),
            new(HelpKind.Paragraph, "HelpFeel", Format("HelpFeel", custom, Get("SectionFeel"))),
            new(HelpKind.Example, "HelpFeelExample", Get("HelpFeelExample")),
            new(HelpKind.Subheading, "HelpStrengthTitle", Get("PulseStrengths")),
            new(HelpKind.Paragraph, "HelpStrength", Format("HelpStrength", strong, medium, soft, silent)),
            new(HelpKind.Example, "HelpStrengthExample", Format("HelpStrengthExample", silent))
        ];
    }
}
