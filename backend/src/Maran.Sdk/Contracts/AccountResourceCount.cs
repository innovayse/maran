namespace Maran.Sdk.Contracts;

/// <summary>
/// One module's answer to "how many of this kind does the account own": either a count, or the
/// explicit statement that it could not be taken.
/// </summary>
/// <remarks>
/// The two outcomes are built only through <see cref="Counted"/> and <see cref="Unavailable"/>, so
/// a count can never be negative and an unavailable answer can never carry a number a caller might
/// mistake for a measurement. <see cref="Used"/> is zero on an unavailable answer; a caller reads
/// <see cref="State"/> first, always.
/// </remarks>
public sealed record AccountResourceCount
{
    /// <summary>The kind of resource this answer is about.</summary>
    public AccountResourceKind Kind { get; }

    /// <summary>Whether the module counted; decides whether <see cref="Used"/> means anything.</summary>
    public AccountResourceCountState State { get; }

    /// <summary>
    /// How many the account owns when <see cref="State"/> is
    /// <see cref="AccountResourceCountState.Counted"/>; zero otherwise.
    /// </summary>
    public int Used { get; }

    /// <summary>Builds an answer; reached only through the two factories.</summary>
    /// <param name="kind">The kind of resource counted.</param>
    /// <param name="state">Whether the count was taken.</param>
    /// <param name="used">The count, or zero when it was not taken.</param>
    private AccountResourceCount(AccountResourceKind kind, AccountResourceCountState state, int used)
    {
        Kind = kind;
        State = state;
        Used = used;
    }

    /// <summary>An answer carrying a measured count.</summary>
    /// <param name="kind">The kind of resource counted.</param>
    /// <param name="used">How many the account owns; never negative.</param>
    /// <returns>The counted answer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="used"/> is negative.</exception>
    public static AccountResourceCount Counted(AccountResourceKind kind, int used)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(used);
        return new AccountResourceCount(kind, AccountResourceCountState.Counted, used);
    }

    /// <summary>An answer stating the count could not be taken.</summary>
    /// <param name="kind">The kind of resource that could not be counted.</param>
    /// <returns>The unavailable answer.</returns>
    public static AccountResourceCount Unavailable(AccountResourceKind kind)
    {
        return new AccountResourceCount(kind, AccountResourceCountState.Unavailable, 0);
    }
}
