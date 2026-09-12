namespace Maran.Modules.Ftp.Queries.ListFtpUsers;

/// <summary>
/// Lists the FTPS logins the caller may see. Takes no account parameter on purpose: the scope comes
/// from the caller's own token through the context's tenant filter, so the query cannot be pointed
/// at somebody else (rules/security.md item 6).
/// </summary>
public sealed record ListFtpUsersQuery;
