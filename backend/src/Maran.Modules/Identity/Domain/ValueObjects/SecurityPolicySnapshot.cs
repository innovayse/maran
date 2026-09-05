using Maran.Modules.Identity.Domain.Entities;

namespace Maran.Modules.Identity.Domain.ValueObjects;

/// <summary>
/// A detached copy of the panel's security policy, safe to hold outside the scope that read it.
/// </summary>
/// <remarks>
/// <para>
/// A record rather than the entity itself because <c>SecurityPolicyCache</c> is a singleton
/// and the entity is materialised by a scoped <c>DbContext</c>. Handing the tracked entity out would
/// keep a disposed context's change tracker alive for the life of the process, and any caller could
/// mutate the panel's policy by assigning to it.
/// </para>
/// <para>
/// <see cref="Default"/> is what a panel that has never saved a policy obeys. Its absence is a
/// legitimate state — every fresh installation is in it — so the cache answers with the defaults
/// rather than with <c>null</c>, and no caller has to decide what "no policy" means.
/// </para>
/// </remarks>
/// <param name="MinimumPasswordLength">The shortest password the panel accepts.</param>
/// <param name="ForceTwoFactorForAdmins">Whether an administrator without a second factor is steered into enrolment.</param>
/// <param name="MaxFailedLoginAttempts">Consecutive failed sign-ins that lock an account.</param>
/// <param name="LockoutMinutes">How long a locked account stays locked, in minutes.</param>
public sealed record SecurityPolicySnapshot(
    int MinimumPasswordLength,
    bool ForceTwoFactorForAdmins,
    int MaxFailedLoginAttempts,
    int LockoutMinutes)
{
    /// <summary>What a panel with no saved policy obeys: the constants on <see cref="SecurityPolicy"/>.</summary>
    public static readonly SecurityPolicySnapshot Default = new(
        SecurityPolicy.DefaultMinimumPasswordLength,
        SecurityPolicy.DefaultForceTwoFactorForAdmins,
        SecurityPolicy.DefaultMaxFailedLoginAttempts,
        SecurityPolicy.DefaultLockoutMinutes);

    /// <summary>How long a locked account stays locked.</summary>
    /// <returns>The lockout duration built from <see cref="LockoutMinutes"/>.</returns>
    public TimeSpan LockoutDuration()
    {
        return TimeSpan.FromMinutes(LockoutMinutes);
    }
}
