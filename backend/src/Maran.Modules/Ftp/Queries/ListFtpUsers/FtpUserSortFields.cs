namespace Maran.Modules.Ftp.Queries.ListFtpUsers;

/// <summary>
/// The public names the FTPS logins list sorts by — the <c>sort</c> parameter's vocabulary, written
/// once and read by <see cref="ListFtpUsersSorts.Map"/>, the tests and anything else that names a sort.
/// </summary>
public static class FtpUserSortFields
{
    /// <summary>The login as the host holds it, which is the name a screen shows and a client types.</summary>
    public const string Name = "name";

    /// <summary>When the login was created.</summary>
    public const string CreatedAt = "createdAt";
}
