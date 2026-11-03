using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// One sub-user as the list query reads it: the member's login joined with their membership in the
/// active account. The page is cut over this shape so search, sort and the total are all computed in
/// the database over the same rows.
/// </summary>
/// <remarks>
/// <b>Initialised by member, not by constructor, because the pager sorts over it.</b> The sort map
/// reads a member of this row inside the database query, and EF Core can bind a member of a
/// projection only when the projection names its members (an object initializer); a positional
/// constructor call leaves it nothing to bind the sort to, and the query is refused as untranslatable.
/// </remarks>
public sealed record SubUserRow
{
    /// <summary>The login's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>The login name.</summary>
    public required string Username { get; init; }

    /// <summary>The login's address.</summary>
    public required string Email { get; init; }

    /// <summary>
    /// Whether the login has accepted its invitation, or <c>null</c> for an open offer, which is
    /// addressed to a login that already exists and has no invitation of its own. Read the meaning
    /// through <see cref="State"/>.
    /// </summary>
    public required UserLifecycle? Lifecycle { get; init; }

    /// <summary>
    /// What the owner's list shows: the login's lifecycle, and <see cref="UserLifecycle.Invited"/> for an
    /// open offer, which is an invitation that has not been taken up.
    /// </summary>
    public UserLifecycle State
    {
        get
        {
            return Lifecycle ?? UserLifecycle.Invited;
        }
    }

    /// <summary>The membership's grant set.</summary>
    public required AccountFeature Grants { get; init; }

    /// <summary>When the login was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The most recent sign-in, or <c>null</c>.</summary>
    public required DateTimeOffset? LastLoginAt { get; init; }
}
