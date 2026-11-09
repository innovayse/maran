using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;

namespace Maran.Modules.Accounts.Tests.Domain;

/// <summary>
/// What an account remembers of how it was created (issue #194): its shell, which the agent does not
/// keep, and the ceilings it states for itself.
/// </summary>
public sealed class AccountShellAndOverridesTests
{
    /// <summary>The instant accounts are created at.</summary>
    private static readonly DateTimeOffset Created = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>An account created without stating either has no shell and follows its plan.</summary>
    [Fact]
    public void An_account_created_without_stating_either_has_no_shell_and_follows_its_plan()
    {
        var account = new Account(Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Created);

        Assert.False(account.ShellAccess);
        Assert.True(account.LimitOverrides.IsEmpty);
    }

    /// <summary>An account keeps the shell and the ceilings it was created with.</summary>
    [Fact]
    public void An_account_keeps_the_shell_and_the_ceilings_it_was_created_with()
    {
        var account = new Account(
            Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Created,
            shellAccess: true, limitOverrides: AccountLimitOverrides.Of(1_000, 20, 512));

        Assert.True(account.ShellAccess);
        Assert.Equal(1_000, account.LimitOverrides.InodeLimit);
        Assert.Equal(20, account.LimitOverrides.ProcessLimit);
        Assert.Equal(512, account.LimitOverrides.OpenFilesLimit);
    }

    /// <summary>Moving the account to another plan does not touch what it states for itself.</summary>
    [Fact]
    public void Moving_the_account_to_another_plan_does_not_touch_what_it_states_for_itself()
    {
        var account = new Account(
            Guid.NewGuid(), "acme", "acme.example.com", Guid.NewGuid(), Created,
            shellAccess: true, limitOverrides: AccountLimitOverrides.Of(1_000, 20, 512));

        account.ChangePlan(Guid.NewGuid());

        Assert.True(account.ShellAccess);
        Assert.Equal(20, account.LimitOverrides.ProcessLimit);
    }
}
