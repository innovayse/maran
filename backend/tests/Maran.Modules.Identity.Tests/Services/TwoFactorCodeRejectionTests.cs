using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>What a refused two-factor code is answered with, with and without a clock warning (issue #150).</summary>
public sealed class TwoFactorCodeRejectionTests
{
    /// <summary>With the clock synchronised, a refused code is the plain refusal.</summary>
    [Fact]
    public async Task With_the_clock_synchronised_a_refused_code_is_the_plain_refusal()
    {
        var rejection = new TwoFactorCodeRejection(new StubClockSyncStatus());

        var error = await rejection.ErrorAsync(CancellationToken.None);

        Assert.Equal("InvalidTwoFactorCodeUnauthorized", error.Code);
        Assert.Equal(ErrorType.Unauthorized, error.Type);
    }

    /// <summary>With the clock not synchronised, a refused code says so and is still unauthorized.</summary>
    [Fact]
    public async Task With_the_clock_not_synchronised_a_refused_code_says_so_and_is_still_unauthorized()
    {
        var rejection = new TwoFactorCodeRejection(new StubClockSyncStatus { Unsynchronized = true });

        var error = await rejection.ErrorAsync(CancellationToken.None);

        Assert.Equal("InvalidTwoFactorCodeClockUnsynchronizedUnauthorized", error.Code);
        Assert.Equal(ErrorType.Unauthorized, error.Type);
    }
}
