using Maran.Modules.Sftp.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Sftp.Queries.ListSftpUsers;

/// <summary>
/// The orders the SFTP logins list accepts. <see cref="ListSftpUsersQueryValidator"/> refuses any other
/// sort and <see cref="ListSftpUsersQueryHandler"/> orders by exactly these, so the whitelist is written
/// once.
/// </summary>
public static class ListSftpUsersSorts
{
    /// <summary>
    /// <see cref="SftpUserSortFields.Name"/> and <see cref="SftpUserSortFields.CreatedAt"/>, oldest first
    /// when none is named — the order the list had before it was paged — with ties broken by the row id.
    /// </summary>
    /// <remarks>
    /// <c>name</c> orders by <see cref="SftpUser.FullName"/>, the login the host holds, and not by the
    /// unprefixed <see cref="SftpUser.Name"/>: the full name is what the list shows, so the column a
    /// reader sorts is the column the rows are ordered by. Within one account the two orders agree,
    /// because every full name there carries the same prefix.
    /// </remarks>
    public static readonly SortMap<SftpUser> Map = new SortMapBuilder<SftpUser>()
        .By(SftpUserSortFields.Name, sftpUser => sftpUser.FullName)
        .By(SftpUserSortFields.CreatedAt, sftpUser => sftpUser.CreatedAt)
        .DefaultTo(SftpUserSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(sftpUser => sftpUser.Id);
}
