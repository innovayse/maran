using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Commands.ReactivateAccount;

/// <summary>
/// Reactivating an account as an administrator (issue #493): an administrator lifts any suspension,
/// its own and a reseller's alike, which is the accepting side of the reseller's refusal.
/// </summary>
public sealed class ReactivateAccountAdministratorTests : IDisposable
{
    /// <summary>The reseller that owns the seeded accounts.</summary>
    private static readonly Guid Reseller = Guid.NewGuid();

    /// <summary>The shared fixture, running the handlers as an administrator.</summary>
    private readonly ResellerAccountLifecycleHarness _harness = new(FakeCurrentUser.Admin());

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _harness.Dispose();
    }

    /// <summary>An administrator reactivates the account it suspended itself, and the record of who suspended is cleared.</summary>
    /// <param name="suspendedBy">Who suspended the account that is reactivated.</param>
    [Theory]
    [InlineData(SuspensionActor.Administrator)]
    [InlineData(SuspensionActor.Reseller)]
    public async Task An_administrator_reactivates_a_suspension_whoever_placed_it(SuspensionActor suspendedBy)
    {
        await _harness.SeedResellerAsync(Reseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", Reseller, suspended: true, suspendedBy: suspendedBy);

        var result = await _harness.ReactivateAsync(account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        var stored = await _harness.StoredAccountAsync(account.Id);
        Assert.Equal(AccountStatus.Active, stored.Status);
        Assert.Null(stored.SuspendedBy);
    }

    /// <summary>An administrator's suspension is recorded as the administrator's.</summary>
    [Fact]
    public async Task An_administrators_suspension_is_recorded_as_the_administrators()
    {
        await _harness.SeedResellerAsync(Reseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", Reseller);

        var result = await _harness.SuspendAsync(account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(
            SuspensionActor.Administrator,
            (await _harness.StoredAccountAsync(account.Id)).SuspendedBy);
    }
}
