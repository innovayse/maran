using Maran.Modules.Accounts.Domain;
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

        account.Suspend();

        Assert.Equal(AccountStatus.Suspended, account.Status);
    }

    /// <summary>Suspending an already suspended account is a harmless no op.</summary>
    [Fact]
    public void Suspending_an_already_suspended_account_is_a_harmless_no_op()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);
        account.Suspend();

        account.Suspend();

        Assert.Equal(AccountStatus.Suspended, account.Status);
    }

    /// <summary>Reactivating a suspended account moves it to active.</summary>
    [Fact]
    public void Reactivating_a_suspended_account_moves_it_to_active()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), SomeInstant);
        account.Suspend();

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
}
