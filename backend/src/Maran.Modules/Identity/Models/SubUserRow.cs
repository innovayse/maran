using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// One sub-user as the list query reads it: the member's login joined with their membership in the
/// active account. The page is cut over this shape so search, sort and the total are all computed in
/// the database over the same rows.
/// </summary>
/// <param name="Id">The login's id.</param>
/// <param name="Username">The login name.</param>
/// <param name="Email">The login's address.</param>
/// <param name="Lifecycle">Whether the login has accepted its invitation, and whether its account is suspended.</param>
/// <param name="Grants">The membership's grant set.</param>
/// <param name="CreatedAt">When the login was created.</param>
/// <param name="LastLoginAt">The most recent sign-in, or <c>null</c>.</param>
public sealed record SubUserRow(
    Guid Id,
    string Username,
    string Email,
    UserLifecycle Lifecycle,
    AccountFeature Grants,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
