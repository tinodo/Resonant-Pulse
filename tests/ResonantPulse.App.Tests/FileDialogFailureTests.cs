using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Localization;

namespace ResonantPulse.App.Tests;

/// <summary>
/// A file dialog (the Windows window that asks where to save a file, or which one to open) that cannot be opened is reported as that,
/// in the person's language, and not as a problem with the audio output (which is what every <see cref="COMException"/> used to read as).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FileDialogFailureTests
{
    private static readonly string[] Languages = ["en", "nl", "de", "fr", "es"];

    // What the save dialog throws on the private desktop of a developer machine: "Invalid window handle" (seen in the real app).
    private const int InvalidWindowHandle = unchecked((int)0x80070578);

    private static void InEveryLanguage(Action<string> check)
    {
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            foreach (var language in Languages)
            {
                Text.Select(language);
                check(language);
            }
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    [TestMethod]
    [DataRow(InvalidWindowHandle)]
    [DataRow(unchecked((int)0x80004005))]
    [DataRow(unchecked((int)0x8007139F))]
    [DataRow(unchecked((int)0x800706BA))]
    public void AFileDialogThatCannotBeOpenedIsReportedAsThatAndNeverAsAnAudioProblem(int hresult)
    {
        InEveryLanguage(language =>
        {
            var shown = ErrorText.Explain(FileDialogFailure.ForPage(new COMException("The dialog failed.", hresult)));
            Assert.AreEqual(Text.Get("ErrorFileDialog"), shown, language);
            Assert.AreNotEqual("ErrorFileDialog", shown, $"{language}: the sentence is not defined");
            Assert.AreNotEqual(Text.Get("AudioError"), shown, $"{language}: a dialog that could not be opened must not read as an audio problem");
        });
    }

    [TestMethod]
    public void TheAudioPathKeepsSayingAudioForTheSameKindOfFailure()
    {
        // The audio code reports its own failures as COMException too (AudioTransport): they still read as the output failing.
        InEveryLanguage(language =>
        {
            foreach (var hresult in new[] { InvalidWindowHandle, unchecked((int)0x88890004) })
                Assert.AreEqual(Text.Get("AudioError"), ErrorText.Explain(new COMException("The audio graph failed.", hresult)), language);
        });
    }

    [TestMethod]
    public void WhateverTheDialogFailsWithItIsReportedAsTheDialogNotAsAFileOrAccessOrRhythmProblem()
    {
        // Nothing was read or written when the dialog fails, so a sentence about a file, access or a rhythm would be wrong.
        var failures = new Exception[]
        {
            new InvalidOperationException("The window is not ready."), new UnauthorizedAccessException("Access is denied."),
            new IOException("The folder is not there."), new ArgumentException("The name is not valid."),
            new InvalidDataException("Invalid."), new NotSupportedException("Not supported."), new COMException("Failed.", InvalidWindowHandle)
        };
        InEveryLanguage(language =>
        {
            foreach (var failure in failures)
            {
                var shown = ErrorText.Explain(FileDialogFailure.ForPage(failure));
                Assert.AreEqual(Text.Get("ErrorFileDialog"), shown, $"{language}: {failure.GetType().Name}");
                foreach (var other in new[] { "ErrorAccess", "ErrorFile", "ErrorProject", "ErrorGeneral", "ErrorRhythm", "AudioError" })
                    Assert.AreNotEqual(Text.Get(other), shown, $"{language}: {failure.GetType().Name} read as {other}");
            }
        });
    }

    [TestMethod]
    public void ARequestToStopIsNotAFailureOfTheDialog()
    {
        // Closing the window while a dialog is being opened cancels; the page swallows that and shows nothing.
        foreach (Exception cancelled in new Exception[] { new OperationCanceledException(), new TaskCanceledException() })
            Assert.AreSame(cancelled, FileDialogFailure.ForPage(cancelled));
    }

    [TestMethod]
    public void TheCauseStaysWithTheFailureSoThatTheLogCanNameTheHresult()
    {
        var cause = new COMException("Invalid window handle.", InvalidWindowHandle);
        var reported = FileDialogFailure.ForPage(cause);
        var inner = reported.InnerException ?? reported;
        Assert.AreSame(cause, inner);
        Assert.AreEqual(InvalidWindowHandle, inner.HResult);
        StringAssert.Contains(reported.ToString(), "0x80070578");
    }

    [TestMethod]
    public void TheSentenceSaysWhatHappenedAndWhatToDoInPlainWordsInEveryLanguage()
    {
        var translations = new HashSet<string>();
        InEveryLanguage(language =>
        {
            var text = Text.Get("ErrorFileDialog");
            Assert.AreNotEqual("ErrorFileDialog", text, language);
            Assert.IsTrue(text.Length is >= 60 and <= 200, $"{language}: {text.Length} characters (a notice is compact: it must not squeeze a page at 400 DIP)");
            Assert.IsFalse(text.Contains('{') || text.Contains('}'), language);
            foreach (var word in new[] { "HRESULT", "exception", "handle", "dialog", "picker", "0x" })
                Assert.IsFalse(text.Contains(word, StringComparison.OrdinalIgnoreCase), $"{language}: “{word}” is not a word for a person: {text}");
            Assert.IsFalse(Regex.IsMatch(text, @"\bCOM\b"), $"{language}: “COM” is not a word for a person: {text}");
            translations.Add(text);
        });
        Assert.AreEqual(5, translations.Count, "Each language has its own sentence.");
    }

    [TestMethod]
    public void TheFrenchSentenceKeepsItsNoBreakSpaceBeforeAColon()
    {
        // The usual rule of French typography, kept as the other French sentences of the page keep it, so that a narrow notice never
        // starts a line with the colon.
        var previousLanguage = Text.Language;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            Text.Select("fr");
            var text = Text.Get("ErrorFileDialog");
            for (var index = 1; index < text.Length; index++)
                if (text[index] is ':' or ';' or '?' or '!')
                    Assert.AreEqual('\u00A0', text[index - 1], $"“{text}” has an ordinary space (or none) before “{text[index]}”");
        }
        finally
        {
            Text.Select(previousLanguage);
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }
}
