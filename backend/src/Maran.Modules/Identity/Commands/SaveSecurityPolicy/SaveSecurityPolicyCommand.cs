namespace Maran.Modules.Identity.Commands.SaveSecurityPolicy;

/// <summary>Replaces the panel's security policy with the values an administrator submitted.</summary>
/// <param name="MinimumPasswordLength">The shortest password the panel accepts.</param>
/// <param name="ForceTwoFactorForAdmins">Whether an administrator without a second factor is steered into enrolment.</param>
/// <param name="MaxFailedLoginAttempts">Consecutive failed sign-ins that lock an account.</param>
/// <param name="LockoutMinutes">How long a locked account stays locked, in minutes.</param>
/// <param name="IpAddress">The caller's address, recorded in the journal.</param>
/// <param name="UserAgent">The caller's user agent, recorded in the journal.</param>
public sealed record SaveSecurityPolicyCommand(
    int MinimumPasswordLength,
    bool ForceTwoFactorForAdmins,
    int MaxFailedLoginAttempts,
    int LockoutMinutes,
    string IpAddress,
    string UserAgent);
