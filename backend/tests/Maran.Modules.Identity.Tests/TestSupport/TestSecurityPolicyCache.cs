using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>Builds a <see cref="SecurityPolicyCache"/> over a test's own database context.</summary>
public static class TestSecurityPolicyCache
{
    /// <summary>
    /// How long a test waits on the cache's load gate before concluding the wait will not finish.
    /// </summary>
    /// <remarks>
    /// Used in both directions and safe in both. Waiting for a signal that is coming, it is an upper
    /// bound rather than a delay — the wait ends when the signal arrives. Asserting that a blocked
    /// invalidation does NOT complete, it is paid in full once, and it can only mis-report in the
    /// direction of a test that is too slow, never one that passes against a broken cache: an
    /// invalidation that does not take the gate returns immediately, not in a second.
    /// </remarks>
    public static readonly TimeSpan GateWait = TimeSpan.FromSeconds(1);

    /// <summary>Creates a cache reading from <paramref name="dbContext"/>.</summary>
    /// <param name="dbContext">The test's context.</param>
    /// <returns>The cache. With no policy row present it answers the built-in defaults.</returns>
    public static SecurityPolicyCache Over(IdentityDbContext dbContext)
    {
        return new SecurityPolicyCache(new SingleContextScopeFactory(dbContext));
    }

    /// <summary>Creates a cache over a context that already holds a saved policy.</summary>
    /// <param name="dbContext">The test's context.</param>
    /// <param name="minimumPasswordLength">The shortest password the panel accepts.</param>
    /// <param name="forceTwoFactorForAdmins">Whether administrators must hold a second factor.</param>
    /// <param name="maxFailedLoginAttempts">Consecutive failed sign-ins that lock an account.</param>
    /// <param name="lockoutMinutes">How long a locked account stays locked, in minutes.</param>
    /// <returns>The cache, reading the row just written.</returns>
    public static SecurityPolicyCache Saved(
        IdentityDbContext dbContext,
        int minimumPasswordLength = SecurityPolicy.DefaultMinimumPasswordLength,
        bool forceTwoFactorForAdmins = SecurityPolicy.DefaultForceTwoFactorForAdmins,
        int maxFailedLoginAttempts = SecurityPolicy.DefaultMaxFailedLoginAttempts,
        int lockoutMinutes = SecurityPolicy.DefaultLockoutMinutes)
    {
        dbContext.SecurityPolicies.Add(new SecurityPolicy(
            minimumPasswordLength,
            forceTwoFactorForAdmins,
            maxFailedLoginAttempts,
            lockoutMinutes,
            DateTimeOffset.UnixEpoch));
        dbContext.SaveChanges();

        return Over(dbContext);
    }
}
