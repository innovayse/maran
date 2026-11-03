namespace Maran.SharedKernel.Callers;

/// <summary>
/// A signed-in reseller login: it owns a set of customer accounts and plans, and acts for the one
/// reseller its token names.
/// </summary>
/// <remarks>
/// It is a case of its own because a member exists only on the case that has it: a reseller has a
/// reseller id and no hosting account, so reusing <see cref="CustomerCaller"/> would hand it an
/// account id it does not have, and reusing <see cref="AdministratorCaller"/> would hand it the
/// whole server. A tenant query filter over customer data (sites, certificates, tasks) shows a
/// reseller no row, exactly as it shows <see cref="AccountlessCustomerCaller"/> none; a reseller
/// reaches a customer's resources only through a view-as session, which is a customer caller with
/// the reseller recorded in <see cref="PrincipalCaller.ViewedBy"/>. Only the Accounts module narrows
/// by <see cref="ResellerId"/>.
/// </remarks>
public sealed record ResellerCaller : PrincipalCaller
{
    /// <summary>The reseller this login acts for; the owner every Accounts query is narrowed to.</summary>
    public Guid ResellerId { get; }

    /// <summary>Creates a reseller caller.</summary>
    /// <param name="userId">The reseller login's panel user id.</param>
    /// <param name="username">The reseller login's name.</param>
    /// <param name="resellerId">The reseller the login acts for.</param>
    /// <param name="viewedBy">The operator viewing as this reseller, or null.</param>
    public ResellerCaller(Guid userId, string username, Guid resellerId, ViewingOperator? viewedBy = null)
        : base(userId, username, viewedBy)
    {
        ResellerId = resellerId;
    }
}
