namespace Maran.SharedKernel.Callers;

/// <summary>A signed-in server administrator. Owns no account: an administrator's scope is the whole server.</summary>
public sealed record AdministratorCaller : PrincipalCaller
{
    /// <summary>Creates an administrator caller.</summary>
    /// <param name="userId">The administrator's panel user id.</param>
    /// <param name="username">The administrator's login name.</param>
    /// <param name="viewedBy">The operator viewing as this administrator, or null.</param>
    public AdministratorCaller(Guid userId, string username, ViewingOperator? viewedBy = null)
        : base(userId, username, viewedBy)
    {
    }
}
