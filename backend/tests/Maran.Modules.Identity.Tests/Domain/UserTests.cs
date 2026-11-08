using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.ValueObjects;

namespace Maran.Modules.Identity.Tests.Domain;
/// <summary>Behavioural contract of user.</summary>

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

    private static readonly SecurityPolicySnapshot Policy = SecurityPolicySnapshot.Default;

    private static readonly Guid ResellerId = new("aaaaaaaa-0000-4000-8000-0000000000e1");

    private static User NewUser()
    {
        return new User(Guid.NewGuid(), "admin", "admin@example.com", "hash", UserRole.Admin, Now);
    }

    private static User NewReseller()
    {
        return User.InviteReseller(Guid.NewGuid(), "northwind", "contact@example.com", ResellerId, Now);
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

    /// <summary>A claimed login reads as active.</summary>
    [Fact]
    public void A_claimed_login_reads_as_active()
    {
        Assert.Equal(UserLifecycle.Active, NewUser().Lifecycle);
    }

    /// <summary>An unclaimed invitation reads as invited, and as active once the password is chosen.</summary>
    [Fact]
    public void An_unclaimed_invitation_reads_as_invited_and_as_active_once_accepted()
    {
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now);
        Assert.Equal(UserLifecycle.Invited, user.Lifecycle);
        Assert.False(user.MayAuthenticate);

        user.Activate("hash");

        Assert.Equal(UserLifecycle.Active, user.Lifecycle);
        Assert.True(user.MayAuthenticate);
        Assert.Equal("hash", user.PasswordHash);
    }

    /// <summary>A login holds no account: the factory takes none and the lifecycle has no suspended value.</summary>
    [Fact]
    public void A_login_has_only_the_two_lifecycles_it_can_really_be_in()
    {
        Assert.Equal(
            [nameof(UserLifecycle.Active), nameof(UserLifecycle.Invited)],
            Enum.GetNames<UserLifecycle>().Order().ToArray());
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
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now);
        Assert.Equal((UserState.Invited, false), (user.LegacyState, user.LegacyAccountSuspended));

        user.Activate("hash");
        Assert.Equal((UserState.Active, false), (user.LegacyState, user.LegacyAccountSuspended));
    }

    /// <summary>The legacy pair of a claimed owner reads suspended while the owner membership is, and clears on resume.</summary>
    [Fact]
    public void The_legacy_pair_of_a_claimed_owner_reads_suspended_until_the_membership_is_active_again()
    {
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now);
        user.Activate("hash");

        user.SyncLegacyColumns(MembershipState.Suspended);
        Assert.Equal((UserState.Suspended, true), (user.LegacyState, user.LegacyAccountSuspended));
        Assert.Equal(UserLifecycle.Active, user.Lifecycle);

        user.SyncLegacyColumns(MembershipState.Active);
        Assert.Equal((UserState.Active, false), (user.LegacyState, user.LegacyAccountSuspended));
    }

    /// <summary>An invited owner of a suspended account stays Invited in the legacy state, with the flag set.</summary>
    [Fact]
    public void An_invited_owner_of_a_suspended_account_keeps_the_invited_state_with_the_flag_set()
    {
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now);

        user.SyncLegacyColumns(MembershipState.Suspended);

        Assert.Equal((UserState.Invited, true), (user.LegacyState, user.LegacyAccountSuspended));
    }

    /// <summary>Accepting an invitation never lifts the suspension the legacy flag records.</summary>
    [Fact]
    public void Accepting_an_invitation_does_not_lift_the_legacy_suspension()
    {
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now);
        user.SyncLegacyColumns(MembershipState.Suspended);

        user.Activate("hash");

        Assert.Equal((UserState.Suspended, true), (user.LegacyState, user.LegacyAccountSuspended));
    }

    /// <summary>A pending invitation is reported while invited and no longer once accepted.</summary>
    [Fact]
    public void A_pending_invitation_is_reported_while_invited_and_not_after_acceptance()
    {
        var user = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now);
        Assert.True(user.HasPendingInvitation);

        user.Activate("hash");
        Assert.False(user.HasPendingInvitation);
    }

    /// <summary>A reseller's invited login has the Reseller role, its reseller id and waits for its invitation.</summary>
    [Fact]
    public void An_invited_reseller_login_has_the_role_the_reseller_id_and_a_pending_invitation()
    {
        var login = NewReseller();

        Assert.Equal(UserRole.Reseller, login.Role);
        Assert.Equal(ResellerId, login.ResellerId);
        Assert.Equal("northwind", login.Username);
        Assert.Equal("contact@example.com", login.Email);
        Assert.Equal(UserLifecycle.Invited, login.Lifecycle);
        Assert.True(login.HasPendingInvitation);
        Assert.Equal(string.Empty, login.PasswordHash);
        Assert.False(login.IsSuspended);
        Assert.Equal((UserState.Invited, false), (login.LegacyState, login.LegacyAccountSuspended));
    }

    /// <summary>Every login that is not a reseller's carries no reseller id.</summary>
    [Fact]
    public void A_login_that_is_not_a_resellers_carries_no_reseller_id()
    {
        Assert.Null(NewUser().ResellerId);
        Assert.Null(User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now).ResellerId);
    }

    /// <summary>An invited reseller cannot authenticate; accepting the invitation lets it.</summary>
    [Fact]
    public void A_reseller_may_authenticate_only_once_it_accepted_its_invitation()
    {
        var login = NewReseller();
        Assert.False(login.MayAuthenticate);

        login.Activate("hash");

        Assert.True(login.MayAuthenticate);
        Assert.Equal("hash", login.PasswordHash);
    }

    /// <summary>Suspending a claimed reseller stops it authenticating, and resuming lets it back.</summary>
    [Fact]
    public void Suspending_a_claimed_reseller_stops_it_authenticating_and_resuming_restores_it()
    {
        var login = NewReseller();
        login.Activate("hash");

        login.Suspend();
        Assert.True(login.IsSuspended);
        Assert.False(login.MayAuthenticate);
        Assert.Equal(UserLifecycle.Active, login.Lifecycle);

        login.Resume();
        Assert.False(login.IsSuspended);
        Assert.True(login.MayAuthenticate);
    }

    /// <summary>Suspending and resuming repeat harmlessly.</summary>
    [Fact]
    public void Suspending_and_resuming_twice_change_nothing_the_second_time()
    {
        var login = NewReseller();
        login.Activate("hash");

        login.Suspend();
        login.Suspend();
        Assert.True(login.IsSuspended);

        login.Resume();
        login.Resume();
        Assert.False(login.IsSuspended);
        Assert.True(login.MayAuthenticate);
    }

    /// <summary>Accepting an invitation does not lift a suspension: the suspended reseller stays unable to sign in.</summary>
    [Fact]
    public void Accepting_an_invitation_does_not_lift_a_resellers_suspension()
    {
        var login = NewReseller();
        login.Suspend();

        login.Activate("hash");

        Assert.True(login.IsSuspended);
        Assert.False(login.MayAuthenticate);
        Assert.Equal(UserLifecycle.Active, login.Lifecycle);
        Assert.False(login.HasPendingInvitation);
    }

    /// <summary>A suspended reseller still waiting for its invitation is both at once, and resuming leaves it invited.</summary>
    [Fact]
    public void Resuming_a_reseller_that_never_accepted_leaves_it_invited_and_unable_to_authenticate()
    {
        var login = NewReseller();
        login.Suspend();

        login.Resume();

        Assert.False(login.IsSuspended);
        Assert.True(login.HasPendingInvitation);
        Assert.False(login.MayAuthenticate);
    }

    /// <summary>Only a reseller's login is suspended on the login: an administrator and a customer refuse it.</summary>
    [Fact]
    public void Suspending_or_resuming_a_login_that_is_not_a_resellers_throws_and_changes_nothing()
    {
        var admin = NewUser();
        var customer = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now);
        customer.Activate("hash");

        Assert.Throws<InvalidOperationException>(() => { admin.Suspend(); });
        Assert.Throws<InvalidOperationException>(() => { admin.Resume(); });
        Assert.Throws<InvalidOperationException>(() => { customer.Suspend(); });
        Assert.Throws<InvalidOperationException>(() => { customer.Resume(); });

        Assert.False(admin.IsSuspended);
        Assert.True(admin.MayAuthenticate);
        Assert.False(customer.IsSuspended);
        Assert.True(customer.MayAuthenticate);
    }

    /// <summary>A customer's membership suspension never reaches the login's own flag.</summary>
    [Fact]
    public void A_customers_legacy_suspension_never_sets_the_login_suspension_flag()
    {
        var customer = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now);
        customer.Activate("hash");

        customer.SyncLegacyColumns(MembershipState.Suspended);

        Assert.False(customer.IsSuspended);
        Assert.True(customer.MayAuthenticate);
    }

    /// <summary>A login created with an initial password is usable at once and must change it.</summary>
    [Fact]
    public void A_login_created_with_an_initial_password_is_usable_and_must_change_it()
    {
        var user = User.CreateWithPassword(Guid.NewGuid(), "acme", "owner@example.com", "initial-hash", Now);

        Assert.True(user.PasswordChangeRequired);
        Assert.Equal(UserLifecycle.Active, user.Lifecycle);
        Assert.Equal(UserRole.Customer, user.Role);
        Assert.Equal("initial-hash", user.PasswordHash);
        Assert.True(user.MayAuthenticate);
    }

    /// <summary>Logins made any other way do not owe a password change.</summary>
    /// <remarks>The accepting control for the flag: a flag that was always true would pass the test above.</remarks>
    [Fact]
    public void A_login_made_any_other_way_owes_no_password_change()
    {
        var invited = User.Invite(Guid.NewGuid(), "acme", "owner@example.com", Now);
        invited.Activate("chosen-hash");

        Assert.False(NewUser().PasswordChangeRequired);
        Assert.False(NewReseller().PasswordChangeRequired);
        Assert.False(invited.PasswordChangeRequired);
    }

    /// <summary>Changing the password ends the obligation to change it.</summary>
    [Fact]
    public void Changing_the_password_ends_the_obligation_to_change_it()
    {
        var user = User.CreateWithPassword(Guid.NewGuid(), "acme", "owner@example.com", "initial-hash", Now);

        user.ChangePassword("chosen-hash");

        Assert.False(user.PasswordChangeRequired);
        Assert.Equal("chosen-hash", user.PasswordHash);
    }

    /// <summary>Re-deriving the hash of the same password does not end the obligation.</summary>
    /// <remarks>
    /// The refusing half: the sign-in rehash runs with the initial password in hand, and a rehash that
    /// cleared the flag would let the first sign-in replace the very obligation it is meant to enforce.
    /// </remarks>
    [Fact]
    public void Rehashing_the_same_password_keeps_the_obligation()
    {
        var user = User.CreateWithPassword(Guid.NewGuid(), "acme", "owner@example.com", "initial-hash", Now);

        user.Rehash("stronger-hash");

        Assert.True(user.PasswordChangeRequired);
        Assert.Equal("stronger-hash", user.PasswordHash);
    }
}
