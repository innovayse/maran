namespace Maran.Modules.Identity.Common;

/// <summary>
/// One signed-in device, as shown on the sessions screen. Carries no token and no hash of one —
/// there is no field a secret could occupy, which is why "the list never leaks a token" is a
/// property of the type rather than a rule someone has to keep.
/// </summary>
/// <param name="Id">The session's identity, used to revoke it.</param>
/// <param name="IssuedAt">When the device signed in.</param>
/// <param name="ExpiresAt">When it will be signed out unless it refreshes.</param>
/// <param name="IpAddress">Where it signed in from.</param>
/// <param name="UserAgent">What client it signed in with.</param>
/// <param name="IsCurrent">True for the device making this request, so the UI can warn before ending it.</param>
public sealed record SessionDto(
    Guid Id,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    string IpAddress,
    string UserAgent,
    bool IsCurrent);
