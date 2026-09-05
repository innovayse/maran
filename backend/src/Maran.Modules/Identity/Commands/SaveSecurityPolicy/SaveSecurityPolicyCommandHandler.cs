using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Commands.SaveSecurityPolicy;

/// <summary>
/// Handles <see cref="SaveSecurityPolicyCommand"/> by writing the singleton row and forgetting what
/// the panel had cached (R12).
/// </summary>
/// <remarks>
/// <para>
/// <b>Insert-or-update against a fixed key, never "find the first row".</b> The primary key is the
/// constant <c>SecurityPolicy.SingletonId</c>, so two concurrent saves contend on one row rather than
/// each creating one — and the panel can never end up with two answers to "how long must a password
/// be", whichever of which happened to be loaded first.
/// </para>
/// <para>
/// <b>The cache is invalidated AFTER the commit.</b> Doing it before would let a concurrent read
/// re-cache the old row — the new one is not visible until the transaction commits — and the panel
/// would go on enforcing the previous policy until it was restarted.
/// </para>
/// </remarks>
public sealed class SaveSecurityPolicyCommandHandler
{
    /// <summary>The module's database context, which owns the policy row.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>The panel's cached copy of the policy, dropped once the new one is committed.</summary>
    private readonly SecurityPolicyCache _cache;

    /// <summary>Records the change; every account on the panel is affected by it.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>Who is asking, for the journal entry.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The panel's clock; the ambient one is a banned API (rules/csharp.md).</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="cache">The panel's cached copy of the policy.</param>
    /// <param name="journal">Records the change.</param>
    /// <param name="currentUser">Who is asking.</param>
    /// <param name="clock">The panel's clock, which stamps the row.</param>
    public SaveSecurityPolicyCommandHandler(
        IdentityDbContext dbContext,
        SecurityPolicyCache cache,
        IdentityAuditJournal journal,
        ICurrentUser currentUser,
        IClock clock)
    {
        _dbContext = dbContext;
        _cache = cache;
        _journal = journal;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>Saves the panel's security policy.</summary>
    /// <param name="command">The validated policy.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>Success. The values are the caller's own input; there is nothing to hand back.</returns>
    public async Task<Result<bool>> HandleAsync(
        SaveSecurityPolicyCommand command,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;

        var policy = await _dbContext.SecurityPolicies
            .FirstOrDefaultAsync(row => row.Id == Domain.Entities.SecurityPolicy.SingletonId, cancellationToken);

        if (policy is null)
        {
            policy = new Domain.Entities.SecurityPolicy(
                command.MinimumPasswordLength,
                command.ForceTwoFactorForAdmins,
                command.MaxFailedLoginAttempts,
                command.LockoutMinutes,
                now);

            _dbContext.SecurityPolicies.Add(policy);
        }
        else
        {
            policy.Replace(
                command.MinimumPasswordLength,
                command.ForceTwoFactorForAdmins,
                command.MaxFailedLoginAttempts,
                command.LockoutMinutes,
                now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _cache.Invalidate();

        await _journal.RecordIdentifiedAsync(
            _currentUser.UserId,
            AuditActions.SecurityPolicySaved,
            nameof(Domain.Entities.SecurityPolicy),
            command.IpAddress,
            command.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<bool>.Ok(true);
    }
}
