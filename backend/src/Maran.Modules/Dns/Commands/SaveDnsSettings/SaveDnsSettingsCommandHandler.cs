using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Commands.SaveDnsSettings;

/// <summary>
/// Handles <see cref="SaveDnsSettingsCommand"/>: stores the name servers, the SOA mailbox, the addresses and
/// the default time to live, creating the one settings row the first time.
/// </summary>
/// <remarks>
/// Names are stored lower case so the rendered SOA and NS records have one spelling. Saving does not touch
/// the daemon and does not switch it on or off; <c>EnableDns</c> and <c>DisableDns</c> do that.
/// </remarks>
public sealed class SaveDnsSettingsCommandHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>The clock the change instant is taken from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock.</param>
    public SaveDnsSettingsCommandHandler(DnsDbContext dbContext, DnsAuditJournal journal, IClock clock)
    {
        _dbContext = dbContext;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Saves the settings.</summary>
    /// <param name="command">The validated settings.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The settings as stored.</returns>
    public async Task<Result<DnsSettingsDto>> HandleAsync(
        SaveDnsSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var settings = await _dbContext.DnsSettings
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
        if (settings is null)
        {
            settings = new DnsSettings(now);
            _dbContext.DnsSettings.Add(settings);
        }

        // A field the request left out arrives as null; it is stored as the empty string the entity means by
        // "not stated", never as a null the column refuses.
        settings.Save(
            command.PrimaryNameserver.ToLowerInvariant(),
            (command.SecondaryNameserver ?? string.Empty).ToLowerInvariant(),
            command.HostmasterMailbox.ToLowerInvariant(),
            command.PublicIpv4 ?? string.Empty,
            IpAddressRule.CanonicalIpv6(command.PublicIpv6 ?? string.Empty),
            command.DefaultTtl,
            now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            DnsAuditActions.DnsSettingsSaved,
            settings.PrimaryNameserver,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<DnsSettingsDto>.Ok(DnsSettingsMapper.From(settings));
    }
}
