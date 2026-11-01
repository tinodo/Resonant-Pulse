using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The command of the tracks export is switched off from the press to the end of the export. A keyboard user who pressed it had the focus on
/// it: the system moves the focus on to the next control when the command goes off, and nothing put it back. When the focus goes back, and when
/// it must not (it is the person's, or the command cannot have it), is decided here.
/// </summary>
[TestClass]
public sealed class CommandFocusTests
{
    [TestMethod]
    public void ACommandThatHadTheFocusAtThePressTakesItBackWhenNothingMovedItAndTheCommandCanHaveIt()
    {
        Assert.IsTrue(CommandFocus.TakesFocusBack(hadFocusAtPress: true, commandCanTakeIt: true, focusIsWhereTheSystemLeftIt: true),
            "After the question's Cancel, after Saved and after an error the person is on the command again, not on the first ready-made rhythm.");
    }

    [TestMethod]
    public void ACommandThatDidNotHaveTheFocusAtThePressNeverTakesIt()
    {
        // Pressed by a screen reader's own action or by automation: nothing was on the command, so nothing is taken from anywhere.
        Assert.IsFalse(CommandFocus.TakesFocusBack(hadFocusAtPress: false, commandCanTakeIt: true, focusIsWhereTheSystemLeftIt: true));
    }

    [TestMethod]
    public void AFocusThatThePersonMovedMeanwhileIsTheirsAndIsNotTaken()
    {
        // Tab, a click or a screen reader moved it while the archive was written: where it is now is where the person chose to be.
        Assert.IsFalse(CommandFocus.TakesFocusBack(hadFocusAtPress: true, commandCanTakeIt: true, focusIsWhereTheSystemLeftIt: false));
    }

    [TestMethod]
    public void ACommandThatCannotHaveTheFocusDoesNotTryIt()
    {
        // The page was built again (another command stands in its place) or the command is still off: there is nothing to give the focus to.
        Assert.IsFalse(CommandFocus.TakesFocusBack(hadFocusAtPress: true, commandCanTakeIt: false, focusIsWhereTheSystemLeftIt: true));
    }

    [TestMethod]
    public void OnlyTheCaseWhereAllThreeConditionsHoldTakesTheFocusBack()
    {
        var yes = new[] { true, false };
        foreach (var hadFocus in yes)
        foreach (var canTake in yes)
        foreach (var untouched in yes)
            Assert.AreEqual(hadFocus && canTake && untouched, CommandFocus.TakesFocusBack(hadFocus, canTake, untouched),
                $"had the focus {hadFocus}, can take it {canTake}, nobody moved it {untouched}");
    }
}
