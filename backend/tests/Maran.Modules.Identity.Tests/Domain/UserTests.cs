using Maran.Modules.Identity.Domain;
using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Tests.Domain;
/// <summary>Behavioural contract of user.</summary>

public sealed class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);

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

        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts - 1; attempt++)
        {
            user.RecordFailedLogin(Now);
        }

        Assert.False(user.IsLockedOut(Now));
    }

    /// <summary>The failure that reaches the threshold locks the account.</summary>
    [Fact]
    public void The_failure_that_reaches_the_threshold_locks_the_account()
    {
        var user = NewUser();

        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
        }

        Assert.True(user.IsLockedOut(Now));
        Assert.Equal(Now + User.LockoutDuration, user.LockedUntil);
    }

    /// <summary>The lock expires on its own.</summary>
    [Fact]
    public void The_lock_expires_on_its_own()
    {
        var user = NewUser();
        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
        }

        Assert.False(user.IsLockedOut(Now + User.LockoutDuration + TimeSpan.FromSeconds(1)));
    }

    /// <summary>A successful login clears the failures and the lock.</summary>
    [Fact]
    public void A_successful_login_clears_the_failures_and_the_lock()
    {
        var user = NewUser();
        for (var attempt = 0; attempt < User.MaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(Now);
        }

        user.RecordLogin(Now);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
        Assert.False(user.IsLockedOut(Now));
    }
}
