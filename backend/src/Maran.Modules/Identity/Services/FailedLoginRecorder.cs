using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Counts a failed password under a row lock on the login, so concurrent failures are counted one
/// after another and none is lost.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a lock and a reload, and not an in-memory increment.</b> <c>READ COMMITTED</c> gives each
/// statement its own snapshot and EF Core writes the absolute value it computed (<c>= k + 1</c>),
/// so two failures that both loaded <c>k</c> both write <c>k + 1</c>. Taking <c>FOR UPDATE</c> on the
/// login's row makes the second failure WAIT until the first has committed; its reload then reads
/// <c>k + 1</c> and writes <c>k + 2</c>. The Argon2id verification runs BEFORE this, outside the lock,
/// so the lock is held for one small update and never for the length of a password hash.
/// </para>
/// <para>
/// <b>Why not a concurrency token.</b> A token on <c>Users</c> would make every other writer of the
/// row (sign-in stamping, rehash, two-factor, suspension) throw on contention, to protect a counter
/// only this type writes. <b>Stated limit:</b> the lock serialises callers that TAKE it; a path that
/// writes <c>FailedLoginAttempts</c> without coming through here is not held back.
/// </para>
/// </remarks>
public sealed class FailedLoginRecorder : IFailedLoginRecorder
{
    /// <summary>The module's database context, and the connection the lock is taken on.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's security policy, read for the lockout threshold and window.</summary>
    private readonly SecurityPolicyCache _policyCache;

    /// <summary>Creates the recorder.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="policyCache">The panel's security policy, read for the lockout numbers.</param>
    public FailedLoginRecorder(IdentityDbContext dbContext, SecurityPolicyCache policyCache)
    {
        _dbContext = dbContext;
        _policyCache = policyCache;
    }

    /// <inheritdoc />
    public async Task RecordAsync(User user, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var policy = await _policyCache.GetAsync(cancellationToken);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // raw-sql: a row lock (FOR UPDATE) has no LINQ translation and no EF Core equivalent.
        // Parameterized through ExecuteSqlInterpolatedAsync (rules/csharp.md "Data & tenancy"): the
        // login id is a parameter, and the only literals are the module's own schema and table,
        // quoted because the names are PascalCase.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""select 1 from identity."Users" where "Id" = {user.Id} for update""",
            cancellationToken);

        await _dbContext.Entry(user).ReloadAsync(cancellationToken);
        user.RecordFailedLogin(at, policy.MaxFailedLoginAttempts, policy.LockoutDuration());
        await _dbContext.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }
}
