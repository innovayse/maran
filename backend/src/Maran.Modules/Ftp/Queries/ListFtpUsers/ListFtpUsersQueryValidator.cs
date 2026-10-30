using Maran.Modules.Ftp.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Ftp.Queries.ListFtpUsers;

/// <summary>
/// Holds <see cref="ListFtpUsersQuery"/> to the panel's paging bounds (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The list's only typed filter is an account id, which the binder either parses as a GUID or refuses,
/// so there is nothing beyond the paging contract for this validator to add.
/// </remarks>
public sealed class ListFtpUsersQueryValidator : PagedQueryValidator<ListFtpUsersQuery, FtpUser>
{
    /// <summary>Declares the paging rules against <see cref="ListFtpUsersSorts.Map"/>.</summary>
    public ListFtpUsersQueryValidator()
        : base(ListFtpUsersSorts.Map)
    {
    }
}
