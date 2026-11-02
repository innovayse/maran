using Maran.Modules.Identity.Domain.Enums;

namespace Maran.Modules.Identity.Common;

/// <summary>What the SPA is told about the person it has just signed in.</summary>
/// <param name="Id">The user's identity.</param>
/// <param name="Username">The login name, shown in the shell's user block.</param>
/// <param name="Email">The contact address, shown on the profile screen.</param>
/// <param name="Role">What the user is allowed to reach, so the SPA can hide what it must not offer.</param>
/// <param name="AccountId">
/// The hosting account the sign-in is acting for (the account claim of its token); null for an
/// administrator, who acts for none. A customer who holds several accounts can switch it
/// (<c>POST /api/v1/me/active-account</c>).
/// </param>
public sealed record AuthenticatedUserDto(
    Guid Id,
    string Username,
    string Email,
    UserRole Role,
    Guid? AccountId);
