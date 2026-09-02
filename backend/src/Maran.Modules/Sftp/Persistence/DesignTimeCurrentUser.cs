namespace Maran.Modules.Sftp.Persistence;

/// <summary>
/// The <see cref="ICurrentUser"/> handed to <see cref="SftpDbContext"/> by
/// <see cref="DesignTimeDbContextFactory"/>. Exists only so EF Core's design-time tooling can build
/// the MODEL — the shape of the tables — which it cannot do without constructing the context.
/// </summary>
/// <remarks>
/// It reports an administrator because the tenant filter must not narrow a generated migration:
/// a migration describes the table, and a filtered model would still describe the same table, but
/// making that depend on a fabricated tenant id is a trap for the next reader. Nothing here ever
/// runs at runtime — the Host registers the real <c>ICurrentUser</c> through DI — and no query is
/// ever executed against this principal.
/// </remarks>
public sealed class DesignTimeCurrentUser : ICurrentUser
{
    /// <inheritdoc />
    public Guid UserId
    {
        get
        {
            return Guid.Empty;
        }
    }

    /// <inheritdoc />
    public string Username
    {
        get
        {
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public Guid? AccountId
    {
        get
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool IsAdmin
    {
        get
        {
            return true;
        }
    }
}
