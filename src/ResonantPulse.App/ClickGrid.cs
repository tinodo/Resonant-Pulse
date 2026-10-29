using System.Globalization;
using ResonantPulse.Core;

namespace ResonantPulse.App;

/// <summary>
/// The clicks of one bar as the beat-strength strip shows them: every counted beat and, when beats are split
/// (Subdivision above 1), the clicks between them. Cell <c>beat * Split + part</c> is part 0, the beat itself, or one of the
/// split clicks. It is plain arithmetic over the settings, so the strip, its names, its numbers and the lit cell while
/// playing cannot disagree with each other or with Core's schedule.
/// </summary>
internal sealed class ClickGrid
{
    /// <summary>
    /// A bar with more clicks than this is shown as its beats alone: that many bars would be neither readable nor cheap to
    /// build. The split clicks still play, at the strengths the project holds.
    /// </summary>
    internal const int MaximumCells = 128;

    private readonly Rational _unit;
    private readonly Rational _writtenBeat;
    private readonly double _unitQuarters;
    private readonly double _swing;
    private readonly IReadOnlySet<int> _groupBeats;

    private ClickGrid(PulseSettings settings)
    {
        Beats = MeterPresets.PulsesPerBar(settings);
        Split = settings.Subdivision > 1 && Beats * settings.Subdivision <= MaximumCells ? settings.Subdivision : 1;
        _unit = Rational.FromDouble(settings.BeatUnitQuarters);
        _unitQuarters = Math.Max(1d / 64, settings.BeatUnitQuarters);
        _writtenBeat = new Rational(4, settings.BeatDenominator);
        _swing = settings.Swing;
        _groupBeats = GroupBeats(settings, Beats, _unit);
    }

    internal static ClickGrid Of(PulseSettings settings) => new(settings);

    /// <summary>Counted beats in a bar: how many times the BPM unit goes into it.</summary>
    internal int Beats { get; }

    /// <summary>Cells per beat: the beat and the clicks that split it, or 1 when the splits are not shown.</summary>
    internal int Split { get; }

    internal int Count => Beats * Split;

    /// <summary>How many split clicks one bar has when they are shown; the length of the pattern that edits them.</summary>
    internal int SplitSlots => Beats * (Split - 1);

    internal bool IsSplit(int cell) => cell % Split != 0;

    internal int BeatOf(int cell) => cell / Split;

    /// <summary>0 for the beat itself, 1 and up for the clicks that split it.</summary>
    internal int PartOf(int cell) => cell % Split;

    /// <summary>Index into the split-click pattern; only for a cell that <see cref="IsSplit"/>.</summary>
    internal int SlotOf(int cell) => BeatOf(cell) * (Split - 1) + PartOf(cell) - 1;

    /// <summary>The strength of every cell as it will sound: the beats with group starts forced Strong, the splits as set.</summary>
    internal Accent[] Levels(PulseSettings settings)
    {
        var beats = AccentPatterns.ExpandEffectiveMain(settings, Beats);
        if (Split == 1) return beats;
        var splits = AccentPatterns.ExpandSplit(settings.SubdivisionAccents, SplitSlots);
        var levels = new Accent[Count];
        for (var cell = 0; cell < levels.Length; cell++)
            levels[cell] = IsSplit(cell) ? splits[SlotOf(cell)] : beats[BeatOf(cell)];
        return levels;
    }

    /// <summary>
    /// Cells with a little space before them: where an additive group starts and, when beats are split, where each beat starts,
    /// so the clicks that split a beat read as belonging to it. Never the first cell: nothing precedes it.
    /// </summary>
    internal IReadOnlySet<int> SpaceBefore()
    {
        var cells = new HashSet<int>();
        foreach (var beat in _groupBeats)
            if (beat > 0) cells.Add(beat * Split);
        if (Split > 1)
            for (var beat = 1; beat < Beats; beat++) cells.Add(beat * Split);
        return cells;
    }

    /// <summary>
    /// The number under a cell: the written beat it falls on (6/8 felt in two reads 1 2 3 4 5 6, as the dial and the status
    /// line count), or a dot for a split click between two written beats.
    /// </summary>
    internal string Label(int cell)
    {
        var position = _unit * new Rational(cell, Split);
        var written = position / _writtenBeat;
        if (written.Denominator.IsOne) return ((int)written.Numerator + 1).ToString(CultureInfo.CurrentCulture);
        return IsSplit(cell) ? "·" : (BeatOf(cell) + 1).ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>The cell sounding at a position in the bar, in quarter notes, following Core's swing placement of split clicks.</summary>
    internal int CellAt(double withinBarQuarters)
    {
        var beatsElapsed = withinBarQuarters / _unitQuarters + 1e-6;
        var beat = (int)Math.Floor(beatsElapsed);
        if (Split == 1) return beat;
        var fraction = beatsElapsed - beat;
        var part = 0;
        for (var sub = 1; sub < Split; sub++)
        {
            var start = _swing == 0.5 || sub % 2 == 0
                ? (double)sub / Split
                : (double)(sub - 1) / Split + 2d / Split * _swing;
            if (fraction >= start) part = sub;
        }
        return beat * Split + part;
    }

    private static HashSet<int> GroupBeats(PulseSettings settings, int beats, Rational unit)
    {
        var starts = new HashSet<int>();
        var boundary = 0;
        foreach (var group in settings.Groups)
        {
            var position = new Rational(4 * boundary, settings.BeatDenominator) / unit;
            if (position.Denominator.IsOne && position.Numerator >= 0 && position.Numerator < beats)
                starts.Add((int)position.Numerator);
            boundary += group;
        }
        return starts;
    }
}
