using Microsoft.VisualStudio.TestTools.UnitTesting;
using ResonantPulse.App.Audio;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class AudioOutputSelectionTests
{
    private static readonly OutputDevice Speakers = new("speakers", "Speakers");
    private static readonly OutputDevice Headphones = new("headphones", "Headphones");

    [TestMethod]
    public void FirstUseSelectsWindowsDefaultRatherThanFirstEnumeratedDevice()
    {
        Assert.AreEqual(Headphones, AudioOutputSelection.Resolve("", Headphones.Id, [Speakers, Headphones]));
    }

    [TestMethod]
    public void SavedOutputWinsOverChangedWindowsDefault()
    {
        Assert.AreEqual(Speakers, AudioOutputSelection.Resolve(Speakers.Id, Headphones.Id, [Headphones, Speakers]));
    }

    [TestMethod]
    public void DisconnectedSavedOutputNeverFallsBackToAnotherSpeaker()
    {
        Assert.IsNull(AudioOutputSelection.Resolve(Speakers.Id, Headphones.Id, [Headphones]));
    }

    [TestMethod]
    public void NoDefaultOrNoEndpointsLeavesSelectionEmpty()
    {
        Assert.IsNull(AudioOutputSelection.Resolve("", "", [Speakers]));
        Assert.IsNull(AudioOutputSelection.Resolve("", Headphones.Id, [Speakers]));
        Assert.IsNull(AudioOutputSelection.Resolve("", Speakers.Id, []));
    }

    [TestMethod]
    public void WindowsEndpointIdentifiersAreComparedCaseInsensitively()
    {
        Assert.AreEqual(Speakers, AudioOutputSelection.Resolve("SPEAKERS", Headphones.Id, [Speakers, Headphones]));
    }
}
