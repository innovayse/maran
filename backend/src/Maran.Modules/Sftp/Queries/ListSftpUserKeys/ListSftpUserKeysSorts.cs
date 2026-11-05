using Maran.Modules.Sftp.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Sftp.Queries.ListSftpUserKeys;

/// <summary>
/// The orders the keys list accepts. <see cref="ListSftpUserKeysQueryValidator"/> refuses any other
/// sort and <see cref="ListSftpUserKeysQueryHandler"/> orders by exactly these, so the whitelist is
/// written once.
/// </summary>
public static class ListSftpUserKeysSorts
{
    /// <summary>
    /// <see cref="SftpUserKeySortFields.Label"/> and <see cref="SftpUserKeySortFields.CreatedAt"/>,
    /// oldest first when none is named, with ties broken by the key's id.
    /// </summary>
    public static readonly SortMap<SftpUserKey> Map = new SortMapBuilder<SftpUserKey>()
        .By(SftpUserKeySortFields.Label, key => key.Label)
        .By(SftpUserKeySortFields.CreatedAt, key => key.CreatedAt)
        .DefaultTo(SftpUserKeySortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(key => key.Id);
}
