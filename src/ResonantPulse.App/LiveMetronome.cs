using System.Text;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

/// <summary>What the pinned metronome says beside its lights.</summary>
internal enum LiveNote
{
    None,
    CountIn,
    Step
}

/// <summary>
/// How a beat light is drawn: its size, the size of one that begins the bar or an additive group, and the space between lights.
/// A bar of many beats gets small lights that differ by their ring alone, so that the strip, which is pinned above the page, never
/// takes more than a few rows of it, whatever the meter (a bar has up to 64 beats).
/// </summary>
internal readonly record struct LightMetrics(double Size, double EmphasizedSize, double Spacing)
{
    /// <summary>From this many beats the lights are small: a bar of 12/8 is still drawn in full size.</summary>
    internal const int CompactFrom = 13;

    internal static LightMetrics For(int beats) => beats >= CompactFrom ? new(10, 10, 4) : new(18, 26, 8);

    /// <summary>
    /// The height of the lights, rows and the space between rows included, when they wrap in a strip that leaves <paramref name="width"/>
    /// for them: the lights are put in a row until the next does not fit, as the strip's layout does.
    /// </summary>
    internal double Height(int beats, IReadOnlySet<int> emphasized, double width)
    {
        double rowWidth = 0, rowHeight = 0, height = 0;
        for (var index = 0; index < beats; index++)
        {
            var size = emphasized.Contains(index) ? EmphasizedSize : Size;
            if (rowWidth > 0 && rowWidth + Spacing + size > width)
            {
                height += rowHeight + Spacing;
                rowWidth = 0;
                rowHeight = 0;
            }
            rowWidth += (rowWidth > 0 ? Spacing : 0) + size;
            rowHeight = Math.Max(rowHeight, size);
        }
        return height + rowHeight;
    }
}

/// <summary>
/// What the metronome pinned to the top of the Studio page shows: the beat that sounds now, the beats that are drawn larger,
/// and what a screen reader reads. Pure, so the rules can be tested without a window.
/// </summary>
internal static class LiveMetronome
{
    /// <summary>
    /// The beat to light, counted from 0, or -1 for none. Nothing is lit while nothing plays (stopped, or finished) and while the phase
    /// cues are hidden (a silent bar of a project that hides them in gaps, which is the default: the dial hides its marker then too).
    /// A project that shows them in gaps keeps lighting the beats through a silent bar, as the dial keeps its marker moving: the person
    /// asked to see the time pass while it is silent. A paused metronome keeps the beat it stopped on.
    /// </summary>
    internal static int LitBeat(bool plays, bool cuesHidden, int beatIndex, int beats) =>
        !plays || cuesHidden || beats < 1 ? -1 : Math.Clamp(beatIndex, 0, beats - 1);

    /// <summary>
    /// What the strip says beside the lights. Nothing while nothing plays, and nothing in a silent bar of a project that hides its phase
    /// cues in gaps: the tempo reads "Silent" there, and the step of the program and its name are a cue that Train hides in the same bar.
    /// Otherwise "Count-in" while the metronome counts in, and the step of a program while one plays.
    /// </summary>
    internal static LiveNote NoteFor(bool plays, bool cuesHidden, bool countIn, bool hasProgram) =>
        !plays || cuesHidden ? LiveNote.None : countIn ? LiveNote.CountIn : hasProgram ? LiveNote.Step : LiveNote.None;

    /// <summary>
    /// A name as one line of text: each run of white space (line breaks and other control characters included) becomes one space, and
    /// the ends are trimmed. A step's name may be up to 256 characters with any number of line breaks, and the strip is pinned above the
    /// page, so what it shows of a name must not be able to make it tall.
    /// </summary>
    internal static string OneLine(string text)
    {
        var line = new StringBuilder(text.Length);
        var space = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                space = line.Length > 0;
                continue;
            }
            if (space) line.Append(' ');
            space = false;
            line.Append(character);
        }
        return line.ToString();
    }

    /// <summary>The beats that begin the bar or an additive group (3+2+2): they are drawn larger, as the long ticks of the dial are.</summary>
    internal static IReadOnlySet<int> Emphasized(int beats, IReadOnlyList<int> groups)
    {
        var starts = new HashSet<int> { 0 };
        var at = 0;
        foreach (var group in groups)
        {
            starts.Add(at);
            at += group;
        }
        starts.RemoveWhere(index => index >= beats);
        return starts;
    }

    /// <summary>What a screen reader hears when it reads the metronome: the tempo, and the beat that is lit or how many beats a bar has.</summary>
    internal static string Describe(string tempo, int beats, int lit) =>
        lit >= 0 ? Text.Format("LiveBeatOf", tempo, lit + 1, beats) : Text.Format("LiveBeats", tempo, beats);
}
