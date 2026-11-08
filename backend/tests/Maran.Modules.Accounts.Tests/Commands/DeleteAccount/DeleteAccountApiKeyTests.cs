using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.DeleteAccount;

/// <summary>
/// Deleting an account as a reseller's API key (issue #7): the same floor as the reseller's login —
/// only the reseller's own accounts, never once the reseller is suspended, and never without the final
/// backup, which a key cannot skip.
/// </summary>
public sealed class DeleteAccountApiKeyTests : IDisposable
{
    /// <summary>The reseller that owns the key.</summary>
    private static readonly Guid OwnReseller = Guid.NewGuid();

    /// <summary>Another reseller, whose accounts the key must never reach.</summary>
    private static readonly Guid OtherReseller = Guid.NewGuid();

    /// <summary>The shared fixture, running the handler as the key.</summary>
    private readonly ResellerAccountLifecycleHarness _harness = new(FakeCurrentUser.ApiKey(OwnReseller));

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _harness.Dispose();
    }

    /// <summary>A key deletes an account its reseller owns, after the final backup was taken.</summary>
    [Fact]
    public async Task A_key_deletes_an_account_its_reseller_owns_after_the_final_backup()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);
        var backups = new StubAccountBackupService();

        var result = await _harness.DeleteAsync(account.Id, backups: backups);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal((account.Id, "alpha"), Assert.Single(backups.Taken));
        Assert.Equal(["delete:alpha"], _harness.Agent.Calls);
        Assert.Empty(await _harness.Seeder.Accounts.ToListAsync());
    }

    /// <summary>A key cannot skip the final backup, and nothing is looked up or destroyed.</summary>
    [Fact]
    public async Task A_key_cannot_skip_the_final_backup()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);
        var backups = new StubAccountBackupService();

        var result = await _harness.DeleteAsync(account.Id, skipFinalBackup: true, backups: backups);

        Assert.Equal("AccountFinalBackupSkipForbidden", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Empty(backups.Taken);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Single(await _harness.Seeder.Accounts.ToListAsync());
        var entry = Assert.Single(_harness.Audit.Entries);
        Assert.Equal(AccountAuditActions.AccountDeleted, entry.Action);
        Assert.False(entry.Succeeded);
        Assert.StartsWith("api-key:", entry.ActorUsername, StringComparison.Ordinal);
    }

    /// <summary>An account another reseller owns is not found, exactly like an id that does not exist.</summary>
    [Fact]
    public async Task An_account_another_reseller_owns_is_not_found_exactly_like_a_random_id()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        await _harness.SeedResellerAsync(OtherReseller, "other");
        var foreign = await _harness.SeedAccountAsync("beta", OtherReseller);

        var foreignResult = await _harness.DeleteAsync(foreign.Id);
        var missingResult = await _harness.DeleteAsync(Guid.NewGuid());

        Assert.Equal("AccountNotFound", foreignResult.Error!.Code);
        Assert.Equal(missingResult.Error!.Code, foreignResult.Error.Code);
        Assert.Equal(missingResult.Error.Type, foreignResult.Error.Type);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Single(await _harness.Seeder.Accounts.ToListAsync());
    }

    /// <summary>A key of a suspended reseller is refused a deletion and the account survives.</summary>
    [Fact]
    public async Task A_key_of_a_suspended_reseller_is_refused_and_the_account_survives()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller", suspended: true);
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);

        var result = await _harness.DeleteAsync(account.Id);

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Single(await _harness.Seeder.Accounts.ToListAsync());
    }
}
