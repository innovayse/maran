using Maran.Modules.Sftp.Domain.Entities;
using Maran.Sdk.Validators;

namespace Maran.Modules.Sftp.Queries.ListSftpUserKeys;

/// <summary>
/// Holds <see cref="ListSftpUserKeysQuery"/> to the panel's paging bounds (rules/security.md "Input").
/// </summary>
/// <remarks>
/// The login id comes from the route and the search is bound to a validated term, so there is nothing
/// beyond the paging contract for this validator to add.
/// </remarks>
public sealed class ListSftpUserKeysQueryValidator : PagedQueryValidator<ListSftpUserKeysQuery, SftpUserKey>
{
    /// <summary>Declares the paging rules against <see cref="ListSftpUserKeysSorts.Map"/>.</summary>
    public ListSftpUserKeysQueryValidator()
        : base(ListSftpUserKeysSorts.Map)
    {
    }
}
