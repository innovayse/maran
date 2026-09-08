namespace Maran.Sdk.Interfaces;

/// <summary>
/// Names the databases the panel knows an account owns, so an operation that acts on all of them
/// can be bounded by the panel's own record rather than by what it finds on the server.
/// </summary>
/// <remarks>
/// <para>
/// The contract lives in the Sdk and its implementation in the module that owns the databases
/// table, the same shape <see cref="IAccountDirectory"/> established — a module may never reference
/// another module (rules/architecture.md "Backend: modular monolith").
/// </para>
/// <para>
/// <b>Read-only, and NAMES ONLY.</b> It answers with the fully-qualified database names and nothing
/// else — no identifiers, no owning user, no creation date, and above all no credential, because
/// the owning module deliberately stores none. A window returns the narrowest value that answers
/// the question, and the question here is "which databases may this operation touch".
/// </para>
/// <para>
/// <b>Why the panel's list and not the server's.</b> Its one caller today is restore, which hands
/// the list to the agent as the set of databases a restore is ALLOWED to replace. A database in an
/// archive's manifest that this list does not name is refused by the agent and never created —
/// because creating it would resurrect a database the panel has forgotten, with a user nothing
/// points at, which is exactly the orphan the account-deletion cascade exists to prevent. Asking the
/// SERVER instead would defeat that: the server's answer includes the orphans.
/// </para>
/// </remarks>
public interface IAccountDatabaseDirectory
{
    /// <summary>Lists the fully-qualified names of one account's databases.</summary>
    /// <param name="accountId">The account whose databases are listed.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>
    /// The names, in no guaranteed order. Empty when the account has no databases — and empty is a
    /// real answer, never "you may not ask": a caller must be able to tell an account with nothing
    /// to restore apart from a refusal, and a refusal is not something this method can express.
    /// </returns>
    /// <remarks>
    /// <b>Tenant semantics, stated because a cross-module window is where they get lost.</b> This
    /// method applies the CALLER'S tenant scope, exactly as <see cref="IAccountDirectory.FindAsync"/>
    /// does: a customer asking about another account's databases is answered with an empty list, not
    /// with that account's names. Empty is therefore ambiguous between "no databases" and "not
    /// yours", and that ambiguity is deliberate — telling them apart would confirm the existence of
    /// another tenant's data (rules/security.md — 404, never 403). A caller that must distinguish
    /// the two authorises the ACCOUNT first, through <see cref="IAccountDirectory.FindAsync"/>,
    /// which answers <c>null</c> for an account the caller may not see; today's one caller does
    /// exactly that before it asks this.
    /// </remarks>
    Task<IReadOnlyList<string>> ListNamesAsync(Guid accountId, CancellationToken cancellationToken);
}
