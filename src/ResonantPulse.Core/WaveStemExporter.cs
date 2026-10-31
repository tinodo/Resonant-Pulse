using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace ResonantPulse.Core;

/// <summary>
/// Writes one aligned track for the main beat and one for every authored lane of a compiled plan, and a manifest, as one ZIP archive
/// (docs/WAV-STEMS.md is the specification). Every track is a 16-bit stereo PCM WAV file written by the same code as the mixed export
/// (<see cref="WaveExporter"/>), starts at frame 0 and ends at <see cref="RenderPlan.TotalFrames"/>, so they line up when they are
/// put at the same place in a music program. The entries are named by number and never by lane name: <c>main.wav</c>,
/// <c>lane-01.wav</c> to <c>lane-08.wav</c> and <c>manifest.json</c>; a lane's name is data in the manifest.
/// </summary>
public static class WaveStemExporter
{
    private static readonly SemaphoreSlim Exports = new(1, 1);

    /// <summary>
    /// The compression of every entry. Deflate at the default level: measured on the machine of the change, the other levels were no
    /// faster (the time is the rendering and the 16-bit conversion, not the compression) and the ZIP was larger, and the smallest size
    /// took longer for 15 to 20% less (docs/WAV-STEMS.md has the numbers).
    /// </summary>
    private const CompressionLevel Level = CompressionLevel.Optimal;

    /// <summary>
    /// Every entry is dated 1 January 1980, the earliest date a ZIP entry can have, so that the same plan gives the same bytes in one
    /// build of the app (another .NET runtime may compress the same data to other bytes) and the archive says nothing about when it
    /// was made.
    /// </summary>
    private static readonly DateTimeOffset EntryDate = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const int MaximumNameLength = 256;

    private sealed record StemTrack(string File, string Kind, int? LaneIndex, string? Name, int AudibleOnsetCount);

    /// <summary>
    /// The audio bytes of all the tracks together before compression: four bytes a frame for the main track and for each authored lane,
    /// without the WAV headers and the manifest. A track longer than a RIFF file can be (4 GiB, about 6 hours at 48,000 Hz) is refused
    /// with an <see cref="ArgumentException"/>: RF64 is not supported.
    /// </summary>
    public static long GetUncompressedAudioBytes(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return checked(PcmWaveWriter.AudioBytes(plan) * (plan.LaneCount + 1L));
    }

