using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// A signed-in CUSTOMER, which is what every login endpoint in this module is actually reached by.
/// </summary>
/// <remarks>
/// It exists because the tenant query filter on <c>FtpUser</c> is only observable through a
/// non-administrator principal: <see cref="TestCurrentUser"/> is an <c>AdministratorCaller</c>, and the filter
/// short-circuits for an administrator, so a cross-tenant test written with it would pass whatever
/// the filter did. This is the principal that makes the filter measurable.
/// </remarks>
public sealed class CustomerCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Caller Caller { get; }

    /// <summary>Creates a customer principal owning one account.</summary>
    /// <param name="accountId">The account this customer owns, and the only one they may see.</param>
    public CustomerCurrentUser(Guid accountId)
    {
        Caller = new CustomerCaller(Guid.NewGuid(), "customer", accountId);
    }
}
