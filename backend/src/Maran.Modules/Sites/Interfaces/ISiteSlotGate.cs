using Maran.Modules.Sites.Domain.Entities;

namespace Maran.Modules.Sites.Interfaces;

/// <summary>
/// The last word on whether an account may hold one more site, taken at the moment the row is written
/// rather than before the host is touched.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the plan limit is a statement about a ROW COUNT and no key can express one. A
/// unique index refuses a duplicate value — which is what stops two rows claiming one HOSTNAME,
/// because a hostname IS a value, though only in the database: the vhost a loser that got past the agent's
/// refusal has already written is the caller's to remove — while "at most N rows for this account" is not a
/// value, so the count and the insert are two steps and something has to make them one. That
/// something is this seam, and the reason it is a seam rather than four lines inside the handler is
/// that its only honest implementation speaks PostgreSQL — an advisory lock and an explicit
/// transaction — while most of the handler's tests run on a provider that has neither.
/// </para>
/// <para>
/// A caller reaches this only AFTER the agent has provisioned the site, so a refusal here means a
/// vhost exists on the host that no row will own: the caller compensates, unless a committed site
/// with the same primary domain owns that very file. That cost is why the handler
/// still checks the limit before calling the agent at all — the ordinary over-limit request is refused
/// without the host being touched, and this gate is what closes the narrow window in which two
/// requests both passed that check.
/// </para>
/// <para>
/// The shape is deliberately the one the Ftp module settled on rather than a second one. The two
/// allowances count different things, but the defect is the same defect and the argument for each
/// rejected alternative is the same argument, so a reader who has understood one has understood both.
/// </para>
/// </remarks>
public interface ISiteSlotGate
{
    /// <summary>Writes the row if, and only if, the account is still inside its allowance.</summary>
    /// <param name="site">The row to write, for a site the agent has already provisioned.</param>
    /// <param name="allowance">How many sites the account's plan allows in total.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>
    /// True when the row was written and committed; false when a concurrent creation had already
    /// taken the last slot, in which case nothing was written.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A database failure is not answered with false: it is thrown, and the caller tells the cases
    /// apart — a taken hostname (<c>SqlState 23505</c>) is a lost race for a name, anything else is a
    /// fault. Answering false for either would tell the customer their plan filled up when it had not,
    /// and would hide from the caller which of the two it has to answer and whether the vhost it made
    /// is still its own to remove.
    /// </para>
    /// <para>
    /// A <see cref="DbUpdateException"/> is the one throw that proves nothing was committed: it comes
    /// from the insert, inside the transaction, which is then rolled back, and the row and its claims
    /// are no longer tracked when it reaches the caller. A failure of the commit itself proves nothing
    /// either way — the server may have committed before the connection went — so it surfaces as
    /// whatever the provider threw, and a caller must not read it as "the row is not there".
    /// </para>
    /// </remarks>
    Task<bool> TryTakeAsync(Site site, int allowance, CancellationToken cancellationToken);
}
