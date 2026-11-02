using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;

namespace Maran.Modules.Identity.Tests.Domain;
/// <summary>Behavioural contract of user.</summary>

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly SecurityPolicySnapshot Policy = SecurityPolicySnapshot.Default;

    private static User NewUser()
    {
        return new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);
    }

    /// <summary>A new user has two factor disabled.</summary>
    [Fact]
    public void A_new_user_has_two_factor_disabled()
    {
        var user = NewUser();

        Assert.False(user.IsTotpEnabled);
        Assert.Null(user.TotpSecret);
    }

    /// <summary>A new user has never logged in.</summary>
    [Fact]
    public void A_new_user_has_never_logged_in()
    {
        Assert.Null(NewUser().LastLoginAt);
    }

    /// <summary>Enabling two factor stores the secret.</summary>
    [Fact]
    public void Enabling_two_factor_stores_the_secret()
    {
        var user = NewUser();

        user.EnableTotp("JBSWY3DPEHPK3PXP");

        Assert.True(user.IsTotpEnabled);
        Assert.Equal("JBSWY3DPEHPK3PXP", user.TotpSecret);
    }

    /// <summary>Disabling two factor clears the secret rather than only the flag.</summary>
    [Fact]
    public void Disabling_two_factor_clears_the_secret_rather_than_only_the_flag()
    {
        var user = NewUser();
        user.EnableTotp("JBSWY3DPEHPK3PXP");

        user.DisableTotp();

        Assert.False(user.IsTotpEnabled);
        Assert.Null(user.TotpSecret);
    }

    /// <summary>Recording a login updates the last login instant.</summary>
    [Fact]
    public void Recording_a_login_updates_the_last_login_instant()
    {
        var user = NewUser();

        user.RecordLogin(Now.AddHours(1));

        Assert.Equal(Now.AddHours(1), user.LastLoginAt);
    }

    /// <summary>Changing the password replaces the stored hash.</summary>
    [Fact]
    public void Changing_the_password_replaces_the_stored_hash()
    {
        var user = NewUser();

        user.ChangePassword("a-new-hash");

        Assert.Equal("a-new-hash", user.PasswordHash);
    }

    /// <summary>An administrator owns no account until one is assigned.</summary>
    [Fact]
    public void An_administrator_owns_no_account_until_one_is_assigned()
    {
        Assert.Null(NewUser().AccountId);
    }

    /// <summary>A new user is not locked out.</summary>
    [Fact]
    public void A_new_user_is_not_locked_out()
    {
        var user = NewUser();

        Assert.False(user.IsLockedOut(Now));
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    /// <summary>Failures below the threshold do not lock the account.</summary>
    [Fact]
    public void Failures_below_the_threshold_do_not_lock_the_account()
    {
        var user = NewUser();

        for (var attempt = 0; attempt < Policy.MaxFailedLoginAttempts - 1; attempt++)
        {
            user.RecordFailedLogin(Now, Policy.MaxFailedLoginAttempts, Policy.LockoutDuration());
        }

        Assert.False(user.IsLockedOut(Now));
    }

    /// <summary>The failure that reaches the threshold locks the account.</summary>
    [Fact]
    public void The_failure_that_reaches_the_threshold_locks_the_account()
    {
        var user = NewUser();

        for (var attempt = 0; attempt < Policy.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now, Policy.MaxFailedLoginAttempts, Policy.LockoutDuration());
        }

        Assert.True(user.IsLockedOut(Now));
        Assert.Equal(Now + Policy.LockoutDuration(), user.LockedUntil);
    }

    /// <summary>The lock expires on its own.</summary>
    [Fact]
    public void The_lock_expires_on_its_own()
    {
        var user = NewUser();
        for (var attempt = 0; attempt < Policy.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now, Policy.MaxFailedLoginAttempts, Policy.LockoutDuration());
        }

        Assert.False(user.IsLockedOut(Now + Policy.LockoutDuration() + TimeSpan.FromSeconds(1)));
    }

    /// <summary>A successful login clears the failures and the lock.</summary>
    [Fact]
    public void A_successful_login_clears_the_failures_and_the_lock()
    {
        var user = NewUser();
        for (var attempt = 0; attempt < Policy.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now, Policy.MaxFailedLoginAttempts, Policy.LockoutDuration());
        }

        user.RecordLogin(Now);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
        Assert.False(user.IsLockedOut(Now));
    }

    /// <summary>Unassigning the account the older column names clears it.</summary>
    [Fact]
    public void Unassigning_the_account_the_older_column_names_clears_it()
    {
        var user = NewUser();
        var accountId = Guid.NewGuid();
        user.AssignAccount(accountId);

        user.UnassignAccount(accountId);

        Assert.Null(user.AccountId);
    }

    /// <summary>Unassigning some other account leaves the older column alone.</summary>
    [Fact]
    public void Unassigning_some_other_account_leaves_the_older_column_alone()
    {
        var user = NewUser();
        var accountId = Guid.NewGuid();
        user.AssignAccount(accountId);

        user.UnassignAccount(Guid.NewGuid());

        Assert.Equal(accountId, user.AccountId);
    }

    /// <summary>A claimed login reads as active.</summary>
    [Fact]
    public void A_claimed_login_reads_as_active()
    {
        Assert.Equal(UserLifecycle.Active, NewUser().Lifecycle);
    }

    /// <summary>An unclaimed invitation reads as invited, and as invited-on-a-suspended-account once suspended.</summary>
    [Fact]
    public void An_unclaimed_invitation_reads_as_invited_and_stays_distinguishable_once_its_account_is_suspended()
    {
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Guid.NewGuid(), Now);
        Assert.Equal(UserLifecycle.Invited, user.Lifecycle);

        user.Suspend();
        Assert.Equal(UserLifecycle.InvitedAccountSuspended, user.Lifecycle);

        user.Resume();
        Assert.Equal(UserLifecycle.Invited, user.Lifecycle);
    }

    /// <summary>A claimed login reads as suspended while its account is, and active again after the resume.</summary>
    [Fact]
    public void A_claimed_login_reads_as_suspended_while_its_account_is_and_active_after_the_resume()
    {
        var user = NewUser();

        user.Suspend();
        Assert.Equal(UserLifecycle.Suspended, user.Lifecycle);

        user.Resume();
        Assert.Equal(UserLifecycle.Active, user.Lifecycle);
    }

    /// <summary>An invitation accepted during a suspension reads as suspended, not active.</summary>
    [Fact]
    public void An_invitation_accepted_during_a_suspension_reads_as_suspended_not_active()
    {
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Guid.NewGuid(), Now);
        user.Suspend();

        user.Activate("hash");

        Assert.Equal(UserLifecycle.Suspended, user.Lifecycle);
    }

    /// <summary>Two-factor reads as enabled exactly while a secret is stored.</summary>
    [Fact]
    public void Two_factor_reads_as_enabled_exactly_while_a_secret_is_stored()
    {
        var user = NewUser();
        Assert.False(user.IsTotpEnabled);
        Assert.Null(user.TotpSecret);

        user.EnableTotp("SECRET");
        Assert.True(user.IsTotpEnabled);
        Assert.Equal("SECRET", user.TotpSecret);

        user.DisableTotp();
        Assert.False(user.IsTotpEnabled);
        Assert.Null(user.TotpSecret);
    }

    /// <summary>The legacy enrolment flag follows enabling and disabling two-factor.</summary>
    [Fact]
    public void The_legacy_enrolment_flag_follows_enabling_and_disabling_two_factor()
    {
        var user = NewUser();
        Assert.False(user.LegacyTotpEnabled);

        user.EnableTotp("SECRET");
        Assert.True(user.LegacyTotpEnabled);

        user.DisableTotp();
        Assert.False(user.LegacyTotpEnabled);
    }

    /// <summary>The legacy state pair is restated from the lifecycle at every step the previous release could read.</summary>
    [Fact]
    public void The_legacy_state_pair_is_restated_from_the_lifecycle_at_every_step()
    {
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Guid.NewGuid(), Now);
        Assert.Equal((UserState.Invited, false), (user.LegacyState, user.LegacyAccountSuspended));

        user.Suspend();
        Assert.Equal((UserState.Invited, true), (user.LegacyState, user.LegacyAccountSuspended));

        user.Activate("hash");
        Assert.Equal((UserState.Suspended, true), (user.LegacyState, user.LegacyAccountSuspended));

        user.Resume();
        Assert.Equal((UserState.Active, false), (user.LegacyState, user.LegacyAccountSuspended));
    }

    /// <summary>A pending invitation is reported for both invited lifecycles and for no other.</summary>
    [Fact]
    public void A_pending_invitation_is_reported_for_both_invited_lifecycles_and_no_other()
    {
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Guid.NewGuid(), Now);
        Assert.True(user.HasPendingInvitation);

        user.Suspend();
        Assert.True(user.HasPendingInvitation);

        user.Activate("hash");
        Assert.False(user.HasPendingInvitation);
    }
}
