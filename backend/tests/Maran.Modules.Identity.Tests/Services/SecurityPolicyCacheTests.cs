using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Services;
using Maran.Modules.Identity.Tests.TestSupport;

namespace Maran.Modules.Identity.Tests.Services;

/// <summary>Behavioural contract of the security-policy cache.</summary>
public sealed class SecurityPolicyCacheTests
{
    /// <summary>A panel that has never saved a policy obeys the built-in defaults.</summary>
    /// <remarks>
    /// The absence of the row is a legitimate state — every fresh installation is in it — so the
    /// cache answers with the defaults rather than with null. A null would make every caller decide
    /// for itself what "no policy" means, and the safe answer is not the obvious one.
    /// </remarks>
    [Fact]
    public async Task A_panel_that_has_never_saved_a_policy_obeys_the_built_in_defaults()
    {
        using var context = IdentityTestContext.Create();

        var policy = await TestSecurityPolicyCache.Over(context).GetAsync(CancellationToken.None);

        Assert.Equal(SecurityPolicySnapshot.Default, policy);
        Assert.Equal(SecurityPolicy.DefaultMinimumPasswordLength, policy.MinimumPasswordLength);
        Assert.Equal(TimeSpan.FromMinutes(SecurityPolicy.DefaultLockoutMinutes), policy.LockoutDuration());
    }

    /// <summary>A saved policy is what the cache reports.</summary>
    [Fact]
    public async Task A_saved_policy_is_what_the_cache_reports()
    {
        using var context = IdentityTestContext.Create();
        var cache = TestSecurityPolicyCache.Saved(context, minimumPasswordLength: 20, forceTwoFactorForAdmins: true);

        var policy = await cache.GetAsync(CancellationToken.None);

        Assert.Equal(20, policy.MinimumPasswordLength);
        Assert.True(policy.ForceTwoFactorForAdmins);
    }

    /// <summary>A cached policy is re-read only after the cache is invalidated.</summary>
    /// <remarks>
    /// Both halves matter. Without the caching the sign-in path pays a query per request; without the
    /// invalidation a saved policy takes effect at the next restart, which is not a behaviour an
    /// administrator can work with.
    /// </remarks>
    [Fact]
    public async Task A_cached_policy_is_re_read_only_after_the_cache_is_invalidated()
    {
        using var context = IdentityTestContext.Create();
        var cache = TestSecurityPolicyCache.Over(context);
        Assert.Equal(
            SecurityPolicy.DefaultMinimumPasswordLength,
            (await cache.GetAsync(CancellationToken.None)).MinimumPasswordLength);

        context.SecurityPolicies.Add(new SecurityPolicy(24, true, 5, 30, DateTimeOffset.UnixEpoch));
        await context.SaveChangesAsync();

        Assert.Equal(
            SecurityPolicy.DefaultMinimumPasswordLength,
            (await cache.GetAsync(CancellationToken.None)).MinimumPasswordLength);

        cache.Invalidate();

        Assert.Equal(24, (await cache.GetAsync(CancellationToken.None)).MinimumPasswordLength);
    }

    /// <summary>A save that commits while a load is in flight waits for that load, and then wins.</summary>
    /// <remarks>
    /// <para>
    /// The failure this pins is silent and permanent. A read that started before a save returns
    /// after it and used to publish the pre-save row; because this cache has no expiry by design,
    /// that row then stayed the panel's policy until the process restarted — forced two-factor
    /// switched on by an administrator and never taking effect, while the screen that reads the row
    /// directly showed it as on.
    /// </para>
    /// <para>
    /// The guarantee is mutual exclusion, so that is what is asserted, and it is asserted from the
    /// one instant that matters: the invalidation is fired from inside the load, after the row has
    /// been read and before it is published, on a thread of its own. An invalidation that does not
    /// take the load gate completes there — which is the whole defect, in either of its shapes: the
    /// bare assignment, and the version counter whose compare and publish are two operations an
    /// invalidation can land between. An invalidation that does take the gate cannot complete until
    /// the load has published and released, which is why the wait below must time out, and why the
    /// save is nevertheless the value the panel ends up with.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task A_save_that_commits_while_a_load_is_in_flight_waits_for_that_load_and_then_wins()
    {
        using var context = IdentityTestContext.Create();
        SecurityPolicyCache? cache = null;
        var invalidationEntered = new TaskCompletionSource();
        Task? invalidation = null;

        // Runs after the load has read the (absent) row and before it publishes what it read.
        var factory = new CallbackScopeFactory(context, () =>
        {
            context.SecurityPolicies.Add(new SecurityPolicy(24, true, 5, 30, DateTimeOffset.UnixEpoch));
            context.SaveChanges();

            invalidation = Task.Run(() =>
            {
                invalidationEntered.SetResult();
                cache!.Invalidate();
            });

            invalidationEntered.Task.Wait(TestSecurityPolicyCache.GateWait);

            // False is the assertion: the invalidation is still blocked on the load's own gate. An
            // invalidation that skipped the gate would have completed inside this window and gone
            // on to be undone by the publish that follows.
            Assert.False(invalidation.Wait(TestSecurityPolicyCache.GateWait));
        });

        cache = new SecurityPolicyCache(factory);

        var duringTheSave = await cache.GetAsync(CancellationToken.None);
        Assert.Equal(SecurityPolicy.DefaultMinimumPasswordLength, duringTheSave.MinimumPasswordLength);

        await invalidation!;

        var afterTheSave = await cache.GetAsync(CancellationToken.None);
        Assert.Equal(24, afterTheSave.MinimumPasswordLength);
        Assert.True(afterTheSave.ForceTwoFactorForAdmins);
    }
}
