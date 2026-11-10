using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Dns.Commands.DeleteZoneTemplate;

/// <summary>
/// Handles <see cref="DeleteZoneTemplateCommand"/>: removes a template the administrator made, unless it ships
/// with the panel or is the template new sites get their zone from (<see cref="ZoneTemplateDeletionPolicy"/>).
/// </summary>
/// <remarks>
/// The default is refused rather than cleared, so the setting never names a row that is gone and a site created
/// afterwards still gets the zone the administrator chose.
/// </remarks>
public sealed class DeleteZoneTemplateCommandHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="journal">This module's audit journal.</param>
    public DeleteZoneTemplateCommandHandler(DnsDbContext dbContext, DnsAuditJournal journal)
    {
        _dbContext = dbContext;
        _journal = journal;
    }

    /// <summary>Deletes the template.</summary>
    /// <param name="command">Which template to delete.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Success, <c>ZoneTemplateNotFound</c>, <c>ZoneTemplateBuiltIn</c> or <c>ZoneTemplateIsDefault</c>.</returns>
    public async Task<Result> HandleAsync(DeleteZoneTemplateCommand command, CancellationToken cancellationToken)
    {
        var template = await _dbContext.ZoneTemplates
            .Include(row => row.Records)
            .SingleOrDefaultAsync(row => row.Id == command.TemplateId, cancellationToken);
        if (template is null)
        {
            return await FailAsync(
                command,
                command.TemplateId.ToString(),
                Error.Of(nameof(ErrorMessages.ZoneTemplateNotFound), ErrorType.NotFound),
                cancellationToken);
        }

        var defaultId = await _dbContext.DnsSettings
            .AsNoTracking()
            .Where(row => row.Id == DnsSettings.SingletonId)
            .Select(row => row.DefaultTemplateId)
            .SingleOrDefaultAsync(cancellationToken);
        var verdict = ZoneTemplateDeletionPolicy.Check(template, defaultId);
        if (verdict != TemplateDeletionVerdict.Allowed)
        {
            var code = verdict == TemplateDeletionVerdict.BuiltIn
                ? nameof(ErrorMessages.ZoneTemplateBuiltIn)
                : nameof(ErrorMessages.ZoneTemplateIsDefault);

            return await FailAsync(command, template.Name, Error.Of(code, ErrorType.Conflict), cancellationToken);
        }

        _dbContext.ZoneTemplates.Remove(template);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            DnsAuditActions.ZoneTemplateDeleted, template.Name, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Ok();
    }

    /// <summary>Journals a refused deletion and returns it as the typed failure.</summary>
    /// <param name="command">The deletion that was refused.</param>
    /// <param name="subject">The template name, or the identifier when no template was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result> FailAsync(
        DeleteZoneTemplateCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.ZoneTemplateDeleted, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result.Fail(error);
    }
}
