using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Common;

/// <summary>
/// The signed-in person's own profile: who they are and the state of their sign-in security. It
/// describes the USER, never a hosting account — an administrator owns no account and still has a
/// profile, so nothing here is borrowed from the account a customer happens to own.
/// </summary>
/// <param name="Username">The login name.</param>
/// <param name="Email">The contact address password-reset links are sent to.</param>
/// <param name="Role">What the user is allowed to reach.</param>
/// <param name="IsTwoFactorEnabled">True when a second factor is enrolled and required at sign-in.</param>
/// <param name="ActiveSessionCount">How many devices are signed in right now, this one included.</param>
public sealed record ProfileDto(
    string Username,
    string Email,
    UserRole Role,
    bool IsTwoFactorEnabled,
    int ActiveSessionCount);
