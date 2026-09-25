namespace Maran.Sdk.Contracts;

/// <summary>Who a module's screens are for, as the module itself declares it.</summary>
/// <remarks>
/// This is what keeps the customer's navigation an answer given by the panel rather than a list the
/// SPA maintains. A module — including one bought on the marketplace, which the open code does not
/// know exists — states its own audience, and the catalogue endpoint returns to each caller only
/// what is addressed to them. The alternative, a role check in the SPA, is a second copy of an
/// authorization decision: it would go stale the first time a module was added and it would be
/// wrong in the direction that hides working screens.
/// </remarks>
public enum ModuleAudience
{
    /// <summary>Every signed-in user, each seeing their own resources through the tenant filters.</summary>
    Everyone,

    /// <summary>Administrators only: the module governs the server rather than an account's resources.</summary>
    AdministratorOnly,
}
