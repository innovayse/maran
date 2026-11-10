using Maran.Modules.ServerMigration.Domain.Entities;
using Maran.Modules.ServerMigration.Domain.Enums;

namespace Maran.Modules.ServerMigration.Tests.Domain.Entities;

/// <summary>The two-phase state machine of one account of a migration job (issue #197, phase 2).</summary>
public sealed class MigrationJobAccountTests
{
    /// <summary>The moment every case runs at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A pending, unmapped account.</summary>
    /// <returns>The account.</returns>
    private static MigrationJobAccount NewAccount()
    {
        return new MigrationJobAccount(Guid.NewGuid(), Guid.NewGuid(), "alice", "/home/alice", 1024, 0, 0, "{}", "alice.example.test");
    }

    /// <summary>An account is mapped only when the plan, the owner and the domain are all chosen.</summary>
    [Fact]
    public void An_account_is_mapped_only_with_a_plan_an_owner_and_a_domain()
    {
        var account = NewAccount();
        Assert.False(account.IsMapped);

        account.Remap(Guid.NewGuid(), null, false, "alice@example.test", null, null, null);

        Assert.True(account.IsMapped);
    }

    /// <summary>A field left out of a remap keeps what the row held.</summary>
    [Fact]
    public void A_remap_keeps_the_fields_it_does_not_name()
    {
        var account = NewAccount();
        var plan = Guid.NewGuid();
        account.Remap(plan, null, false, "alice@example.test", "ru", null, null);

        account.Remap(null, null, false, null, null, "new.example.test", null);

        Assert.Equal(plan, account.PlanId);
        Assert.Equal("alice@example.test", account.OwnerEmail);
        Assert.Equal("ru", account.OwnerLanguage);
        Assert.Equal("new.example.test", account.PrimaryDomain);
    }

    /// <summary>The cut-over starts only from a synced account.</summary>
    [Fact]
    public void The_cut_over_needs_a_synced_account()
    {
        var account = NewAccount();
        Assert.False(account.BeginCutOver().IsSuccess);

        account.BeginRun(Now);
        Assert.False(account.BeginCutOver().IsSuccess);

        account.Synced(Now);
        Assert.True(account.BeginCutOver().IsSuccess);
        Assert.Equal(MigrationAccountState.CuttingOver, account.State);
    }

    /// <summary>A completed account holds its report and can no longer be retried.</summary>
    [Fact]
    public void A_completed_account_keeps_its_report_and_is_not_retried()
    {
        var account = NewAccount();
        account.BeginRun(Now);
        account.Synced(Now);
        account.BeginCutOver();
        account.Complete("{}", Now.AddHours(1));

        Assert.Equal(MigrationAccountState.Completed, account.State);
        Assert.Equal("{}", account.ReportJson);
        Assert.True(account.HasEnded);
        Assert.False(account.Retry().IsSuccess);
    }

    /// <summary>A rolled-back account starts over; a failed cut-over returns to synced and keeps its account.</summary>
    [Fact]
    public void Retry_starts_a_rolled_back_account_over_and_a_failed_cut_over_from_synced()
    {
        var rolledBack = NewAccount();
        rolledBack.BeginRun(Now);
        rolledBack.AccountCreated(Guid.NewGuid());
        rolledBack.PullProgressed(5);
        rolledBack.RollBack("MigrationPullFailed", Now);
        Assert.Null(rolledBack.TargetAccountId);

        Assert.True(rolledBack.Retry().IsSuccess);
        Assert.Equal(MigrationAccountState.Pending, rolledBack.State);
        Assert.Equal(0, rolledBack.SlicesCompleted);

        var failed = NewAccount();
        var created = Guid.NewGuid();
        failed.BeginRun(Now);
        failed.AccountCreated(created);
        failed.Synced(Now);
        failed.BeginCutOver();
        failed.FailedAfterSync("MigrationPullFailed", Now);
        Assert.True(failed.Retry().IsSuccess);
        Assert.Equal(MigrationAccountState.Synced, failed.State);
        Assert.Equal(created, failed.TargetAccountId);
    }

    /// <summary>A pending account cannot be retried: there is nothing that failed.</summary>
    [Fact]
    public void A_pending_account_is_not_retried()
    {
        Assert.False(NewAccount().Retry().IsSuccess);
    }

    /// <summary>Resuming a paused account that had synced returns it to synced, not to the start.</summary>
    [Fact]
    public void A_paused_account_that_had_synced_returns_to_synced()
    {
        var synced = NewAccount();
        synced.BeginRun(Now);
        synced.Synced(Now);
        synced.Paused();
        synced.Requeue();
        Assert.Equal(MigrationAccountState.Synced, synced.State);

        var fresh = NewAccount();
        fresh.BeginRun(Now);
        fresh.Paused();
        fresh.Requeue();
        Assert.Equal(MigrationAccountState.Pending, fresh.State);
    }

    /// <summary>The generated credentials are handed over once and then forgotten.</summary>
    [Fact]
    public void Issued_secrets_are_claimed_once()
    {
        var account = NewAccount();
        account.HoldIssuedSecrets("[{\"subject\":\"db\",\"secret\":\"x\"}]");

        Assert.NotNull(account.ClaimIssuedSecrets());
        Assert.Null(account.ClaimIssuedSecrets());
    }

    /// <summary>Deleting the half-created account forgets it and the provisioned mark.</summary>
    [Fact]
    public void Deleting_the_half_created_account_clears_the_mark()
    {
        var account = NewAccount();
        account.AccountCreated(Guid.NewGuid());
        account.Provision();

        account.AccountDeleted();

        Assert.Null(account.TargetAccountId);
        Assert.False(account.Provisioned);
    }
}
