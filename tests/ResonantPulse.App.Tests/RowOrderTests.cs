using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ResonantPulse.App.Tests;

[TestClass]
public sealed class RowOrderTests
{
    private sealed class Row(string name)
    {
        public override string ToString() => name;
    }

    private static Row[] Rows(int count) => Enumerable.Range(0, count).Select(index => new Row($"row {index}")).ToArray();

    /// <summary>Makes the moves the way a list does: the row at From comes out and goes back in at To.</summary>
    private static List<Row> Apply(IEnumerable<Row> start, IEnumerable<(int From, int To)> moves, out List<Row> moved)
    {
        var list = start.ToList();
        moved = [];
        foreach (var (from, to) in moves)
        {
            var row = list[from];
            moved.Add(row);
            list.RemoveAt(from);
            list.Insert(to, row);
        }
        return list;
    }

    private static IEnumerable<int[]> Permutations(int count)
    {
        var items = Enumerable.Range(0, count).ToArray();
        IEnumerable<int[]> Walk(int depth)
        {
            if (depth == items.Length)
            {
                yield return (int[])items.Clone();
                yield break;
            }
            for (var index = depth; index < items.Length; index++)
            {
                (items[depth], items[index]) = (items[index], items[depth]);
                foreach (var permutation in Walk(depth + 1)) yield return permutation;
                (items[depth], items[index]) = (items[index], items[depth]);
            }
        }
        return Walk(0);
    }

    [TestMethod]
    public void RowsThatAreAlreadyInOrderDoNotMove()
    {
        var rows = Rows(5);
        Assert.AreEqual(0, RowOrder.Moves(rows, rows, rows[2]).Count);
        Assert.AreEqual(0, RowOrder.Moves(rows, rows, null).Count);
    }

    [TestMethod]
    public void TheMovesGiveTheWantedOrderForEveryOrderOfUpToSixRowsWhicheverRowIsKept()
    {
        for (var count = 0; count <= 6; count++)
        {
            var rows = Rows(count);
            foreach (var permutation in Permutations(count))
            {
                var wanted = permutation.Select(index => rows[index]).ToArray();
                // No row kept, then each row in turn.
                var keeps = new List<Row?> { null };
                keeps.AddRange(rows);
                foreach (var keep in keeps)
                {
                    var result = Apply(rows, RowOrder.Moves(rows, wanted, keep), out var moved);
                    CollectionAssert.AreEqual(wanted, result,
                        $"{string.Join(", ", permutation)} with {(keep is null ? "nothing" : keep.ToString())} kept");
                    if (keep is not null)
                        Assert.IsFalse(moved.Contains(keep), $"{keep} was kept but moved: {string.Join(", ", permutation)}");
                }
            }
        }
    }

    [TestMethod]
    public void ARowThatMovesPastItsNeighboursLeavesTheKeptRowWhereItIs()
    {
        // Row 1 is typed past rows 2 and 3 while the person is in row 4's box: only the rows around it move.
        var rows = Rows(6);
        var wanted = new[] { rows[0], rows[2], rows[3], rows[1], rows[4], rows[5] };
        var moves = RowOrder.Moves(rows, wanted, rows[4]);
        var result = Apply(rows, moves, out var moved);
        CollectionAssert.AreEqual(wanted, result);
        Assert.IsFalse(moved.Contains(rows[4]));
        Assert.IsFalse(moved.Contains(rows[0]), "a row that has nothing to do with the change stays too");
        Assert.IsFalse(moved.Contains(rows[5]));
    }

    [TestMethod]
    public void TheRowThatWasMovedCanBeTheOneThatIsKept()
    {
        // The person typed a position and pressed Tab: the focus is on that row's strength, and it is the row that moved.
        var rows = Rows(5);
        var wanted = new[] { rows[0], rows[2], rows[3], rows[4], rows[1] };
        var result = Apply(rows, RowOrder.Moves(rows, wanted, rows[1]), out var moved);
        CollectionAssert.AreEqual(wanted, result);
        Assert.IsFalse(moved.Contains(rows[1]), "the kept row does not move, the others move around it");
    }

    [TestMethod]
    public void AKeptRowThatIsNotWantedIsTreatedAsNoRowToKeep()
    {
        var rows = Rows(3);
        var wanted = new[] { rows[2], rows[0], rows[1] };
        var result = Apply(rows, RowOrder.Moves(rows, wanted, new Row("gone")), out _);
        CollectionAssert.AreEqual(wanted, result);
    }

    [TestMethod]
    public void RowsThatAreNotTheRowsThatAreThereAreRefused()
    {
        var rows = Rows(3);
        Assert.ThrowsExactly<ArgumentException>(() => RowOrder.Moves(rows, rows.Take(2).ToArray(), null));
        Assert.ThrowsExactly<ArgumentException>(() => RowOrder.Moves(rows, [rows[0], rows[1], new Row("another")], null));
    }
}
