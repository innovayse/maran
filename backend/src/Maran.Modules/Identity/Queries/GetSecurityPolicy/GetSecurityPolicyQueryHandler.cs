using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Services;

namespace Maran.Modules.Identity.Queries.GetSecurityPolicy;

/// <summary>Handles <see cref="GetSecurityPolicyQuery"/> by reading the panel's cached policy.</summary>
/// <remarks>
/// It reads through the cache rather than the table so the screen shows what the panel is ACTUALLY
/// enforcing. Those are the same thing when the cache is correct — and when they are not, an
/// administrator debugging a policy that "did not take effect" needs to see the value in force, not
/// the value in the row.
/// </remarks>
public sealed class GetSecurityPolicyQueryHandler
{
    /// <summary>The panel's cached security policy.</summary>
    private readonly SecurityPolicyCache _cache;

    /// <summary>Creates the handler.</summary>
    /// <param name="cache">The panel's cached security policy.</param>
    public GetSecurityPolicyQueryHandler(SecurityPolicyCache cache)
    {
        _cache = cache;
    }

    /// <summary>Reads the policy in force.</summary>
    /// <param name="query">The query; it carries no parameters.</param>
    /// <param name="cancellationToken">Cancellation token for the request.</param>
    /// <returns>The policy, which is the built-in defaults on a panel that has never saved one.</returns>
    public async Task<Result<SecurityPolicyDto>> HandleAsync(
        GetSecurityPolicyQuery query,
        CancellationToken cancellationToken)
    {
        var policy = await _cache.GetAsync(cancellationToken);

        return Result<SecurityPolicyDto>.Ok(new SecurityPolicyDto(
            policy.MinimumPasswordLength,
            policy.ForceTwoFactorForAdmins,
            policy.MaxFailedLoginAttempts,
            policy.LockoutMinutes));
    }
}
