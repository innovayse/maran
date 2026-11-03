using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.DeleteAccount;

/// <summary>
/// Deleting an account as a reseller: only its own accounts, never a suspended reseller's, and never
/// without the final backup, which only an administrator may skip.
/// </summary>
public sealed class DeleteAccountResellerTests : IDisposable
{
    /// <summary>The reseller the caller acts for.</summary>
    private static readonly Guid OwnReseller = Guid.NewGuid();

    /// <summary>Another reseller, whose accounts the caller must never reach.</summary>
    private static readonly Guid OtherReseller = Guid.NewGuid();

    /// <summary>The shared fixture, running the handler as the reseller.</summary>
    private readonly ResellerAccountLifecycleHarness _harness =
        new(FakeCurrentUser.Reseller(OwnReseller, "acme-reseller"));

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _harness.Dispose();
    }

    /// <summary>A reseller deletes an account it owns, after the final backup was taken.</summary>
    [Fact]
    public async Task A_reseller_deletes_an_account_it_owns_after_the_final_backup()
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

    /// <summary>A reseller cannot skip the final backup, and nothing is looked up or destroyed.</summary>
    [Fact]
    public async Task A_reseller_cannot_skip_the_final_backup()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);
        var backups = new StubAccountBackupService();

        var result = await _harness.DeleteAsync(account.Id, skipFinalBackup: true, backups: backups);

        Assert.Equal("AccountFinalBackupSkipForbidden", result.Error!.Code);
        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Empty(backups.Taken);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Empty(_harness.Tasks.Tasks);
        Assert.Single(await _harness.Seeder.Accounts.ToListAsync());
        var entry = Assert.Single(_harness.Audit.Entries);
        Assert.Equal(AccountAuditActions.AccountDeleted, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A reseller cannot skip the final backup even when no Backups module is installed.</summary>
    [Fact]
    public async Task A_reseller_cannot_skip_the_final_backup_even_with_no_backups_module()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);

        var result = await _harness.DeleteAsync(account.Id, skipFinalBackup: true);

        Assert.Equal("AccountFinalBackupSkipForbidden", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
    }

    /// <summary>The inverse control: an administrator still may skip the final backup.</summary>
    [Fact]
    public async Task An_administrator_may_still_skip_the_final_backup()
    {
        using var administrator = new ResellerAccountLifecycleHarness(FakeCurrentUser.Admin());
        var account = await administrator.SeedAccountAsync("alpha", resellerId: null);
        var backups = StubAccountBackupService.Failing();

        var result = await administrator.DeleteAsync(account.Id, skipFinalBackup: true, backups: backups);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Empty(backups.Taken);
        Assert.Contains(administrator.Audit.Entries, entry =>
        {
            return entry.Action == AccountAuditActions.FinalBackupSkipped && entry.Succeeded;
        });
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

    /// <summary>A suspended reseller is refused a deletion and the account survives.</summary>
    [Fact]
    public async Task A_suspended_reseller_is_refused_and_the_account_survives()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller", suspended: true);
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);

        var result = await _harness.DeleteAsync(account.Id);

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Empty(_harness.Agent.Calls);
        Assert.Single(await _harness.Seeder.Accounts.ToListAsync());
    }

    /// <summary>A reseller's deletion is journalled with the reseller as the actor, success and failure.</summary>
    [Fact]
    public async Task A_reseller_deletion_is_journalled_with_the_reseller_as_actor()
    {
        await _harness.SeedResellerAsync(OwnReseller, "acme-reseller");
        var account = await _harness.SeedAccountAsync("alpha", OwnReseller);

        await _harness.DeleteAsync(account.Id);
        await _harness.DeleteAsync(Guid.NewGuid());

        var deletions = _harness.Audit.Entries
            .Where(entry =>
            {
                return entry.Action == AccountAuditActions.AccountDeleted;
            })
            .ToList();
        Assert.Equal(2, deletions.Count);
        Assert.All(deletions, entry =>
        {
            Assert.Equal("acme-reseller", entry.ActorUsername);
        });
        Assert.True(deletions[0].Succeeded);
        Assert.Equal("alpha", deletions[0].Subject);
        Assert.False(deletions[1].Succeeded);
    }
}
