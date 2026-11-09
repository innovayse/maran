using Maran.Modules.Identity.Domain.Policies;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Applies the panel's password policy to the first password an administrator states for an account
/// owner: not longer than <see cref="PasswordPolicy.MaxLength"/>, not shorter than the operator's
/// minimum, and not the login's own name.
/// </summary>
/// <remarks>
/// The ONE place this verdict is reached. The Accounts module asks it through
/// <see cref="IInitialPasswordPolicy"/> before it provisions anything, and
/// <c>AccountCreatedHandler</c> asks it again when it makes the login, because the event can reach
/// that handler by a path that never went through the first question; both read the same rules, so
/// they cannot disagree.
/// </remarks>
public sealed class InitialPasswordPolicy : IInitialPasswordPolicy
{
    /// <summary>The operator's security policy, read for the minimum password length.</summary>
    private readonly SecurityPolicyCache _policyCache;

    /// <summary>Creates the policy over the operator's cached security policy.</summary>
    /// <param name="policyCache">The operator's security policy.</param>
    public InitialPasswordPolicy(SecurityPolicyCache policyCache)
    {
        _policyCache = policyCache;
    }

    /// <inheritdoc />
    public async Task<bool> AcceptsAsync(SensitiveString password, string username, CancellationToken cancellationToken)
    {
        var policy = await _policyCache.GetAsync(cancellationToken);
        var revealed = password.Reveal();

        return PasswordPolicy.IsWithinMaximumLength(revealed)
            && PasswordPolicy.MeetsMinimumLength(revealed, policy.MinimumPasswordLength)
            && PasswordPolicy.DiffersFromUsername(revealed, username);
    }
}
