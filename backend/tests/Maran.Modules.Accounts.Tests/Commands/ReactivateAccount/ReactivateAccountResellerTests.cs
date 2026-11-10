using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Commands.ReactivateAccount;

/// <summary>
/// Reactivating an account as a reseller: it reaches the reseller's own accounts and nothing else, a
/// suspended reseller's live token cannot write, and the journal names the reseller as the actor.
/// </summary>
public sealed class ReactivateAccountResellerTests : IDisposable
{
    /// <summary>The reseller the caller acts for.</summary>
    private static readonly Guid OwnReseller = Guid.NewGuid();

    /// <summary>Another reseller, whose accounts the caller must never reach.</summary>
    private static readonly Guid OtherReseller = Guid.NewGuid();

    /// <summary>The shared fixture, running the handlers as the reseller.</summary>
    private readonly ResellerAccountLifecycleHarness _harness =
        new(FakeCurrentUser.Reseller(OwnReseller, "acme-reseller"));

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _harness.Dispose();
    }

    /// <summary>A reseller reactivates a suspended account it owns, and the answer names the reseller.</summary>
    [Fact]
    public async Task A_reseller_reactivates_a_suspended_account_it_owns()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller, suspended: true);

        var result = await _harness.ReactivateAsync(account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(AccountStatus.Active, result.Value.Status);
        Assert.Equal("acme-reseller", result.Value.ResellerName);
        Assert.Equal(["unsuspend:alpha", "observe:alpha"], _harness.Agent.Calls);
    }

    /// <summary>An account another reseller owns is not found, exactly like an id that does not exist.</summary>
    [Fact]
    public async Task An_account_another_reseller_owns_is_not_found_exactly_like_a_random_id()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        await _harness.SeedResellerAsync(OtherReseller, "other");
        var foreign = await _harness.SeedAccountAsync("beta", OtherReseller, suspended: true);

        var foreignResult = await _harness.ReactivateAsync(foreign.Id);
        var missingResult = await _harness.ReactivateAsync(Guid.NewGuid());

        Assert.Equal("AccountNotFound", foreignResult.Error!.Code);
        Assert.Equal(missingResult.Error!.Code, foreignResult.Error.Code);
        Assert.Equal(missingResult.Error.Type, foreignResult.Error.Type);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Equal(
            AccountStatus.Suspended,
            (await _harness.StoredAccountAsync(foreign.Id)).Status);
    }

    /// <summary>An account the administrator owns is not found for a reseller.</summary>
    [Fact]
    public async Task An_account_the_administrator_owns_is_not_found_for_a_reseller()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var adminOwned = await _harness.SeedAccountAsync("gamma", resellerId: null, suspended: true);

        var result = await _harness.ReactivateAsync(adminOwned.Id);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
    }

    /// <summary>A suspended reseller's live token is refused a reactivation and nothing is touched.</summary>
    [Fact]
    public async Task A_suspended_reseller_is_refused_and_the_account_is_untouched()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller", suspended: true);
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller, suspended: true);

        var result = await _harness.ReactivateAsync(account.Id);

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Equal(
            AccountStatus.Suspended,
            (await _harness.StoredAccountAsync(account.Id)).Status);
    }

    /// <summary>A reseller whose row is gone is refused as not found, never allowed to write.</summary>
    [Fact]
    public async Task A_reseller_with_no_row_is_refused_as_not_found()
    {
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller, suspended: true);

        var result = await _harness.ReactivateAsync(account.Id);

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
    }

    /// <summary>A reseller's reactivation is journalled with the reseller as the actor, success and failure.</summary>
    [Fact]
    public async Task A_reseller_reactivation_is_journalled_with_the_reseller_as_actor()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller, suspended: true);

        await _harness.ReactivateAsync(account.Id);
        await _harness.ReactivateAsync(Guid.NewGuid());

        Assert.Equal(2, _harness.Audit.Entries.Count);
        var success = _harness.Audit.Entries[0];
        Assert.Equal("acme-reseller", success.ActorUsername);
        Assert.Equal("alpha", success.Subject);
        Assert.True(success.Succeeded);
        var failure = _harness.Audit.Entries[1];
        Assert.Equal("acme-reseller", failure.ActorUsername);
        Assert.False(failure.Succeeded);
    }

    /// <summary>
    /// A reseller cannot lift a suspension an administrator placed (issue #493): the account stays
    /// suspended, nothing on the host is touched, and the refusal is journaled as a failed reactivation.
    /// </summary>
    [Fact]
    public async Task A_reseller_cannot_reactivate_an_account_an_administrator_suspended()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync(
            "alpha", OwnReseller, suspended: true, suspendedBy: SuspensionActor.Administrator);

        var result = await _harness.ReactivateAsync(account.Id);

        Assert.Equal("AccountSuspendedByAdministrator", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Empty(_harness.Agent.Calls);
        var stored = await _harness.StoredAccountAsync(account.Id);
        Assert.Equal(AccountStatus.Suspended, stored.Status);
        Assert.Equal(SuspensionActor.Administrator, stored.SuspendedBy);
        var entry = Assert.Single(_harness.Audit.Entries);
        Assert.Equal("acme-reseller", entry.ActorUsername);
        Assert.Equal("alpha", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>
    /// A suspension whose origin was never recorded is read as an administrator's, because the other
    /// reading lets a reseller undo one.
    /// </summary>
    [Fact]
    public async Task A_suspension_with_no_recorded_origin_is_not_a_resellers_to_lift()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller, suspended: true);
        _harness.Seeder.Entry(account).Property(row => row.SuspendedBy).CurrentValue = null;
        await _harness.Seeder.SaveChangesAsync();

        var result = await _harness.ReactivateAsync(account.Id);

        Assert.Equal("AccountSuspendedByAdministrator", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
    }

    /// <summary>
    /// The accepting control of the refusal above: the same reseller lifts a suspension it placed
    /// itself, and the record of who suspended is cleared.
    /// </summary>
    [Fact]
    public async Task A_reseller_reactivates_an_account_it_suspended_itself_and_the_origin_is_cleared()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);
        Assert.True((await _harness.SuspendAsync(account.Id)).IsSuccess);
        Assert.Equal(
            SuspensionActor.Reseller,
            (await _harness.StoredAccountAsync(account.Id)).SuspendedBy);

        var result = await _harness.ReactivateAsync(account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        var stored = await _harness.StoredAccountAsync(account.Id);
        Assert.Equal(AccountStatus.Active, stored.Status);
        Assert.Null(stored.SuspendedBy);
    }

    /// <summary>A reseller reactivating an account that is not suspended is the idempotent no-op, not a refusal.</summary>
    [Fact]
    public async Task A_reseller_reactivating_an_active_account_is_not_refused()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);

        var result = await _harness.ReactivateAsync(account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
    }
}
