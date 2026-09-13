using Maran.SharedKernel.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A signed-in customer who owns one account, so a module's tenant query filter has a principal to
/// close over.
/// </summary>
public sealed class AccountCustomer : ICurrentUser
{
    /// <summary>Creates the principal.</summary>
    /// <param name="accountId">The account this customer owns.</param>
    public AccountCustomer(Guid accountId)
    {
        AccountId = accountId;
    }

    /// <inheritdoc />
    public Guid UserId { get; } = Guid.Parse("11111111-1111-4111-8111-111111111111");

    /// <inheritdoc />
    public string Username
    {
        get
        {
            return "customer";
        }
    }

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
