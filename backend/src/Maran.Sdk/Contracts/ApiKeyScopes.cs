namespace Maran.Sdk.Contracts;

/// <summary>
/// The closed vocabulary of scopes an API key can hold. A scope bounds what a key may reach; nothing
/// outside this list is granted, and a stored scope this build does not know grants nothing.
/// </summary>
public static class ApiKeyScopes
{
    /// <summary>Reads the key's own description. Implicit: every valid key holds it.</summary>
    public const string SelfRead = "self.read";

    /// <summary>Reads server-wide administrator data that belongs to no tenant.</summary>
    public const string ServerRead = "server.read";

    /// <summary>Reads the plans a reseller owns. Reseller-grade: only a key a reseller owns may hold it.</summary>
    public const string PlansRead = "plans.read";

    /// <summary>Reads the accounts a reseller owns. Reseller-grade: only a key a reseller owns may hold it.</summary>
    public const string AccountsRead = "accounts.read";

    /// <summary>
    /// Creates accounts and changes the ones a reseller owns: create, suspend, reactivate and change plan.
    /// Reseller-grade: only a key a reseller owns may hold it. Every operation it reaches can be undone.
    /// </summary>
    public const string AccountsWrite = "accounts.write";

    /// <summary>
    /// Terminates (deletes) an account a reseller owns. Reseller-grade: only a key a reseller owns may hold it.
    /// A scope of its own because it is the one operation that cannot be undone: it is neither implied by
    /// <see cref="AccountsWrite"/> nor does it imply it, so a key made only to provision cannot destroy data.
    /// </summary>
    public const string AccountsDelete = "accounts.delete";

    /// <summary>Every scope a key can hold, in the order the panel lists them.</summary>
    public static readonly IReadOnlyList<string> All =
        [SelfRead, ServerRead, PlansRead, AccountsRead, AccountsWrite, AccountsDelete];

    /// <summary>
    /// The scopes an administrator-owned key may be granted: the implicit <see cref="SelfRead"/> and
    /// the administrator-grade ones.
    /// </summary>
    public static readonly IReadOnlyList<string> AdministratorGrantable = [SelfRead, ServerRead];

    /// <summary>
    /// The scopes a reseller-owned key may be granted: the implicit <see cref="SelfRead"/> and the
    /// reseller-grade ones. Apart from <see cref="SelfRead"/> it shares no scope with
    /// <see cref="AdministratorGrantable"/>, so no data scope is grantable to both kinds of owner.
    /// </summary>
    public static readonly IReadOnlyList<string> ResellerGrantable =
        [SelfRead, PlansRead, AccountsRead, AccountsWrite, AccountsDelete];

    /// <summary>
    /// The scopes an owner may grant: the reseller-grade ones for a reseller-owned key, the
    /// administrator-grade ones for an administrator-owned key.
    /// </summary>
    /// <param name="ownerResellerId">The reseller that owns the key, or <c>null</c> for the administrator.</param>
    /// <returns>The grantable scopes, in the order the panel lists them.</returns>
    public static IReadOnlyList<string> GrantableTo(Guid? ownerResellerId)
    {
        return ownerResellerId is null ? AdministratorGrantable : ResellerGrantable;
    }
}
