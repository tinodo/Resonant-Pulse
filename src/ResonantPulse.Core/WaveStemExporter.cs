using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace ResonantPulse.Core;

/// <summary>Atomically exports one aligned PCM16 main/subdivision track and every authored lane in a local ZIP.</summary>
public static class WaveStemExporter
{
    private static readonly SemaphoreSlim Exports = new(1, 1);
    private sealed record StemTrack(string File, int? LaneIndex, string? Name);

    /// <summary>PCM payload bytes before ZIP compression, excluding headers and metadata. Validates the per-track RIFF limit.</summary>
    public static long GetUncompressedAudioBytes(RenderPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return checked(PcmWaveWriter.AudioBytes(plan) * (plan.LaneCount + 1));
    }

    /// <summary>
    /// Preserves compiled audible decisions, gaps, pan and gain without recompiling probability.
    /// All tracks start at frame zero and end at TotalFrames. Count-in belongs to main only.
    /// Muted/solo-suppressed lanes remain as silent tracks. Independent PCM16 rounding and
    /// peak protection mean summing stems need not reproduce the mixed WAV bit-for-bit.
    /// Cancellation or a failed write leaves the prior destination unchanged.
    /// </summary>
    public static async Task ExportAsync(string path, RenderPlan plan, IReadOnlyList<string> laneNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(laneNames);
        if (laneNames.Count != plan.LaneCount)
            throw new ArgumentException("Provide every authored lane in its original index order.", nameof(laneNames));
        string[] names = laneNames.ToArray();
        long audioBytes = GetUncompressedAudioBytes(plan);
        if (names.Length != plan.LaneCount || names.Any(name => name is null || name.Length > 256))
            throw new ArgumentException("Lane names must be non-null and at most 256 characters.", nameof(laneNames));
        await Exports.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tracks = new[] { new StemTrack("main.wav", null, null) }
                .Concat(names.Select((name, index) => new StemTrack(
                    "lane-" + (index + 1).ToString("D2", CultureInfo.InvariantCulture) + ".wav", index, name))).ToArray();
            await AtomicFile.WriteAsync(path, async (stream, token) =>
            {
                using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
                foreach (var track in tracks)
                {
                    token.ThrowIfCancellationRequested();
                    var selected = SelectStem(plan, track.LaneIndex);
                    await using var output = archive.CreateEntry(track.File, CompressionLevel.Optimal).Open();
                    await PcmWaveWriter.WriteAsync(output, selected, token).ConfigureAwait(false);
                }
                await using var manifest = archive.CreateEntry("manifest.json", CompressionLevel.Optimal).Open();
                await JsonSerializer.SerializeAsync(manifest, new
                {
                    SchemaVersion = 1, Format = "PCM16 stereo", plan.SampleRate, plan.TotalFrames, plan.CountInFrames, plan.LaneCount,
                    OriginFrame = 0, UncompressedAudioBytes = audioBytes,
                    plan.BeatUnitQuarters, plan.Seed, plan.ProbabilityAlgorithm,
                    MasterGain = plan.DefaultMasterGain, PeakProtection = "hard clamp [-1,1] independently per stem",
                    Recombination = "Independent PCM16 rounding and peak protection; not guaranteed bit-identical to mixed WAV",
                    MainTrack = "Main pulse, subdivisions and count-in; lane tracks retain leading count-in silence",
                    MixPolicy = "Preserve compiled lane mute/solo, probability, pan and gain; retain silent lanes",
                    Loop = false, TailPolicy = "truncate at finite duration",
                    Tracks = tracks, plan.TempoSegments, plan.SectionBoundaries,
                    SilentRanges = plan.SilentRanges.Select(range => new { range.StartFrame, range.EndFrame }),
                    AudibleOnsets = plan.Onsets
                }, DocumentStore.JsonOptions, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { Exports.Release(); }
    }

    private static RenderPlan SelectStem(RenderPlan plan, int? laneIndex)
    {
        bool Selected(int index) => laneIndex is int lane ? index == lane : index < 0;
        // Select after compilation: removing/reindexing authored lanes before compiling
        // could change seeded decisions and would restart count-in in every track.
        return new RenderPlan(plan.SampleRate, plan.TotalFrames,
            plan.Onsets.Where(onset => Selected(onset.LaneIndex)).ToArray(),
            plan.TempoSegments.ToArray(), plan.SilentRanges.ToArray(),
            plan.Targets.Where(target => Selected(target.LaneIndex)).ToArray(),
            plan.CountInFrames, plan.DefaultMasterGain,
            plan.MusicalOnsets.Where(onset => Selected(onset.Onset.LaneIndex)).ToArray(),
            plan.Sections, plan.BeatUnitQuarters, plan.Seed, plan.ProbabilityAlgorithm, plan.LaneCount);
    }
}
