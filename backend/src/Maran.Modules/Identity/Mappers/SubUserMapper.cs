using Maran.Modules.Identity.Common;
using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Models;
using Maran.Sdk.Extensions;

namespace Maran.Modules.Identity.Mappers;

/// <summary>
/// Builds a <see cref="SubUserDto"/> from the two shapes a sub-user is read as. One place, because the
/// list, the invite answer and the grants answer all describe the same person, and the set of grants
/// must always be spelled as a list of features and never as the bits.
/// </summary>
public static class SubUserMapper
{
    /// <summary>Projects a list row into its outward view.</summary>
    /// <param name="row">The joined row the list query read.</param>
    /// <returns>The sub-user as the screens see them.</returns>
    public static SubUserDto From(SubUserRow row)
    {
        ArgumentNullException.ThrowIfNull(row);

        return new SubUserDto(
            row.Id, row.Username, row.Email, row.Lifecycle, row.Grants.Members(), row.CreatedAt, row.LastLoginAt);
    }

    /// <summary>Projects a login and its membership into the outward view.</summary>
    /// <param name="user">The sub-user's login.</param>
    /// <param name="membership">The sub-user's membership in the owner's account.</param>
    /// <returns>The sub-user as the screens see them.</returns>
    public static SubUserDto From(User user, AccountMembership membership)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(membership);

        return new SubUserDto(
            user.Id,
            user.Username,
            user.Email,
            user.Lifecycle,
            membership.Grants.Members(),
            user.CreatedAt,
            user.LastLoginAt);
    }
}
