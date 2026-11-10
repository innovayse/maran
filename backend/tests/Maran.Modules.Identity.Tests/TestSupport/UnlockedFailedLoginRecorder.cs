using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Interfaces;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Tests.TestSupport;

/// <summary>
/// The failed-login recorder as this project can run it: the same reload, the same entity rule and
/// the same save as the real one, without the PostgreSQL row lock.
/// </summary>
/// <remarks>
/// <b>UNOBSERVED HERE: that simultaneous failures are all counted.</b> That is a property of the row
/// lock, invisible to a provider with no locks, and is measured against real PostgreSQL by
/// <c>Maran.Host.IntegrationTests.ParallelFailedLoginCountTests</c>.
/// </remarks>
public sealed class UnlockedFailedLoginRecorder : IFailedLoginRecorder
{
    /// <summary>The module's database.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The policy the lockout numbers are read from.</summary>
    private readonly SecurityPolicyCache _policyCache;

    /// <summary>Creates the stand-in.</summary>
    /// <param name="dbContext">The module's database.</param>
    /// <param name="policyCache">The policy the lockout numbers are read from.</param>
    public UnlockedFailedLoginRecorder(IdentityDbContext dbContext, SecurityPolicyCache policyCache)
    {
        _dbContext = dbContext;
        _policyCache = policyCache;
    }

    /// <inheritdoc />
    public async Task RecordAsync(User user, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var policy = await _policyCache.GetAsync(cancellationToken);
        await _dbContext.Entry(user).ReloadAsync(cancellationToken);
        user.RecordFailedLogin(at, policy.MaxFailedLoginAttempts, policy.LockoutDuration());
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
