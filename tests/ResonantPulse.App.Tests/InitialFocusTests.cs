using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

/// <summary>
/// When Windows activated the packaged application the focus was on the Settings item, the only thing its navigation held then, and the
/// ring of a focused item showed there. The focus now starts on what a person starts from, placed once, and never taken from a person who
/// has already moved it.
/// </summary>
[TestClass]
public sealed class InitialFocusTests
{
    [TestMethod]
    public void TheFocusStartsOnStartWhenItCanTakeIt()
    {
        Assert.AreEqual(InitialFocus.Place.Start, InitialFocus.Choose(startCanTakeIt: true, audioCanTakeIt: true));
        Assert.AreEqual(InitialFocus.Place.Start, InitialFocus.Choose(startCanTakeIt: true, audioCanTakeIt: false));
    }

    [TestMethod]
    public void WithoutAnOutputStartIsOffAndTheFocusStartsOnAudioWhereOneIsChosen()
    {
        Assert.AreEqual(InitialFocus.Place.Audio, InitialFocus.Choose(startCanTakeIt: false, audioCanTakeIt: true));
    }

    [TestMethod]
    public void WhenNeitherCanTakeTheFocusItIsLeftAlone()
    {
        Assert.AreEqual(InitialFocus.Place.None, InitialFocus.Choose(startCanTakeIt: false, audioCanTakeIt: false));
    }

    [TestMethod]
    public void TheFocusIsPlacedWhileNobodyHasActedAndTheSystemStillHasItWhereItLeftIt()
    {
        Assert.IsTrue(InitialFocus.Takes(personHasActed: false, focusIsWhereTheSystemLeftIt: true),
            "Settings (or nothing) has the focus at the start, and a person who has done nothing yet has not chosen it.");
    }

    [TestMethod]
    public void AFocusThatThePersonMovedIsTheirsAndIsNotTaken()
    {
        // A key or a press of the pointer is the person acting; so is a screen reader that put the focus on a control of its own choice.
        Assert.IsFalse(InitialFocus.Takes(personHasActed: true, focusIsWhereTheSystemLeftIt: true));
        Assert.IsFalse(InitialFocus.Takes(personHasActed: false, focusIsWhereTheSystemLeftIt: false));
        Assert.IsFalse(InitialFocus.Takes(personHasActed: true, focusIsWhereTheSystemLeftIt: false));
    }
}
