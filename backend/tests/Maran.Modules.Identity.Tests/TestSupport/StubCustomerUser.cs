using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>A signed-in customer acting for one account, so the sub-user operations have a principal to read.</summary>
public sealed class StubCustomerUser : ICurrentUser
{
    /// <summary>Creates the principal.</summary>
    /// <param name="userId">The login's id.</param>
    /// <param name="username">The login name.</param>
    /// <param name="accountId">The account the sign-in is acting for.</param>
    public StubCustomerUser(Guid userId, string username, Guid? accountId)
    {
        UserId = userId;
        Username = username;
        AccountId = accountId;
    }

    /// <inheritdoc />
    public Guid UserId { get; }

    /// <inheritdoc />
    public string Username { get; }

    /// <inheritdoc />
    public Guid? AccountId { get; }

    /// <inheritdoc />
    public bool IsAdmin
    {
        get
        {
            return false;
        }
    }
}
