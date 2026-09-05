using Maran.Sdk.Contracts;
using Maran.Sdk.Events;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Asks the composed panel whether anything it stores is still keyed to an account, so a deletion
/// can report what it did rather than what it attempted.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The <see cref="AccountDeleting"/> cascade is announced to whoever
/// subscribed, and a module that subscribed to nothing is indistinguishable, from the publisher's
/// side, from a module that had nothing to release. That is not a hypothetical: an account deletion
/// reported COMPLETED at 100 percent while the Sites and Ssl modules — neither of which had a
/// subscriber — kept every row they held, and the panel went on rendering an ENABLED site for an
/// account it no longer had. The step said "asking every module to release what it holds", and it
/// was asking two of them.
/// </para>
/// <para>
/// <b>What it changes about completion.</b> With this, a deletion completes on OBSERVED absence
/// rather than on the absence of an exception. A module that keeps rows through the cascade now
/// stops the deletion at the recoverable point — before the system user is removed — instead of
/// producing a green task and an orphan.
/// </para>
/// <para>
/// <b>It is not the cascade's replacement.</b> It reads the panel's own database and nothing else,
/// so what is on the HOST — a vhost, a crontab, key material — is outside what it can see. And it
/// finds only what a module MAPPED: a module that stores a customer's rows somewhere this panel's
/// model does not describe is invisible to it, which is the blind spot
/// <c>AccountCascadeTests</c> covers at build time instead.
/// </para>
/// <para>
/// <b>It reports its own blind spot rather than hiding inside it.</b> An implementation that could
/// not read a module answers with that module in <see cref="AccountResidue.Unchecked"/>, so a caller
/// never mistakes "not asked" for "nothing to release" — the exact substitution that produced a
/// COMPLETED task over two modules that kept everything.
/// </para>
/// </remarks>
public interface IAccountResidueAuditor
{
    /// <summary>Names everything still stored against an account after the cascade has run.</summary>
    /// <param name="accountId">The account being deleted.</param>
    /// <param name="cancellationToken">Cancels the scan.</param>
    /// <returns>
    /// What still holds rows and what could not be read, both named for the operator's log and the
    /// task's error, never for a customer-facing message.
    /// </returns>
    Task<AccountResidue> FindResidueAsync(Guid accountId, CancellationToken cancellationToken);
}
