namespace ResonantPulse.App;

/// <summary>What a control does when the answer to one of its submissions comes back.</summary>
internal enum SubmissionAnswer
{
    /// <summary>A newer value was submitted, or typed, since: that one decides what the control shows.</summary>
    Superseded,

    /// <summary>The value is applied. The control already shows it.</summary>
    Applied,

    /// <summary>The value was refused (or could not be saved), and nothing newer is on its way: the control shows what the project holds.</summary>
    Refused
}

/// <summary>
/// Follows the values one control submits. A value is submitted when the person commits it, but it is applied later (it waits
/// in the edit queue), so for a while the control shows a value that the project does not hold yet, and answers can come back
/// after the person has gone on. The rules, all of which were once wrong:
/// an answer to anything but the newest submission (or the newest change, for a slider that is still being moved) is ignored;
/// an empty or invalid draft submits nothing, and shows the value that is on its way rather than the one the project still
/// holds (otherwise the box ends up showing a value the project no longer has); a refusal puts the control back only when
/// nothing newer exists, and then to the newest value that is still in the queue, if there is one: a value that is refused at
/// once (a blank name) is answered before an older value that is still waiting, and that older value is about to be applied.
/// Pure, so every order the answers can arrive in is tested without a window.
/// </summary>
internal sealed class SubmissionTracker<T> where T : notnull
{
    private readonly List<(int Ticket, T Value)> _unanswered = [];
    private int _issued;
    private int _latest;

    /// <summary>A value is submitted. It is what the project is about to hold, until an answer says otherwise. Returns its ticket.</summary>
    internal int Begin(T value)
    {
        _latest = ++_issued;
        _unanswered.Add((_latest, value));
        return _latest;
    }

    /// <summary>The person changed the control again before submitting (a slider that is still moving): older answers no longer decide.</summary>
    internal void Changed() => _latest = ++_issued;

    /// <summary>True, with the newest value, while a submitted value has not been answered yet.</summary>
    internal bool TryGetPending(out T value)
    {
        if (_unanswered.Count == 0)
        {
            value = default!;
            return false;
        }
        value = _unanswered[^1].Value;
        return true;
    }

    /// <summary>What the control shows after a refusal or for an invalid draft: the value on its way, or else <paramref name="held"/>, what the project holds.</summary>
    internal T OnItsWayOr(T held) => TryGetPending(out var onItsWay) ? onItsWay : held;

    /// <summary>The project answered the submission with this ticket.</summary>
    internal SubmissionAnswer Resolve(int ticket, bool applied)
    {
        _unanswered.RemoveAll(item => item.Ticket == ticket);
        if (ticket != _latest) return SubmissionAnswer.Superseded;
        if (applied)
        {
            // The queue runs in the order it was asked: everything submitted before this value has been dealt with.
            _unanswered.Clear();
            return SubmissionAnswer.Applied;
        }
        // Refused at once, an older value may still be in the queue: the control shows it now, and its answer decides next.
        if (_unanswered.Count > 0) _latest = _unanswered[^1].Ticket;
        return SubmissionAnswer.Refused;
    }
}
