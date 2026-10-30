using ResonantPulse.App.Localization;

namespace ResonantPulse.App;

/// <summary>The speed in quarter notes a minute that a tempo makes, and the markings that fit it.</summary>
internal readonly record struct TempoMarkings(double QuarterNotesPerMinute, IReadOnlyList<string> Names);

/// <summary>
/// The approximate Italian tempo markings that the Tempo panel names beside the tempo typed in it (old PR #10, ported onto the panel).
/// It is a guide: nothing it works out reaches the project, the note value or the transport.
/// The bands overlap on purpose, so two markings can fit one speed, and they are approximate background, not a standard. They
/// follow the table of plain BPM at https://tunableapp.com/tempo/ (read on 30 October 2026), which gives a range for each term, says
/// that the exact speed varies by piece, period and performer, and names no note value. The slowest band is open downwards and the
/// fastest upwards, so every speed the project allows has a marking (that table ends Larghissimo at 24 and gives Prestissimo 200 to
/// 300; the bands meet at 25 so that a fractional speed never falls between two of them).
/// A marking is about a speed, so the bands are quarter notes a minute, whatever note value the tempo is counted in: the tempo
/// typed counts the project's note value (the Help says BPM counts one kind of note per minute), and the engine's tempo is that
/// number times the note's length in quarter notes (80 dotted quarters, 120 quarter notes a minute). The time signature picker counts
/// the written beat, eighth notes in 6/8 and half notes in 2/2 (MeterPresets.Apply), so the same number is another speed in each;
/// matching the bare number would call 6/8 at 120 a Moderato when it is 60 quarter notes a minute.
/// What is named is the tempo that Done would apply (the panel rounds it to a tenth), and the speed is rounded to the tenth that the
/// sentence prints, so the line is never one thing in numbers and another in names.
/// The names are Italian terms that musicians write the same way in every language. They are code and not entries of the translated
/// text, so the translation tests (every string in all five languages, none copied from English) apply to the sentences around them.
/// </summary>
internal static class TempoGuide
{
    // The tempo and the note value that the project accepts: the Tempo panel's box takes 1 to 1000 and DocumentValidator takes a note
    // value from 1/64 to 16 quarter notes.
    private const double MinimumTempo = 1;
    private const double MaximumTempo = 1000;
    private const double MinimumUnit = 1d / 64;
    private const double MaximumUnit = 16;

    /// <summary>The eleven terms, slow to fast. Each is in the same place in <see cref="Bands"/>.</summary>
    internal static IReadOnlyList<string> Names { get; } =
        ["Larghissimo", "Grave", "Largo", "Larghetto", "Adagio", "Andante", "Moderato", "Allegro", "Vivace", "Presto", "Prestissimo"];

    /// <summary>Quarter notes a minute, both ends included. Neighbours meet or overlap; the ends are open.</summary>
    private static readonly (double From, double To)[] Bands =
    [
        (double.NegativeInfinity, 25), (25, 45), (40, 60), (60, 66), (66, 76), (76, 108),
        (108, 120), (120, 156), (156, 176), (168, 200), (200, double.PositiveInfinity)
    ];

    /// <summary>
    /// The speed that a tempo counted in a note value makes, and its markings; null when the project would refuse the tempo or the
    /// note value (the box takes 1 to 1000 BPM, and an empty box is not a number).
    /// </summary>
    /// <param name="tempo">The tempo typed, in the note value below.</param>
    /// <param name="unitQuarters">The length of that note in quarter notes: 1 for a quarter note, 1.5 for a dotted quarter, 0.5 for an eighth.</param>
    internal static TempoMarkings? For(double tempo, double unitQuarters)
    {
        // The tempo as Done applies it, to a tenth.
        var applied = Math.Round(tempo, 1);
        if (!double.IsFinite(applied) || applied < MinimumTempo || applied > MaximumTempo) return null;
        if (!double.IsFinite(unitQuarters) || unitQuarters < MinimumUnit || unitQuarters > MaximumUnit) return null;
        var rate = Math.Round(applied * unitQuarters, 1);
        return new TempoMarkings(rate, MarkingsAt(rate));
    }

    /// <summary>The markings whose band holds a speed in quarter notes a minute: one, or two where bands overlap or meet.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The speed is not a number, or is negative.</exception>
    internal static IReadOnlyList<string> MarkingsAt(double quarterNotesPerMinute)
    {
        if (!double.IsFinite(quarterNotesPerMinute) || quarterNotesPerMinute < 0) throw new ArgumentOutOfRangeException(nameof(quarterNotesPerMinute));
        var names = new List<string>(2);
        for (var index = 0; index < Bands.Length; index++)
            if (quarterNotesPerMinute >= Bands[index].From && quarterNotesPerMinute <= Bands[index].To) names.Add(Names[index]);
        return names;
    }

    /// <summary>The line the Tempo panel shows: the markings and what they are counted in, or what to enter when there is nothing to name.</summary>
    internal static string Describe(double tempo, double unitQuarters) =>
        For(tempo, unitQuarters) is { } markings
            ? Text.Format("TempoGuideValue", TempoFormat.Format(markings.QuarterNotesPerMinute, Text.Culture),
                Text.Get("UnitQuarterNotes"), string.Join(" / ", markings.Names))
            : Text.Get("TempoGuideInput");
}
