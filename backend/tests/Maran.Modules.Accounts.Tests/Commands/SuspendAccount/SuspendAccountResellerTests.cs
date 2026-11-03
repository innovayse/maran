using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.SuspendAccount;

/// <summary>
/// Suspending an account as a reseller: it reaches the reseller's own accounts and nothing else, a
/// suspended reseller's live token cannot write, and the journal names the reseller as the actor.
/// </summary>
public sealed class SuspendAccountResellerTests : IDisposable
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

    /// <summary>A reseller suspends an account it owns, and the answer names the reseller.</summary>
    [Fact]
    public async Task A_reseller_suspends_an_account_it_owns()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);

        var result = await _harness.SuspendAsync(account.Id);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(AccountStatus.Suspended, result.Value.Status);
        Assert.Equal("acme-reseller", result.Value.ResellerName);
        Assert.Equal(["suspend:alpha", "observe:alpha"], _harness.Agent.Calls);
    }

    /// <summary>An account another reseller owns is not found, exactly like an id that does not exist.</summary>
    [Fact]
    public async Task An_account_another_reseller_owns_is_not_found_exactly_like_a_random_id()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        await _harness.SeedResellerAsync(OtherReseller, "other");
        var foreign = await _harness.SeedAccountAsync("beta", OtherReseller);

        var foreignResult = await _harness.SuspendAsync(foreign.Id);
        var missingResult = await _harness.SuspendAsync(Guid.NewGuid());

        Assert.Equal("AccountNotFound", foreignResult.Error!.Code);
        Assert.Equal(missingResult.Error!.Code, foreignResult.Error.Code);
        Assert.Equal(missingResult.Error.Type, foreignResult.Error.Type);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Equal(
            AccountStatus.Active,
            (await _harness.Seeder.Accounts.SingleAsync(row => row.Id == foreign.Id)).Status);
    }

    /// <summary>An account the administrator owns is not found for a reseller.</summary>
    [Fact]
    public async Task An_account_the_administrator_owns_is_not_found_for_a_reseller()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var adminOwned = await _harness.SeedAccountAsync("gamma", resellerId: null);

        var result = await _harness.SuspendAsync(adminOwned.Id);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
    }

    /// <summary>A suspended reseller's live token is refused a suspension and nothing is touched.</summary>
    [Fact]
    public async Task A_suspended_reseller_is_refused_and_the_account_is_untouched()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller", suspended: true);
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);

        var result = await _harness.SuspendAsync(account.Id);

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Equal(
            AccountStatus.Active,
            (await _harness.Seeder.Accounts.SingleAsync(row => row.Id == account.Id)).Status);
    }

    /// <summary>A reseller whose row is gone is refused as not found, never allowed to write.</summary>
    [Fact]
    public async Task A_reseller_with_no_row_is_refused_as_not_found()
    {
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);

        var result = await _harness.SuspendAsync(account.Id);

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
    }

    /// <summary>A reseller's suspension is journalled with the reseller as the actor, success and failure.</summary>
    [Fact]
    public async Task A_reseller_suspension_is_journalled_with_the_reseller_as_actor()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);

        await _harness.SuspendAsync(account.Id);
        await _harness.SuspendAsync(Guid.NewGuid());

        Assert.Equal(2, _harness.Audit.Entries.Count);
        var success = _harness.Audit.Entries[0];
        Assert.Equal("acme-reseller", success.ActorUsername);
        Assert.Equal("alpha", success.Subject);
        Assert.True(success.Succeeded);
        var failure = _harness.Audit.Entries[1];
        Assert.Equal("acme-reseller", failure.ActorUsername);
        Assert.False(failure.Succeeded);
    }
}
