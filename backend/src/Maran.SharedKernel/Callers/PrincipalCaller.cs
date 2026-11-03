namespace Maran.SharedKernel.Callers;

/// <summary>
/// A caller that is a signed-in panel login: what an <see cref="AdministratorCaller"/>, a
/// <see cref="ResellerCaller"/>, a <see cref="CustomerCaller"/> and an
/// <see cref="AccountlessCustomerCaller"/> share.
/// </summary>
/// <remarks>
/// Every member describes the login being acted as, because that is whose authority a request runs
/// under — role, tenant scope, everything. <see cref="ViewedBy"/> is the one exception that names a
/// second person, and it exists so the audit journal stays truthful about who did it.
/// </remarks>
public abstract record PrincipalCaller : Caller
{
    /// <summary>The panel user's id.</summary>
    public Guid UserId { get; }

    /// <summary>The panel user's login name, as the audit journal records it.</summary>
    public string Username { get; }

    /// <summary>
    /// The operator viewing the panel as this login, or null when the login is acting as themselves.
    /// </summary>
    public ViewingOperator? ViewedBy { get; }

    /// <summary>Seals the hierarchy and records what every signed-in caller has.</summary>
    /// <param name="userId">The panel user's id.</param>
    /// <param name="username">The panel user's login name.</param>
    /// <param name="viewedBy">The operator viewing as this login, or null.</param>
    private protected PrincipalCaller(Guid userId, string username, ViewingOperator? viewedBy)
    {
        UserId = userId;
        Username = username;
        ViewedBy = viewedBy;
    }
}
