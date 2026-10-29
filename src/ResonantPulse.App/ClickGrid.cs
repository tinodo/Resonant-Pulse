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
    /// build. So is a bar whose unit does not divide it (an imported project; the unit notice offers the fix): Core cuts the
    /// clicks of its last, partial beat at the bar line, so cells for them would promise clicks that do not sound. The split
    /// clicks still play, at the strengths the project holds.
    /// </summary>
    internal const int MaximumCells = 128;

    private readonly Rational _unit;
    private readonly Rational _writtenBeat;
    private readonly bool _countsWrittenBeats;
    private readonly double _unitQuarters;
    private readonly double _swing;
    private readonly Rational _swingFraction;
    private readonly (int BeatsPerBar, int BeatDenominator, double BeatUnitQuarters, int Subdivision, double Swing) _source;
    private readonly IReadOnlySet<int> _groupBeats;

    private ClickGrid(PulseSettings settings)
    {
        Beats = MeterPresets.PulsesPerBar(settings);
        Split = settings.Subdivision > 1 && Beats * settings.Subdivision <= MaximumCells && !MeterPresets.UnitNeedsFix(settings)
            ? settings.Subdivision : 1;
        _unit = Rational.FromDouble(settings.BeatUnitQuarters);
        _unitQuarters = Math.Max(1d / 64, settings.BeatUnitQuarters);
        _writtenBeat = new Rational(4, settings.BeatDenominator);
        _countsWrittenBeats = (_unit / _writtenBeat).Denominator.IsOne;
        _swing = settings.Swing;
        _swingFraction = Rational.FromDouble(settings.Swing);
        _source = (settings.BeatsPerBar, settings.BeatDenominator, settings.BeatUnitQuarters, settings.Subdivision, settings.Swing);
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

    /// <summary>
    /// True when both grids cut a bar into the same cells, so a cell number means the same click in each. A tap names a cell of the
    /// strip it was made on; this tells whether that strip still matches the bar.
    /// </summary>
    internal bool SameCells(ClickGrid other) => Beats == other.Beats && Split == other.Split;

    /// <summary>
    /// True when audio running with these settings places its clicks where this grid's cells put them: the same bar, unit,
    /// subdivision and swing. A change of any of them reaches the audio a beat after it reaches the document, and until then the
    /// position the audio reports belongs to another set of cells, so nothing may be lit from it.
    /// </summary>
    internal bool IsPlayedBy(PulseSettings active) =>
        _source == (active.BeatsPerBar, active.BeatDenominator, active.BeatUnitQuarters, active.Subdivision, active.Swing);

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
    /// The number under a cell, one scheme for the whole bar. When the BPM unit is a whole number of written beats (a 6/8 felt in
    /// two counts three eighths a beat), every click is numbered by the written beat it falls on, as the dial and the status
    /// line count: 6/8 felt in two reads 1 2 3 4 5 6, half notes in 4/4 read 1 3, and a click between two written beats is a
    /// dot. A split click is placed where Core plays it, so one that swing pulls off a written beat is a dot too. Any other
    /// unit (an eighth note in 4/4) has beats that fall between written beats, so there the beats are numbered 1, 2, 3 and
    /// on, and the clicks that split them are dots. Either way no two beats share a number.
    /// </summary>
    internal string Label(int cell)
    {
        if (_countsWrittenBeats)
        {
            var written = PositionOf(cell) / _writtenBeat;
            if (written.Denominator.IsOne) return ((int)written.Numerator + 1).ToString(CultureInfo.CurrentCulture);
        }
        return IsSplit(cell) ? "·" : (BeatOf(cell) + 1).ToString(CultureInfo.CurrentCulture);
    }

    /// <summary>Where the click of a cell sounds, in quarter notes into the bar, placed as Core places it: split clicks follow the swing.</summary>
    private Rational PositionOf(int cell)
    {
        var beatStart = _unit * BeatOf(cell);
        var part = PartOf(cell);
        if (part == 0) return beatStart;
        return beatStart + (_swing == 0.5 || part % 2 == 0
            ? _unit * new Rational(part, Split)
            : _unit * (new Rational(part - 1, Split) + new Rational(2, Split) * _swingFraction));
    }

    /// <summary>
    /// The cell sounding at a position in the bar, in quarter notes, following Core's swing placement of split clicks. A count-in
    /// plays the beats alone, so during it the beat that sounded last stays lit and the silent clicks between beats are skipped.
    /// </summary>
    internal int CellAt(double withinBarQuarters, bool countIn = false)
    {
        var beatsElapsed = withinBarQuarters / _unitQuarters + 1e-6;
        var beat = (int)Math.Floor(beatsElapsed);
        if (Split == 1 || countIn) return beat * Split;
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
