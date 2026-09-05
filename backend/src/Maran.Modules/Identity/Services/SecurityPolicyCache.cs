using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Holds the panel's one security-policy row in memory, and forgets it the moment somebody saves a
/// new one (R12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a cache at all.</b> The policy is read on the hottest path the panel has — every sign-in
/// reads the lockout numbers, every password validator reads the minimum length, every issued access
/// token reads the forced-2FA flag — and it changes perhaps twice in a server's life. A row read per
/// request is not ruinous on its own; what makes the cache worth its existence is that it removes a
/// database round trip from the refusal path of a brute-force attack, which is exactly the moment
/// the panel can least afford one.
/// </para>
/// <para>
/// <b>Invalidation is a write-side responsibility, and it is a single call.</b> The save handler
/// calls <see cref="Invalidate"/> after it commits. There is deliberately no expiry: a time-based
/// cache would mean a saved policy takes effect at some unpredictable moment, and "forced 2FA is on
/// but not for everyone yet" is not a state an operator can reason about. The panel is one process
/// per server, so an invalidation reaches every reader of this policy by reaching this object; there
/// is no second process an expiry would be catching up for. A deployment that ever ran two API
/// processes would need this cache rethought, not a shorter lifetime bolted onto it.
/// </para>
/// <para>
/// <b>Because there is no expiry, a lost invalidation would be permanent</b>, so losing one must be
/// impossible rather than unlikely. Committing before invalidating is necessary but NOT sufficient:
/// a load that started before the save returns after it, and if it could publish what it read, the
/// pre-save policy would be re-cached for the life of the process — forced two-factor switched on
/// and silently never taking effect, with the administrator's own screen (which reads the row, not
/// this cache) showing the new values. <see cref="_gate"/> is what prevents it: a load holds the
/// gate from before it queries until after it publishes, and <see cref="Invalidate"/> takes the same
/// gate, so an invalidation can only be observed by a load that has not yet queried, never by one
/// that is between reading a row and publishing it. A version counter compared before the publish
/// was tried first and rejected: the compare and the publish are two operations, so an invalidation
/// landing between them reproduced the same permanent stale policy through a narrower window. There
/// is no window left to narrow here, because there is no interleaving to have one.
/// </para>
/// <para>
/// <b>What it costs.</b> <see cref="Invalidate"/> is called from a request path — the save handler,
/// after its commit — and it now blocks until any in-flight load finishes, at most one policy query.
/// That is the whole price: policy saves are rare enough to count on one hand over a server's life,
/// the blocked caller is the administrator who asked for the change, and it buys the reader path
/// nothing to pay for, since <see cref="GetAsync"/>'s fast path still takes no lock at all.
/// </para>
/// <para>
/// <b>The absence of a row is cached too, as the defaults.</b> A panel that has never opened the
/// security screen is the ordinary state of a fresh installation; re-querying an empty table on
/// every sign-in for the life of the process would be the cost of treating that state as "not loaded
/// yet".
/// </para>
/// <para>
/// <b>It is a singleton and it resolves a scoped context through a scope factory.</b> A singleton
/// that captured <c>IdentityDbContext</c> directly is refused by the container at build time, which
/// stops the whole API rather than degrading one feature.
/// </para>
/// </remarks>
public sealed class SecurityPolicyCache : IDisposable
{
    /// <summary>Opens one scope per load to resolve the module's database context from.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// Serialises loads against each other and against invalidations, so a burst of sign-ins on a
    /// cold cache issues one query rather than one each, and no load can publish a row that a save
    /// has already superseded.
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The cached policy, or <c>null</c> until the first load.</summary>
    /// <remarks>
    /// Read on the fast path without taking <see cref="_gate"/>, so every access goes through
    /// <see cref="Volatile"/>. Plain double-checked locking on a non-volatile reference is unsound on
    /// a weakly ordered architecture — arm64 is in this product's OS matrix — where a reader can see
    /// the published reference before the record's own fields, and evaluate a half-built policy as
    /// "two-factor not forced, zero failed attempts allowed".
    /// </remarks>
    private SecurityPolicySnapshot? _snapshot;

    /// <summary>Creates the cache.</summary>
    /// <param name="scopeFactory">Opens the scope each load resolves the database context from.</param>
    public SecurityPolicyCache(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    /// <summary>Reads the panel's security policy, loading it on the first call after an invalidation.</summary>
    /// <param name="cancellationToken">Cancellation token for the load.</param>
    /// <returns>The saved policy, or <see cref="SecurityPolicySnapshot.Default"/> when none was ever saved.</returns>
    public async Task<SecurityPolicySnapshot> GetAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _snapshot) is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // Re-checked inside the gate: several callers can pass the check above before any of
            // them takes the lock, and without this the second one would repeat the query it was
            // waiting for the first to finish.
            if (Volatile.Read(ref _snapshot) is { } loaded)
            {
                return loaded;
            }

            // Read and published under the one gate an invalidation must also hold. A save that
            // commits while this query is in flight cannot invalidate until the publish below has
            // happened, and its invalidation then clears exactly the row it superseded.
            var policy = await LoadAsync(cancellationToken);
            Volatile.Write(ref _snapshot, policy);

            return policy;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Forgets what is cached, so the next read goes to the database.</summary>
    /// <remarks>
    /// <para>
    /// Called after a save commits, never before: invalidating first would let a read that starts
    /// afterwards load the pre-commit row and cache it as current.
    /// </para>
    /// <para>
    /// Ordering alone does not close the window, and this used to claim it did, first through the
    /// ordering itself and then through a version counter checked just before the publish. Both left
    /// a load already in flight able to publish the pre-save row afterwards, and with no expiry on
    /// this cache that row is the panel's policy until the process restarts. Taking the load gate is
    /// what removes the interleaving instead of shrinking it: while a load holds the gate this call
    /// waits, and the clearing below therefore always happens after that load's publish, never
    /// before it.
    /// </para>
    /// <para>
    /// The wait is bounded by one policy query and is paid by the save request, which is the right
    /// caller to pay it — see the type's remarks.
    /// </para>
    /// <para>
    /// The gate is not reentrant, so this is called from a request that holds nothing, never from
    /// inside a load. Nothing on the load path invalidates, and nothing should be made to.
    /// </para>
    /// </remarks>
    public void Invalidate()
    {
        _gate.Wait();

        try
        {
            Volatile.Write(ref _snapshot, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the load gate when the container shuts the panel down.</summary>
    public void Dispose()
    {
        _gate.Dispose();
    }

    /// <summary>Reads the policy row and projects it onto a detached snapshot.</summary>
    /// <param name="cancellationToken">Cancellation token for the read.</param>
    /// <returns>The snapshot, or the defaults when the table is empty.</returns>
    /// <remarks>
    /// <c>AsNoTracking</c> because nothing here ever writes through this context, and the row is
    /// about to outlive the scope it was read in.
    /// </remarks>
    private async Task<SecurityPolicySnapshot> LoadAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var policy = await dbContext.SecurityPolicies
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == Domain.Entities.SecurityPolicy.SingletonId, cancellationToken);

        if (policy is null)
        {
            return SecurityPolicySnapshot.Default;
        }

        return new SecurityPolicySnapshot(
            policy.MinimumPasswordLength,
            policy.ForceTwoFactorForAdmins,
            policy.MaxFailedLoginAttempts,
            policy.LockoutMinutes);
    }
}
