using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Tests.TestSupport;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>Behavioral contract of <see cref="Account"/>.</summary>
public sealed class AccountTests
{
    /// <summary>A fixed instant used wherever a test needs "some" creation time.</summary>
    private static readonly DateTimeOffset SomeInstant = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Creating account sets every field from the constructor arguments.</summary>
    [Fact]
    public void Creating_account_sets_every_field_from_the_constructor_arguments()
    {
        var id = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero));

        var account = new Account(id, "acme", "acme.example.com", planId, clock.UtcNow);

        Assert.Equal(id, account.Id);
        Assert.Equal("acme", account.Name);
        Assert.Equal("acme.example.com", account.PrimaryDomain);
        Assert.Equal(planId, account.PlanId);
    }

    /// <summary>Creating account starts it in the active status.</summary>
    [Fact]
    public void Creating_account_starts_it_in_the_active_status()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);

        Assert.Equal(AccountStatus.Active, account.Status);
    }

    /// <summary>Creating account stamps created at from the injected clock not real time.</summary>
    [Fact]
    public void Creating_account_stamps_created_at_from_the_injected_clock_not_real_time()
    {
        // A clearly-not-real instant: if CreatedAt ever silently switched to the ambient clock,
        // this assertion would fail no matter when the test actually runs.
        var fixedInstant = new DateTimeOffset(2001, 9, 9, 1, 46, 40, TimeSpan.Zero);
        var clock = new FakeClock(fixedInstant);

        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), clock.UtcNow);

        Assert.Equal(fixedInstant, account.CreatedAt);
        Assert.NotEqual(fixedInstant.AddYears(20).Date, account.CreatedAt.Date);
    }

    /// <summary>Suspending an active account moves it to suspended.</summary>
    [Fact]
    public void Suspending_an_active_account_moves_it_to_suspended()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);

        account.Suspend(SuspensionActor.Administrator);

        Assert.Equal(AccountStatus.Suspended, account.Status);
    }

    /// <summary>Suspending an already suspended account is a harmless no op.</summary>
    [Fact]
    public void Suspending_an_already_suspended_account_is_a_harmless_no_op()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);
        account.Suspend(SuspensionActor.Administrator);

        account.Suspend(SuspensionActor.Administrator);

        Assert.Equal(AccountStatus.Suspended, account.Status);
    }

    /// <summary>Reactivating a suspended account moves it to active.</summary>
    [Fact]
    public void Reactivating_a_suspended_account_moves_it_to_active()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);
        account.Suspend(SuspensionActor.Administrator);

        account.Reactivate();

        Assert.Equal(AccountStatus.Active, account.Status);
    }

    /// <summary>Reactivating an already active account is a harmless no op.</summary>
    [Fact]
    public void Reactivating_an_already_active_account_is_a_harmless_no_op()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);

        account.Reactivate();

        Assert.Equal(AccountStatus.Active, account.Status);
    }

    /// <summary>Transferring an account sets its owner and its plan together.</summary>
    [Fact]
    public void Transferring_an_account_sets_its_owner_and_its_plan_together()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);
        var reseller = Guid.NewGuid();
        var plan = Guid.NewGuid();

        account.TransferTo(reseller, plan);

        Assert.Equal(reseller, account.ResellerId);
        Assert.Equal(plan, account.PlanId);
    }

    /// <summary>Transferring an account to the administrator clears its reseller.</summary>
    [Fact]
    public void Transferring_an_account_to_the_administrator_clears_its_reseller()
    {
        var account = new Account(
            Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant, resellerId: Guid.NewGuid());
        var plan = Guid.NewGuid();

        account.TransferTo(null, plan);

        Assert.Null(account.ResellerId);
        Assert.Equal(plan, account.PlanId);
    }

    /// <summary>Transferring an account leaves its identity, status and contact alone.</summary>
    [Fact]
    public void Transferring_an_account_leaves_its_identity_status_and_contact_alone()
    {
        var id = Guid.NewGuid();
        var account = new Account(id, "acme", "acme.example.com", Guid.NewGuid(), SomeInstant, "owner@example.com");
        account.Suspend(SuspensionActor.Administrator);

        account.TransferTo(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(id, account.Id);
        Assert.Equal("acme", account.Name);
        Assert.Equal(AccountStatus.Suspended, account.Status);
        Assert.Equal("owner@example.com", account.OwnerEmail);
    }

    /// <summary>An account records who suspended it, and reactivating clears the record (issue #493).</summary>
    [Fact]
    public void Suspending_records_who_did_it_and_reactivating_clears_the_record()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);
        Assert.Null(account.SuspendedBy);

        account.Suspend(SuspensionActor.Reseller);
        Assert.Equal(SuspensionActor.Reseller, account.SuspendedBy);

        account.Reactivate();
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Null(account.SuspendedBy);
    }

    /// <summary>The strongest hold wins: an administrator's suspension replaces a reseller's and is never weakened.</summary>
    [Fact]
    public void An_administrators_suspension_replaces_a_resellers_and_is_never_weakened_to_it()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);

        account.Suspend(SuspensionActor.Reseller);
        account.Suspend(SuspensionActor.Administrator);
        Assert.Equal(SuspensionActor.Administrator, account.SuspendedBy);

        account.Suspend(SuspensionActor.Reseller);
        Assert.Equal(SuspensionActor.Administrator, account.SuspendedBy);
    }

    /// <summary>Only an administrator may lift an administrator's suspension; a reseller lifts its own.</summary>
    [Fact]
    public void A_reseller_may_lift_only_a_suspension_a_reseller_placed()
    {
        var byAdministrator = new Account(Guid.NewGuid(), "alpha", "alpha.example.com", Guid.NewGuid(), SomeInstant);
        byAdministrator.Suspend(SuspensionActor.Administrator);
        var byReseller = new Account(Guid.NewGuid(), "beta", "beta.example.com", Guid.NewGuid(), SomeInstant);
        byReseller.Suspend(SuspensionActor.Reseller);
        var active = new Account(Guid.NewGuid(), "gamma", "gamma.example.com", Guid.NewGuid(), SomeInstant);

        Assert.False(byAdministrator.IsReleasableBy(SuspensionActor.Reseller));
        Assert.True(byAdministrator.IsReleasableBy(SuspensionActor.Administrator));
        Assert.True(byReseller.IsReleasableBy(SuspensionActor.Reseller));
        Assert.True(byReseller.IsReleasableBy(SuspensionActor.Administrator));
        Assert.True(active.IsReleasableBy(SuspensionActor.Reseller));
    }
}
