using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Persistence;

namespace Maran.Modules.Mail.Services;

/// <summary>Reads the one fact every customer operation depends on: whether an administrator turned mail on.</summary>
/// <remarks>
/// A customer cannot create a domain, a mailbox or an alias on a server whose daemons are off: the host
/// would be asked to configure something that nothing serves. Asked of the settings row, which is the
/// administrator's stated intention, and not of the host, so a request does not pay an agent round trip
/// to learn what the panel already knows.
/// </remarks>
public sealed class MailServerReader
{
    /// <summary>The Mail module's context.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>Creates the reader.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    public MailServerReader(MailDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Reads the settings row.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or <c>null</c> when mail was never enabled on this server.</returns>
    public async Task<MailServerSettings?> FindAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.MailServerSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Reads the settings row, only when the administrator wants the daemons running.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The row, or <c>null</c> when mail was never enabled or has been disabled.</returns>
    public async Task<MailServerSettings?> FindEnabledAsync(CancellationToken cancellationToken)
    {
        var settings = await FindAsync(cancellationToken);

        return settings is { Enabled: true } ? settings : null;
    }
}
