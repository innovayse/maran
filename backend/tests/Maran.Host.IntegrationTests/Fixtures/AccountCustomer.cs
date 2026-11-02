using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A signed-in customer who owns one account, so a module's tenant query filter has a principal to
/// close over.
/// </summary>
public sealed class AccountCustomer : ICurrentUser
{
    /// <summary>The id of the one customer login every instance speaks for.</summary>
    private static readonly Guid CustomerUserId = Guid.Parse("11111111-1111-4111-8111-111111111111");

    /// <inheritdoc />
    public Caller Caller { get; }

    /// <summary>Creates the principal.</summary>
    /// <param name="accountId">The account this customer owns.</param>
    public AccountCustomer(Guid accountId)
    {
        Caller = new CustomerCaller(CustomerUserId, "customer", accountId);
    }
}
