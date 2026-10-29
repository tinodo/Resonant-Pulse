namespace ResonantPulse.App;

/// <summary>
/// How to put the rows of a list in another order by moving them one at a time, without ever moving the row that is kept in
/// place. A control that is taken out of the tree and put back loses the keyboard focus, so the row that has it stays where it is
/// and the others move around it.
/// </summary>
internal static class RowOrder
{
    /// <summary>
    /// The moves that turn <paramref name="current"/> into <paramref name="wanted"/>, which must hold the same rows. A move takes the
    /// row at <c>From</c> out and puts it back at <c>To</c>, as <c>IList.Move</c> does, so the moves are made one after the other.
    /// The row <paramref name="keep"/> is never the one that moves: the rows that come before it are put in front of it first, from
    /// the front, and the rows that come after it behind it, from the back. Without a row to keep, the rows are put in place from the
    /// front. A row that is where it has to be does not move.
    /// </summary>
    internal static IReadOnlyList<(int From, int To)> Moves<T>(IReadOnlyList<T> current, IReadOnlyList<T> wanted, T? keep)
        where T : class
    {
        if (current.Count != wanted.Count) throw new ArgumentException("The rows to put in order must be the rows that are there.", nameof(wanted));
        var working = current.ToList();
        var moves = new List<(int From, int To)>();

        void Put(T row, int index)
        {
            var from = working.IndexOf(row);
            if (from < 0) throw new ArgumentException("A row that is wanted is not in the list.", nameof(wanted));
            if (from == index) return;
            working.RemoveAt(from);
            working.Insert(index, row);
            moves.Add((from, index));
        }

        var anchor = -1;
        if (keep is not null)
            for (var index = 0; index < wanted.Count && anchor < 0; index++)
                if (ReferenceEquals(wanted[index], keep)) anchor = index;

        if (anchor < 0)
        {
            for (var index = 0; index < wanted.Count; index++) Put(wanted[index], index);
            return moves;
        }
        for (var index = 0; index < anchor; index++) Put(wanted[index], index);
        for (var index = wanted.Count - 1; index > anchor; index--) Put(wanted[index], index);
        return moves;
    }
}
