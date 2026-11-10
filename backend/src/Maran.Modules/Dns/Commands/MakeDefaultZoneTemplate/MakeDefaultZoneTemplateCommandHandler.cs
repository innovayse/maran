using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Commands.MakeDefaultZoneTemplate;

/// <summary>
/// Handles <see cref="MakeDefaultZoneTemplateCommand"/>: records which template new sites get their zone from,
/// creating the settings row if none was ever saved.
/// </summary>
public sealed class MakeDefaultZoneTemplateCommandHandler
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
    public MakeDefaultZoneTemplateCommandHandler(DnsDbContext dbContext, DnsAuditJournal journal, IClock clock)
    {
        _dbContext = dbContext;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Makes the template the default.</summary>
    /// <param name="command">Which template.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, or <c>ZoneTemplateNotFound</c>.</returns>
    public async Task<Result> HandleAsync(MakeDefaultZoneTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await _dbContext.ZoneTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == command.TemplateId, cancellationToken);
        if (template is null)
        {
            await _journal.RecordFailureAsync(
                DnsAuditActions.ZoneTemplateDefaultChanged,
                command.TemplateId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result.Fail(Error.Of(nameof(ErrorMessages.ZoneTemplateNotFound), ErrorType.NotFound));
        }

        var now = _clock.UtcNow;
        var settings = await _dbContext.DnsSettings
            .SingleOrDefaultAsync(row => row.Id == DnsSettings.SingletonId, cancellationToken);
        if (settings is null)
        {
            settings = new DnsSettings(now);
            _dbContext.DnsSettings.Add(settings);
        }

        settings.ChooseDefaultTemplate(template.Id, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            DnsAuditActions.ZoneTemplateDefaultChanged,
            template.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result.Ok();
    }
}
