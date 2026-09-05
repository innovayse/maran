using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.ValueObjects;
using Maran.Modules.Identity.Models;

namespace Maran.Modules.Identity.Mappers;

/// <summary>
/// Translates a completed sign-in from what the panel knows into what the caller is told.
/// </summary>
/// <remarks>
/// <para>
/// The boundary is here and nowhere else. <see cref="AuthenticatedOutcome"/> carries domain values —
/// an <see cref="AccessToken"/> and the <see cref="User"/> entity — because a handler decides who
/// signed in, not how they are rendered; the wire shape is this file's business alone.
/// </para>
/// <para>
/// It only restates a decision already made. Nothing here inspects a null to work out which case it
/// is in: the caller either holds an authenticated outcome or does not, and that question was
/// answered before this method was reached.
/// </para>
/// </remarks>
public static class AuthenticatedSessionMapper
{
    /// <summary>Renders a completed sign-in as the body the caller receives.</summary>
    /// <param name="authenticated">The sign-in, as the handler produced it.</param>
    /// <returns>The signed-in half of a login response.</returns>
    public static AuthenticatedSessionDto From(AuthenticatedOutcome authenticated)
    {
        ArgumentNullException.ThrowIfNull(authenticated);

        var user = authenticated.User;

        return new AuthenticatedSessionDto(
            authenticated.AccessToken.Value,
            authenticated.AccessToken.ExpiresAt,
            new AuthenticatedUserDto(user.Id, user.Username, user.Email, user.Role, user.AccountId),
            authenticated.AccessToken.RequiresTwoFactorSetup);
    }
}
