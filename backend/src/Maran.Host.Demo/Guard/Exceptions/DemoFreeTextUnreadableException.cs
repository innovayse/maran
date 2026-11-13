namespace Maran.Host.Demo.Guard.Exceptions;

/// <summary>
/// The arguments of an action could not be read in full by the walk that finds visitor text: they nest deeper
/// than the walk goes, hold more values than its budget, or a member could not be read. Raised, not skipped:
/// text the walk did not reach would be stored unscreened, so the write is refused instead.
/// </summary>
public sealed class DemoFreeTextUnreadableException : Exception
{
    /// <summary>The dotted name of the member where the walk stopped.</summary>
    public string Field { get; }

    /// <summary>Creates the exception.</summary>
    /// <param name="field">The dotted name of the member where the walk stopped.</param>
    /// <param name="reason">Why it stopped, in a few words.</param>
    /// <param name="inner">The failure that stopped it, if one did.</param>
    public DemoFreeTextUnreadableException(string field, string reason, Exception? inner = null)
        : base($"The demo cannot read the arguments at '{field}': {reason}.", inner)
    {
        Field = field;
    }
}
