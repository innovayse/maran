namespace Maran.Modules.Identity.Common;

/// <summary>The panel's security policy as the settings screen reads it.</summary>
/// <param name="MinimumPasswordLength">The shortest password the panel accepts.</param>
/// <param name="ForceTwoFactorForAdmins">Whether an administrator without a second factor is steered into enrolment.</param>
/// <param name="MaxFailedLoginAttempts">Consecutive failed sign-ins that lock an account.</param>
/// <param name="LockoutMinutes">How long a locked account stays locked, in minutes.</param>
public sealed record SecurityPolicyDto(
    int MinimumPasswordLength,
    bool ForceTwoFactorForAdmins,
    int MaxFailedLoginAttempts,
    int LockoutMinutes);
