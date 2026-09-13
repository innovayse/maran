using Maran.Modules.Sites.Domain.Entities;

namespace Maran.Modules.Sites.Interfaces;

/// <summary>
/// The last word on whether an account may hold one more site, taken at the moment the row is written
/// rather than before the host is touched.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the plan limit is a statement about a ROW COUNT and no key can express one. A
/// unique index refuses a duplicate value — which is what closes the equivalent race for a DOMAIN,
/// because a domain IS a value — while "at most N rows for this account" is not a value, so the count
/// and the insert are two steps and something has to make them one. That something is this seam, and
/// the reason it is a seam rather than four lines inside the handler is that its only honest
/// implementation speaks PostgreSQL — an advisory lock and an explicit transaction — while most of the
/// handler's tests run on a provider that has neither.
/// </para>
/// <para>
/// A caller reaches this only AFTER the agent has provisioned the site, so a refusal here means a
/// vhost exists on the host that no row will own: the caller compensates. That cost is why the handler
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
    /// A database failure is not answered with false: it is thrown, and reaches the caller exactly as
    /// it does today. Answering false for a lost connection or a duplicate domain would make the
    /// caller delete a vhost for a reason it had not established, and would tell the customer their
    /// plan filled up when it had not.
    /// </remarks>
    Task<bool> TryTakeAsync(Site site, int allowance, CancellationToken cancellationToken);
}