    /// <summary>
    /// Writes the archive atomically: to a temporary file next to <paramref name="path"/>, then moved over it. Whatever goes wrong
    /// (a file that is open elsewhere, a full disk, a cancelled export), the file that was there stays as it was and the temporary
    /// file is removed. The arguments are checked before anything is written. One export runs at a time; others wait their turn.
    /// <paramref name="laneNames"/> holds the name of every authored lane in lane order (the plan keeps only their number); they
    /// are written into the manifest and nowhere else.
    /// </summary>
    /// <remarks>
    /// The tracks are cut out of the plan after it was compiled: leaving lanes out before compiling would change which clicks the seeded
    /// probability lets through and would start the count-in again in every track. A lane that is muted, left out by solo, has a
    /// probability of 0 or has no click still gets its track, silent and as long as the others. Each track has the master gain and
    /// its own peak limit, so adding the tracks together is not guaranteed to give the mixed WAV back (docs/WAV-STEMS.md says by how much).
    /// </remarks>
    public static async Task ExportAsync(string path, RenderPlan plan, IReadOnlyList<string> laneNames,
        CancellationToken cancellationToken = default)
    {
        var names = Validate(plan, laneNames);
        await Exports.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await AtomicFile.WriteAsync(path, (stream, token) => WriteCoreAsync(stream, plan, names, token), cancellationToken)
                .ConfigureAwait(false);
        }
        finally { Exports.Release(); }
    }

    /// <summary>Writes the same bytes to a stream. This is not atomic: use <see cref="ExportAsync"/> to write a file.</summary>
    public static async Task WriteAsync(Stream stream, RenderPlan plan, IReadOnlyList<string> laneNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var names = Validate(plan, laneNames);
        await WriteCoreAsync(stream, plan, names, cancellationToken).ConfigureAwait(false);
    }

    private static string[] Validate(RenderPlan plan, IReadOnlyList<string> laneNames)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(laneNames);
        if (laneNames.Count != plan.LaneCount)
            throw new ArgumentException("Give the name of every authored lane, in lane order.", nameof(laneNames));
        var names = laneNames.ToArray();
        if (names.Any(name => !IsName(name)))
            throw new ArgumentException($"Lane names must be given, well-formed and at most {MaximumNameLength} characters.", nameof(laneNames));
        _ = GetUncompressedAudioBytes(plan);
        return names;
    }

    private static bool IsName(string? name)
    {
        if (name is null || name.Length > MaximumNameLength) return false;
        for (var index = 0; index < name.Length; index++)
        {
            var c = name[index];
            if (char.IsHighSurrogate(c))
            {
                if (index + 1 >= name.Length || !char.IsLowSurrogate(name[index + 1])) return false;
                index++;
            }
            else if (char.IsLowSurrogate(c)) return false;
        }
        return true;
    }

    private static string LaneFile(int index) => "lane-" + (index + 1).ToString("D2", CultureInfo.InvariantCulture) + ".wav";

    private static async Task WriteCoreAsync(Stream stream, RenderPlan plan, string[] names, CancellationToken cancellationToken)
    {
        var audioBytes = GetUncompressedAudioBytes(plan);
        var tracks = new List<StemTrack>(names.Length + 1)
        {
            new("main.wav", "main", null, null, plan.Onsets.Count(onset => onset.LaneIndex < 0))
        };
        for (var index = 0; index < names.Length; index++)
            tracks.Add(new(LaneFile(index), "lane", index, names[index], plan.Onsets.Count(onset => onset.LaneIndex == index)));

        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        foreach (var track in tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selected = SelectStem(plan, track.LaneIndex);
            var entry = archive.CreateEntry(track.File, Level);
            entry.LastWriteTime = EntryDate;
            await using var output = entry.Open();
            await PcmWaveWriter.WriteAsync(output, selected, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        var manifestEntry = archive.CreateEntry("manifest.json", Level);
        manifestEntry.LastWriteTime = EntryDate;
        await using var manifest = manifestEntry.Open();
        await JsonSerializer.SerializeAsync(manifest, new
        {
            SchemaVersion = 1, Format = "PCM16 stereo", plan.SampleRate, plan.TotalFrames, plan.CountInFrames, plan.LaneCount,
            OriginFrame = 0, UncompressedAudioBytes = audioBytes,
            plan.BeatUnitQuarters, plan.Seed, plan.ProbabilityAlgorithm,
            MasterGain = plan.DefaultMasterGain,
            PeakProtection = "A hard limit at full scale, applied to each track on its own.",
            Recombination = "Each track is rounded to 16 bits and peak-limited on its own, so adding the tracks together can differ from the mixed WAV by a few steps, and by more where the mix itself would clip.",
            MainTrack = "The main beat, the clicks that split it and the count-in. The lane tracks are silent during the count-in.",
            MixPolicy = "Lane mute and solo, probability, gaps, pan and gain are applied as in the mixed WAV. A lane that is left out still has a track: silence of the same length.",
            Loop = false, TailPolicy = "Every track ends at TotalFrames; a click that would ring on past it is cut.",
            Tracks = tracks, plan.TempoSegments, plan.SectionBoundaries,
            SilentRanges = plan.SilentRanges.Select(range => new { range.StartFrame, range.EndFrame }),
            AudibleOnsets = plan.Onsets
        }, DocumentStore.JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One track of the plan: the onsets of the main beat (the main clicks, the clicks that split a beat and the count-in), or of one lane.</summary>
    private static RenderPlan SelectStem(RenderPlan plan, int? laneIndex)
    {
        bool Selected(int index) => laneIndex is int lane ? index == lane : index < 0;
        return new RenderPlan(plan.SampleRate, plan.TotalFrames,
            plan.Onsets.Where(onset => Selected(onset.LaneIndex)).ToArray(),
            plan.TempoSegments.ToArray(), plan.SilentRanges.ToArray(),
            plan.Targets.Where(target => Selected(target.LaneIndex)).ToArray(),
            plan.CountInFrames, plan.DefaultMasterGain,
            plan.MusicalOnsets.Where(onset => Selected(onset.Onset.LaneIndex)).ToArray(),
            plan.Sections, plan.BeatUnitQuarters, plan.Seed, plan.ProbabilityAlgorithm, plan.LaneCount);
    }
}
