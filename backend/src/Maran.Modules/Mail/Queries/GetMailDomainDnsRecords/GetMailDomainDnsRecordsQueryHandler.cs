using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Persistence;
using Maran.Modules.Mail.Resources;
using Maran.Modules.Mail.Services;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Mail.Queries.GetMailDomainDnsRecords;

/// <summary>
/// Handles <see cref="GetMailDomainDnsRecordsQuery"/>: the MX, SPF, DKIM and DMARC records a domain
/// publishes, computed from the domain, its keys that are still published and the server's host name.
/// </summary>
/// <remarks>
/// Read through the tenant-filtered context, so another account's domain answers
/// <c>MailDomainNotFound</c> exactly as a missing one does. The records are computed, never stored:
/// nothing here can drift from the keys the domain actually has.
/// </remarks>
public sealed class GetMailDomainDnsRecordsQueryHandler
{
    /// <summary>The Mail module's database context, and this module's tenant boundary.</summary>
    private readonly MailDbContext _dbContext;

    /// <summary>The mail server's settings row, whose host name is the MX target.</summary>
    private readonly MailServerReader _server;

    /// <summary>The module's localized display names.</summary>
    private readonly IStringLocalizer<DisplayNames> _names;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Mail module's database context.</param>
    /// <param name="server">Reads the mail server's settings.</param>
    /// <param name="names">The module's localized display names.</param>
    public GetMailDomainDnsRecordsQueryHandler(
        MailDbContext dbContext,
        MailServerReader server,
        IStringLocalizer<DisplayNames> names)
    {
        _dbContext = dbContext;
        _server = server;
        _names = names;
    }

    /// <summary>Returns the domain's records.</summary>
    /// <param name="query">The domain to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The records, or <c>MailDomainNotFound</c>, or <c>MailNotEnabled</c> when no server host name is set.</returns>
    public async Task<Result<IReadOnlyList<MailDnsRecordDto>>> HandleAsync(
        GetMailDomainDnsRecordsQuery query,
        CancellationToken cancellationToken)
    {
        var domain = await _dbContext.MailDomains
            .AsNoTracking()
            .Include(row => row.DkimKeys)
            .SingleOrDefaultAsync(row => row.Id == query.MailDomainId, cancellationToken);
        if (domain is null)
        {
            return Result<IReadOnlyList<MailDnsRecordDto>>.Fail(
                Error.Of(nameof(ErrorMessages.MailDomainNotFound), ErrorType.NotFound));
        }

        var settings = await _server.FindAsync(cancellationToken);
        if (settings is null)
        {
            return Result<IReadOnlyList<MailDnsRecordDto>>.Fail(
                Error.Of(nameof(ErrorMessages.MailNotEnabled), ErrorType.Conflict));
        }

        IReadOnlyList<MailDnsRecordDto> records = MailDnsRecordPolicy.For(domain, settings.Hostname)
            .Select(record =>
            {
                return MailDomainMapper.From(record, _names);
            })
            .ToList();

        return Result<IReadOnlyList<MailDnsRecordDto>>.Ok(records);
    }
}
