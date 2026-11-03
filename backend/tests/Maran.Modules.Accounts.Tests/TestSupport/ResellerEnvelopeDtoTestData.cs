using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// The wire envelope the reseller tests send, built so a test states only the one figure it is about
/// with a <c>with</c> expression over a value that is known to be valid.
/// </summary>
public static class ResellerEnvelopeDtoTestData
{
    /// <summary>A valid envelope: generous counts, every feature, no runtime ceiling.</summary>
    /// <returns>The wire envelope.</returns>
    public static ResellerEnvelopeDto Valid()
    {
        return ResellerMapper.ToEnvelopeDto(ResellerTestData.Envelope(features: AccountFeature.All));
    }

    /// <summary>A valid envelope that sells only <paramref name="maxAccounts"/> accounts and a small pool.</summary>
    /// <param name="maxAccounts">The most accounts the reseller may own.</param>
    /// <param name="diskPoolMb">The disk pool, in megabytes.</param>
    /// <param name="maxPlans">The most plans the reseller may own.</param>
    /// <returns>The wire envelope.</returns>
    public static ResellerEnvelopeDto Small(int maxAccounts, long diskPoolMb, int maxPlans)
    {
        return Valid() with { MaxAccounts = maxAccounts, DiskPoolMb = diskPoolMb, MaxPlans = maxPlans };
    }
}
