namespace Maran.Modules.Identity.Controllers.Requests;

/// <summary>The body of a security-policy save.</summary>
/// <param name="MinimumPasswordLength">The shortest password the panel accepts.</param>
/// <param name="ForceTwoFactorForAdmins">Whether an administrator without a second factor is steered into enrolment.</param>
/// <param name="MaxFailedLoginAttempts">Consecutive failed sign-ins that lock an account.</param>
/// <param name="LockoutMinutes">How long a locked account stays locked, in minutes.</param>
public sealed record SaveSecurityPolicyRequest(
    int MinimumPasswordLength,
    bool ForceTwoFactorForAdmins,
    int MaxFailedLoginAttempts,
    int LockoutMinutes);
