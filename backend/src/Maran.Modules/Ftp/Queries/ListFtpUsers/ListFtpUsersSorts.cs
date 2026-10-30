using Maran.Modules.Ftp.Domain.Entities;
using Maran.SharedKernel.Enums;
using Maran.SharedKernel.Pagination;

namespace Maran.Modules.Ftp.Queries.ListFtpUsers;

/// <summary>
/// The orders the FTPS logins list accepts. <see cref="ListFtpUsersQueryValidator"/> refuses any other
/// sort and <see cref="ListFtpUsersQueryHandler"/> orders by exactly these, so the whitelist is written
/// once.
/// </summary>
public static class ListFtpUsersSorts
{
    /// <summary>
    /// <see cref="FtpUserSortFields.Name"/> and <see cref="FtpUserSortFields.CreatedAt"/>, oldest first
    /// when none is named — the order the list had before it was paged — with ties broken by the row id.
    /// </summary>
    /// <remarks>
    /// <c>name</c> orders by <see cref="FtpUser.FullName"/>, the login the host holds, and not by the
    /// unprefixed <see cref="FtpUser.Name"/>: the full name is what the list shows, so the column a
    /// reader sorts is the column the rows are ordered by. Within one account the two orders agree,
    /// because every full name there carries the same prefix.
    /// </remarks>
    public static readonly SortMap<FtpUser> Map = new SortMapBuilder<FtpUser>()
        .By(FtpUserSortFields.Name, ftpUser => ftpUser.FullName)
        .By(FtpUserSortFields.CreatedAt, ftpUser => ftpUser.CreatedAt)
        .DefaultTo(FtpUserSortFields.CreatedAt, SortDirection.Ascending)
        .TieBreaker(ftpUser => ftpUser.Id);
}
