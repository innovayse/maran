using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>A signed-in customer acting for one account, so the sub-user operations have a principal to read.</summary>
public sealed class StubCustomerUser : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; }

    /// <summary>Creates the principal.</summary>
    /// <param name="userId">The login's id.</param>
    /// <param name="username">The login name.</param>
    /// <param name="accountId">
    /// The account the sign-in is acting for; null for a login acting for none, which owns nothing.
    /// </param>
    public StubCustomerUser(Guid userId, string username, Guid? accountId)
    {
        Caller = accountId is { } account
            ? new CustomerCaller(userId, username, account)
            : new AccountlessCustomerCaller(userId, username);
    }
}
