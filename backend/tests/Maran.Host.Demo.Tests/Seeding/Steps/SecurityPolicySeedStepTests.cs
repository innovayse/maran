using Maran.Host.Demo.Seeding.Steps;
using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Host.Demo.Tests.Seeding.Steps;

/// <summary>The policy the demo is seeded under cannot be tripped by a visitor.</summary>
public sealed class SecurityPolicySeedStepTests
{
    /// <summary>The "never locked" ceiling is above every value the save command's validator would accept.</summary>
    [Fact]
    public void The_lockout_ceiling_is_beyond_what_a_script_can_reach()
    {
        Assert.True(SecurityPolicySeedStep.NeverLockedAttempts > 100);
        Assert.True(SecurityPolicySeedStep.NeverLockedAttempts > SecurityPolicy.DefaultMaxFailedLoginAttempts);
    }

    /// <summary>A failed sign-in counted against the seeded ceiling never locks the login.</summary>
    [Fact]
    public void Many_failures_never_lock_a_login_under_the_seeded_ceiling()
    {
        var user = User.Invite(Guid.NewGuid(), "demo", "demo@example.com", DateTimeOffset.UnixEpoch);
        var now = DateTimeOffset.UnixEpoch.AddDays(1);

        for (var attempt = 0; attempt < 10_000; attempt++)
        {
            user.RecordFailedLogin(now, SecurityPolicySeedStep.NeverLockedAttempts, TimeSpan.FromMinutes(1));
        }

        Assert.Null(user.LockedUntil);
    }

    /// <summary>The control: the panel's default ceiling does lock the same login, so the check could fail.</summary>
    [Fact]
    public void The_default_ceiling_locks_the_same_login()
    {
        var user = User.Invite(Guid.NewGuid(), "demo", "demo@example.com", DateTimeOffset.UnixEpoch);
        var now = DateTimeOffset.UnixEpoch.AddDays(1);

        for (var attempt = 0; attempt < SecurityPolicy.DefaultMaxFailedLoginAttempts; attempt++)
        {
            user.RecordFailedLogin(now, SecurityPolicy.DefaultMaxFailedLoginAttempts, TimeSpan.FromMinutes(1));
        }

        Assert.NotNull(user.LockedUntil);
    }
}
