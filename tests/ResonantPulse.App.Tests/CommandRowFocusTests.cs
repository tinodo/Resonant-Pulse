using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

/// <summary>
/// The timing check's commands are Begin, Hit, Finish and Export (in that order). Begin and Finish build the Train page again, and the commands
/// of the old page are gone with it: the keyboard focus goes on to the command that takes over, as it does when a focused command turns itself off.
/// </summary>
[TestClass]
public sealed class CommandRowFocusTests
{
    private static readonly bool[] Running = [false, true, true, false];
    private static readonly bool[] Finished = [true, false, false, true];
    private static readonly bool[] Idle = [true, false, false, false];

    [TestMethod]
    public void AfterBeginTheFocusIsOnHitWhichTakesOverFromBegin()
    {
        Assert.AreEqual(1, CommandRowFocus.After(hadFocus: 0, Running));
    }

    [TestMethod]
    public void AfterFinishTheFocusIsOnExportWhichTheResultTurnedOn()
    {
        Assert.AreEqual(3, CommandRowFocus.After(hadFocus: 2, Finished));
    }

    [TestMethod]
    public void ACommandThatCanStillHaveTheFocusKeepsIt()
    {
        Assert.AreEqual(1, CommandRowFocus.After(hadFocus: 1, Running));
        Assert.AreEqual(0, CommandRowFocus.After(hadFocus: 0, Idle));
    }

    [TestMethod]
    public void TheSearchGoesOnInTheOrderOfTheRowAndThenBackToItsStart()
    {
        Assert.AreEqual(0, CommandRowFocus.After(hadFocus: 2, Idle));
    }

    [TestMethod]
    public void TheSearchReachesEveryCommandOfTheRowFromAnyStart()
    {
        // The only command that can take the focus may be the one just before the start of the search, which is the last one it looks at.
        for (var only = 0; only < 4; only++)
        {
            for (var start = 0; start < 4; start++)
            {
                var row = new bool[4];
                row[only] = true;
                Assert.AreEqual(only, CommandRowFocus.After(start, row), $"Only command {only} can take the focus; the search began at {start}.");
            }
        }
    }

    [TestMethod]
    public void AFocusThatWasNotOnTheRowIsNotMoved()
    {
        Assert.IsNull(CommandRowFocus.After(hadFocus: null, Running));
        Assert.IsNull(CommandRowFocus.After(hadFocus: -1, Running));
        Assert.IsNull(CommandRowFocus.After(hadFocus: 4, Running));
    }

    [TestMethod]
    public void WhenNoCommandCanTakeTheFocusNothingGetsIt()
    {
        Assert.IsNull(CommandRowFocus.After(hadFocus: 2, [false, false, false, false]));
    }
}
