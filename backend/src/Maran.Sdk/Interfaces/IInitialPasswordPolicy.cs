using Maran.SharedKernel.Security;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Answers whether a password an administrator chose for someone else's first sign-in meets the
/// panel's password policy. A read-only window implemented by the module that owns the policy
/// (Identity), so a module that creates a login by message can refuse a weak password BEFORE it does
/// anything that cannot be undone, without holding a second definition of "strong enough"
/// (rules/architecture.md "A shared facility's contract lives in Maran.Sdk").
/// </summary>
/// <remarks>
/// It answers a verdict and never says which rule failed: the caller refuses with one code, and the
/// password is revealed only inside the implementation, which is the one place that hashes it.
/// </remarks>
public interface IInitialPasswordPolicy
{
    /// <summary>Whether <paramref name="password"/> is acceptable as the first password of the login <paramref name="username"/>.</summary>
    /// <param name="password">The password the administrator chose.</param>
    /// <param name="username">The login's name, which a password may not equal.</param>
    /// <param name="cancellationToken">Cancels the read of the operator's policy.</param>
    /// <returns>True when the password meets every rule the panel applies to a password a person sets.</returns>
    Task<bool> AcceptsAsync(SensitiveString password, string username, CancellationToken cancellationToken);
}
