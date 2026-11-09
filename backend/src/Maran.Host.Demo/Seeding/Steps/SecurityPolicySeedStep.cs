using Maran.Host.Demo.Seeding.Interfaces;
using Maran.Host.Demo.Seeding.Models;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Services;
using Microsoft.EntityFrameworkCore;

namespace Maran.Host.Demo.Seeding.Steps;

/// <summary>
/// Writes the security policy the public demonstration runs under: no second factor is forced, and a
/// username is never locked out by failed attempts.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not the save command.</b> The published sign-ins are public, so ten wrong passwords from
/// any visitor would lock the demo out for everybody. The save command's validator allows at most 100
/// attempts, which is a ceiling a script reaches. The row is written directly with a ceiling no
/// sequence of failures reaches, and the screen that would edit it is read-only in the demo.
/// </para>
/// <para>
/// Per-address throttling of sign-ins is a separate control and stays on.
/// </para>
/// </remarks>
public sealed class SecurityPolicySeedStep : IDemoSeedStep
{
    /// <summary>The failed-attempt ceiling meaning "never locked by failures".</summary>
    public const int NeverLockedAttempts = int.MaxValue;

    /// <summary>The shortest lockout, kept so the stored value stays a legal one.</summary>
    private const int LockoutMinutes = 1;

    /// <summary>Opens the scope the identity context is resolved in.</summary>
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Reads the policy of this process, dropped after the row changes.</summary>
    private readonly SecurityPolicyCache _cache;

    /// <summary>Reads the instant.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the step.</summary>
    /// <param name="scopeFactory">Opens the scope the identity context is resolved in.</param>
    /// <param name="cache">Reads the policy of this process.</param>
    /// <param name="clock">Reads the instant.</param>
    public SecurityPolicySeedStep(IServiceScopeFactory scopeFactory, SecurityPolicyCache cache, IClock clock)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _clock = clock;
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "security-policy"; }
    }

    /// <inheritdoc />
    public async Task RunAsync(DemoSeedContext context, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var policy = await dbContext.SecurityPolicies
            .SingleOrDefaultAsync(row => row.Id == SecurityPolicy.SingletonId, cancellationToken);
        if (policy is null)
        {
            dbContext.SecurityPolicies.Add(new SecurityPolicy(
                SecurityPolicy.DefaultMinimumPasswordLength,
                forceTwoFactorForAdmins: false,
                forceTwoFactorForResellers: false,
                NeverLockedAttempts,
                LockoutMinutes,
                _clock.UtcNow));
        }
        else
        {
            policy.Replace(
                SecurityPolicy.DefaultMinimumPasswordLength,
                forceTwoFactorForAdmins: false,
                forceTwoFactorForResellers: false,
                NeverLockedAttempts,
                LockoutMinutes,
                _clock.UtcNow);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        _cache.Invalidate();
    }
}
