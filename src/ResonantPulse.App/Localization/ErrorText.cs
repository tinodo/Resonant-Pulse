using System.Runtime.InteropServices;
using System.Text.Json;
using ResonantPulse.Core;

namespace ResonantPulse.App.Localization;

internal static class ErrorText
{
    public static string Explain(Exception exception)
    {
        // Typed export outcomes take precedence even over a coincidentally
        // known localized Message. Recovery semantics never depend on parsing
        // Core's English diagnostics or on the generic IOException classifier.
        if (exception is WaveExportException wave)
        {
            var key = wave.Failure switch
            {
                WaveExportFailure.RollbackFailed => "ErrorWaveRollback",
                WaveExportFailure.CommittedCleanupFailed => "ErrorWaveCommittedCleanup",
                WaveExportFailure.FailedCleanup => "ErrorWaveFailedCleanup",
                _ => "ErrorFile"
            };
            return Text.Get(key) + Environment.NewLine + Environment.NewLine +
                Text.Format("ErrorWavePaths", wave.WavePath, wave.ManifestPath,
                    string.Join(Environment.NewLine, wave.RecoveryPaths));
        }
        // OS/provider/Core diagnostics are kept intact in the local log. They
        // are not passed off as a translated application message.
        var message = exception.Message;
        if (Text.TryLocalizeKnownMessage(message, out var translated)) return translated;
        if (exception is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
            return Explain(aggregate.InnerExceptions[0]);
        if (exception is UnauthorizedAccessException) return Text.Get("ErrorAccess");
        if (exception is InvalidDataException or JsonException) return Text.Get("ErrorProject");
        if (exception is IOException) return Text.Get("ErrorFile");
        if (exception is COMException) return Text.Get("AudioError");
        // Preflight text can contain "constant-tempo steps" or "swing".
        // Classify export constraints before generic authoring keywords, which
        // would otherwise recommend the very ramps MIDI explicitly rejects.
        if (message.Contains("PPQ", StringComparison.OrdinalIgnoreCase))
            return Text.Get("ErrorMidiPpq");
        if (message.Contains("MIDI", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("continuous ramp", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("constant-tempo steps", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Tempo-field rounding", StringComparison.OrdinalIgnoreCase))
        {
            if (message.Contains("continuous ramp", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("constant-tempo steps", StringComparison.OrdinalIgnoreCase))
                return Text.Get("ErrorMidiRamp");
            return Text.Get("ErrorMidi");
        }
        if (message.Contains("Swing", StringComparison.OrdinalIgnoreCase)) return Text.Get("ErrorSwing");
        if (message.Contains("Constant", StringComparison.OrdinalIgnoreCase)) return Text.Get("ErrorRamp");
        if (message.Contains("gap", StringComparison.OrdinalIgnoreCase)) return Text.Get("ErrorGap");
        if (message.Contains("group", StringComparison.OrdinalIgnoreCase)) return Text.Get("ErrorGroup");
        if (message.Contains("density", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("4096", StringComparison.Ordinal) || message.Contains("million", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("voices", StringComparison.OrdinalIgnoreCase)) return Text.Get("ErrorDensity");
        if (exception is ArgumentException or FormatException or OverflowException or DivideByZeroException)
            return Text.Get("ErrorRhythm");
        return Text.Get("ErrorGeneral");
    }
}
