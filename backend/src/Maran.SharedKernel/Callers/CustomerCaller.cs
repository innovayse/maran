namespace Maran.SharedKernel.Callers;

/// <summary>A signed-in customer, acting for the one hosting account they own or were added to.</summary>
public sealed record CustomerCaller : PrincipalCaller
{
    /// <summary>The hosting account this customer acts for; the tenant every query is scoped to.</summary>
    public Guid AccountId { get; }

    /// <summary>Creates a customer caller.</summary>
    /// <param name="userId">The customer's panel user id.</param>
    /// <param name="username">The customer's login name.</param>
    /// <param name="accountId">The hosting account the customer acts for.</param>
    /// <param name="viewedBy">The operator viewing as this customer, or null.</param>
    public CustomerCaller(Guid userId, string username, Guid accountId, ViewingOperator? viewedBy = null)
        : base(userId, username, viewedBy)
    {
        AccountId = accountId;
    }
}
