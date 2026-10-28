using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;
using ResonantPulse.Core;

namespace ResonantPulse.App.Tests;

/// <summary>
/// Completeness and plain-language rules for the page redesign (docs/design/pages-design.md). The first test covers every
/// string the app owns; the others cover the copy written for the redesigned pages, which is listed here as it grows.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class PageTextTests
{
    private static readonly string[] Languages = ["en", "nl", "de", "fr", "es"];

    /// <summary>Strings written or rewritten for the redesigned pages.</summary>
    private static readonly string[] PageKeys =
    [
        "On", "Off", "Appearance", "ReducedMotion", "ReducedMotionHelp", "AboutApp", "VersionLine", "TimingHonesty",
        "AudioNote", "UnitSeconds", "UnitBeats", "UnitBars", "HelpBasics", "HelpWhilePlaying", "HelpShortcuts",
        "ShortcutStart", "ShortcutMute", "ShortcutHit", "KeySpace", "KeyCtrl", "ExportBars", "ExportBarsHelp",
        "PracticeStopAfter", "PracticeTimerHint", "PracticeLimitsSaved",
        "LibraryPurpose", "LibraryProjects", "LibraryLoading", "LibraryUnreadable", "CloseUnsaved", "NotesProjectChanged", "LibraryNoMatch", "LibraryEmptyTitle", "ExportTitle", "PresetsHelp",
        "Presets", "Load", "Search", "SaveLibrary", "EmptyLibrary", "ExportNotice", "Replace", "MidiPreflight",
        "PresetDesc_open", "PresetDesc_compound-6-8", "PresetDesc_additive-7-8", "PresetDesc_swing", "PresetDesc_polyrhythm-3-4",
        "PresetDesc_polymeter-3-4", "PresetDesc_euclidean-5-8", "PresetDesc_paradiddle", "PresetDesc_warmup-ladder",
        "PresetDesc_internal-time", "PresetDesc_ramp-domains", "PresetDesc_seeded-density",
        "StagePurpose", "NowPlaying", "UpNextBadge", "NothingUpNext", "SetlistHelp", "SetlistEmptyTitle", "NotesFor", "NotesSaved",
        "LockHelp", "AddSong", "ArmedSong", "StageTransition", "EmptySetlist", "ChooseSong",
        "TrainPurpose", "TrainProgramTitle", "TrainProgramHelp", "EditInStudio", "ProgramEmptyTitle", "BuildProgram", "ProgramIdle",
        "TrainProgramChanged", "TrainProgramRemoved", "Progress",
        "UnitMs", "IncludeSilentHelp", "TempoChartAxis", "Target", "MainTarget", "IncludeSilentTargets", "Tolerance",
        "Program", "ProgramEmpty", "BeginTiming", "FinishTiming", "Hit", "TimingBudget", "TimingDriftUnavailable", "TimingNoMatches",
        "TimingAdditional", "TimingLab", "TimingLimits", "TimingArmed", "TimingReport", "TimingRequiresPlayback", "TimingPendingEdits",
        "TimingTargetChanged", "TimingInvalidated",
        "StudioPurpose", "StudioLocked", "StudioEmptyTitle", "StudioEmptyBody", "LanePlain", "LaneThreeInFour", "LaneThreeInThree",
        "LaneEuclidean", "LaneField", "LaneBeatName", "LaneGroupRhythm", "LaneGroupVariation", "LanePulses", "LaneCycle", "LaneOffset",
        "UnitQuarterNotes", "LaneRhythmHelp", "LaneExactNote", "CustomPositions", "LanePan", "LaneSoundCaption", "PanLeft", "PanRight", "PanCenter",
        "LaneChance", "LaneChanceHelp", "LaneSoloHelp", "LaneMuteHelp", "LaneMore", "LaneCopySuffix", "LaneNameRequired",
        "EuclideanHits", "EuclideanSteps", "EuclideanExplain", "EuclideanInvalid", "EuclideanWhole", "EuclideanPreviewName", "Create",
        "Advanced",
        "AdvancedHelp", "SeedLabel", "SeedInvalid"
    ];

    /// <summary>Internal words that mean nothing to a musician (docs/design/pages-design.md, copy rules).</summary>
    private static readonly string[] BannedEnglish =
        ["onset", "armed", "deterministic", "graph", "flush", "document", "frame", "quantum", "spliced"];

    /// <summary>Wording that legitimately matches English: a number format, a key name, a unit.</summary>
    private static readonly HashSet<string> SameAsEnglishAllowed =
    [
        "VersionLine/de", "VersionLine/fr", "KeyCtrl/nl", "KeyCtrl/fr", "KeyCtrl/es",
        "UnitMs/nl", "UnitMs/de", "UnitMs/fr", "UnitMs/es",
        "LaneGroupVariation/de", "LaneGroupVariation/fr"
    ];

    private static IReadOnlyList<int> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)(?:[:,][^}]*)?\}").Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .Distinct().Order().ToArray();

    private static T InLanguage<T>(string language, Func<T> read)
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            Text.Select(language);
            return read();
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static string Get(string language, string key) => InLanguage(language, () => Text.Get(key));

    [TestMethod]
    public void EveryStringExistsInAllFiveLanguagesWithTheSamePlaceholders()
    {
        var failures = new List<string>();
        foreach (var key in Text.Keys)
        {
            var english = Get("en", key);
            var expected = Placeholders(english);
            foreach (var language in Languages)
            {
                var value = Get(language, key);
                if (string.IsNullOrWhiteSpace(value)) failures.Add($"{key}/{language} is empty");
                else if (!expected.SequenceEqual(Placeholders(value)))
                    failures.Add($"{key}/{language} has placeholders [{string.Join(",", Placeholders(value))}], English has [{string.Join(",", expected)}]");
            }
        }
        Assert.AreEqual(0, failures.Count, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void EveryPageKeyIsDefined()
    {
        foreach (var key in PageKeys)
            Assert.IsTrue(Text.Keys.Contains(key), $"{key} is not defined");
    }

    [TestMethod]
    public void PageCopyIsTranslatedNotCopiedFromEnglish()
    {
        foreach (var key in PageKeys)
        {
            var english = Get("en", key);
            foreach (var language in Languages.Skip(1))
            {
                if (SameAsEnglishAllowed.Contains($"{key}/{language}")) continue;
                Assert.AreNotEqual(english, Get(language, key), $"{key}/{language} is untranslated");
            }
        }
    }

    [TestMethod]
    public void PageCopyAvoidsInternalJargon()
    {
        foreach (var key in PageKeys)
        {
            var english = Get("en", key).ToLowerInvariant();
            foreach (var word in BannedEnglish)
                Assert.IsFalse(english.Contains(word, StringComparison.Ordinal), $"{key} uses “{word}”: {english}");
        }
    }

    [TestMethod]
    public void TheSavedLimitsLineFillsEveryPlaceholderInEveryLanguage()
    {
        foreach (var language in Languages)
        {
            var line = InLanguage(language, () => Text.Format("PracticeLimitsSaved", "5:00", "off", "8"));
            StringAssert.Contains(line, "5:00", language);
            StringAssert.Contains(line, "8", language);
            Assert.IsFalse(line.Contains('{') || line.Contains('}'), $"{language}: {line}");
        }
    }

    [TestMethod]
    public void EveryBuiltInRhythmHasADescriptionInEveryLanguage()
    {
        foreach (var preset in BuiltInLibrary.Presets().Concat(BuiltInLibrary.PracticePrograms()))
        foreach (var language in Languages)
        {
            var name = Get(language, "Preset_" + preset.Id);
            var description = Get(language, "PresetDesc_" + preset.Id);
            Assert.AreNotEqual("Preset_" + preset.Id, name, $"{language}: {preset.Id} has no name");
            Assert.AreNotEqual("PresetDesc_" + preset.Id, description, $"{language}: {preset.Id} has no description");
            Assert.IsTrue(description.Length is > 20 and < 200, $"{language}/{preset.Id}: {description.Length} characters");
        }
    }

    [TestMethod]
    public void TheStudioSentencesFillEveryPlaceholderInEveryLanguage()
    {
        foreach (var language in Languages)
        {
            var purpose = InLanguage(language, () => Text.Format("StudioPurpose", "My groove"));
            StringAssert.Contains(purpose, "My groove", language);
            // The sentence says how to hear the changes in the app's own words for the two buttons (a Start that resumes a
            // pause keeps the revision it paused on, so "the next Start" would be wrong).
            StringAssert.Contains(purpose, InLanguage(language, () => Text.Get("Stop")), language);
            StringAssert.Contains(purpose, InLanguage(language, () => Text.Get("Start")), language);
            var beat = InLanguage(language, () => Text.Format("LaneBeatName", 2, 3, Text.Get("LevelStrong")));
            StringAssert.Contains(beat, "2", language);
            StringAssert.Contains(beat, "3", language);
            var field = InLanguage(language, () => Text.Format("LaneField", 4, Text.Get("Volume")));
            StringAssert.Contains(field, "4", language);
            var euclid = InLanguage(language, () => Text.Format("EuclideanExplain", 5, 8));
            StringAssert.Contains(euclid, "5", language);
            StringAssert.Contains(euclid, "8", language);
            var left = InLanguage(language, () => Text.Format("PanLeft", 30));
            var right = InLanguage(language, () => Text.Format("PanRight", 25));
            StringAssert.Contains(left, "30", language);
            StringAssert.Contains(right, "25", language);
            // The locked note names the page and the switch as that language calls them, not as English does.
            var locked = InLanguage(language, () => Text.Format("StudioLocked", Text.Get("Stage"), Text.Get("Lock")));
            StringAssert.Contains(locked, InLanguage(language, () => Text.Get("Stage")), language);
            StringAssert.Contains(locked, InLanguage(language, () => Text.Get("Lock")), language);
            foreach (var line in new[] { purpose, beat, field, euclid, left, right, locked })
                Assert.IsFalse(line.Contains('{') || line.Contains('}'), $"{language}: {line}");
        }
    }

    [TestMethod]
    public void TheTimerPanelCaptionsAreShortSoTheyNeverSqueezeABox()
    {
        foreach (var language in Languages)
        foreach (var key in new[] { "PracticeStopAfter", "UnitSeconds", "UnitBeats", "UnitBars" })
            Assert.IsTrue(Get(language, key).Length <= 14, $"{language}/{key}: {Get(language, key)}");
    }
}
