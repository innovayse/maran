using Maran.SharedKernel.Callers;
using Maran.SharedKernel.Interfaces;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// A signed-in reseller, so a module's query filter has a principal to close over. The reseller
/// counterpart of <see cref="AccountCustomer"/>: it speaks for one reseller and for no account at all.
/// </summary>
public sealed class ResellerCustomer : ICurrentUser
{
    /// <summary>The id of the one reseller login every instance speaks for.</summary>
    private static readonly Guid ResellerUserId = Guid.Parse("22222222-2222-4222-8222-222222222222");

    /// <inheritdoc />
    public Caller Caller { get; }

    /// <summary>Creates the principal.</summary>
    /// <param name="resellerId">The reseller this principal acts for.</param>
    public ResellerCustomer(Guid resellerId)
    {
        Caller = new ResellerCaller(ResellerUserId, "reseller", resellerId);
    }
}
