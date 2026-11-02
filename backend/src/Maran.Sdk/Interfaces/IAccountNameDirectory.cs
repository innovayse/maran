namespace Maran.Sdk.Interfaces;

/// <summary>
/// Reads the display names of hosting accounts a login holds a membership of, whichever of them the
/// current session happens to be acting for. The window behind the header's account selector.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is not <see cref="IAccountDirectory.NamesOfAsync"/>.</b> That method applies the
/// caller's tenant scope, so a customer acting for account A learns nothing about account B — which
/// is correct for every tenant query and exactly wrong for the list of accounts a person may switch
/// between. The two answer different questions and are kept apart so the scoped one never has to be
/// loosened.
/// </para>
/// <para>
/// <b>It applies NO tenant scope, by contract, and the whole authorization burden sits on the
/// caller.</b> Handing it an id the current login does not hold a membership of would disclose that
/// account's name. The only caller (the Identity module's account list) passes ids it has just read
/// from the login's own membership rows, never ids that came from a request. An id naming no account
/// is simply absent from the answer.
/// </para>
/// <para>
/// It exposes names and nothing else — no plan, no system user name, no owner address — because the
/// selector needs a label and every further field would be one more thing a mistaken caller could
/// disclose (rules/architecture.md "A window exposes the narrowest value that answers the question").
/// </para>
/// </remarks>
public interface IAccountNameDirectory
{
    /// <summary>Reads the names of several accounts, applying no tenant scope.</summary>
    /// <param name="accountIds">
    /// The accounts whose names to read; duplicates are harmless. Every id MUST come from the current
    /// login's own memberships.
    /// </param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Account id to account name, for every requested account that exists.</returns>
    Task<IReadOnlyDictionary<Guid, string>> NamesOfAsync(
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken);
}
