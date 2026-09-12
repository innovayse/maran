namespace Maran.Modules.Ftp.Queries.GetFtpUser;

/// <summary>Reads one FTPS login.</summary>
/// <param name="FtpUserId">The login to read; another tenant's id answers "not found".</param>
public sealed record GetFtpUserQuery(Guid FtpUserId);
