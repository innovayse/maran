using Maran.Modules.Sftp.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Sftp.Queries.ListSftpUsers;

/// <summary>
/// Holds <see cref="ListSftpUsersQuery"/> to the panel's paging bounds (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The list's only typed filter is an account id, which the binder either parses as a GUID or refuses,
/// so there is nothing beyond the paging contract for this validator to add.
/// </remarks>
public sealed class ListSftpUsersQueryValidator : PagedQueryValidator<ListSftpUsersQuery, SftpUser>
{
    /// <summary>Declares the paging rules against <see cref="ListSftpUsersSorts.Map"/>.</summary>
    public ListSftpUsersQueryValidator()
        : base(ListSftpUsersSorts.Map)
    {
    }
}
