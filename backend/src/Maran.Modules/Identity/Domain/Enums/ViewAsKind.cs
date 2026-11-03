namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>
/// What an operator is viewing the panel as (issues #317, #314): the login that owns a hosting
/// account, or the login of a reseller. It is derived from the viewed login's role, a fact already
/// stored, so no session column holds a second copy of it.
/// </summary>
public enum ViewAsKind
{
    /// <summary>One hosting account, seen through the customer login that owns it.</summary>
    Account,

    /// <summary>One reseller, seen through the login created for it.</summary>
    Reseller,
}
