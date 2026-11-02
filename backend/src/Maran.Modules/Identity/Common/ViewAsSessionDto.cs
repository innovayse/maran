namespace Maran.Modules.Identity.Common;

/// <summary>
/// The answer to starting a view-as session: the access token that speaks for the viewed login, and
/// what the banner shows (issue #317).
/// </summary>
/// <remarks>
/// Deliberately no refresh token, and no cookie is set beside it: the operator's own refresh cookie is
/// what returns them to their own session, and a view-as session cannot be refreshed at all. The SPA
/// holds this token in memory until Back or until it expires.
/// </remarks>
/// <param name="AccessToken">The signed view-as access token, compact-serialized.</param>
/// <param name="ExpiresAt">When it stops being accepted; the same instant the view-as session ends.</param>
/// <param name="User">The login the token signs in as, in the shape every sign-in answer uses.</param>
/// <param name="Viewing">What the banner shows.</param>
public sealed record ViewAsSessionDto(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    AuthenticatedUserDto User,
    ViewingAsDto Viewing);
