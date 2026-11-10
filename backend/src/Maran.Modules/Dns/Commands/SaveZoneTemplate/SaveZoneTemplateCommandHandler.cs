using Maran.Modules.Dns.Common;
using Maran.Modules.Dns.Domain.Entities;
using Maran.Modules.Dns.Domain.Enums;
using Maran.Modules.Dns.Domain.Policies;
using Maran.Modules.Dns.Domain.ValueObjects;
using Maran.Modules.Dns.Mappers;
using Maran.Modules.Dns.Options;
using Maran.Modules.Dns.Persistence;
using Maran.Modules.Dns.Resources;
using Maran.Modules.Dns.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Dns.Commands.SaveZoneTemplate;

/// <summary>
/// Handles <see cref="SaveZoneTemplateCommand"/>: weighs the template as a whole and stores it, creating it or
/// replacing the one the route names.
/// </summary>
/// <remarks>
/// <para>
/// <b>A template is judged by the law of a zone, not by a law of its own.</b> Each record passes
/// <see cref="TemplateRecordRules"/> (the closed variables, then <see cref="RecordShapePolicy"/> on the sample
/// expansion). The records are then added to a throwaway zone of the sample name through
/// <see cref="Zone.AddTemplateRecords"/>, which is the very path a real zone is created through, so the cap
/// and the rules between records (a CNAME beside other data, a duplicate) are enforced here and not discovered
/// when the first customer creates a zone from it.
/// </para>
/// <para>
/// The validator holds the same per-record rule at the boundary; it is repeated here because a command is also
/// dispatched on the bus, where no validator ran.
/// </para>
/// </remarks>
public sealed class SaveZoneTemplateCommandHandler
{
    /// <summary>The Dns module's database context.</summary>
    private readonly DnsDbContext _dbContext;

    /// <summary>The operator's settings: how many records a zone may hold.</summary>
    private readonly IOptions<DnsOptions> _options;

    /// <summary>This module's audit journal.</summary>
    private readonly DnsAuditJournal _journal;

    /// <summary>The clock the change instant is taken from.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Dns module's database context.</param>
    /// <param name="options">The operator's settings.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock.</param>
    public SaveZoneTemplateCommandHandler(
        DnsDbContext dbContext,
        IOptions<DnsOptions> options,
        DnsAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _options = options;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Saves the template.</summary>
    /// <param name="command">The validated request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The template as stored, or the typed reason it was refused.</returns>
    public async Task<Result<ZoneTemplateDetailDto>> HandleAsync(
        SaveZoneTemplateCommand command,
        CancellationToken cancellationToken)
    {
        var name = TemplateNamePolicy.Normalize(command.Name);
        var description = command.Description ?? string.Empty;
        var records = (command.Records ?? Array.Empty<TemplateRecordDraft>())
            .Select(TemplateExpansionPolicy.Normalize)
            .ToList();

        var refusal = FindRefusal(name, description, records);
        if (refusal is not null)
        {
            return await FailAsync(command, name, refusal, cancellationToken);
        }

        var now = _clock.UtcNow;
        var creating = command.TemplateId == Guid.Empty;
        var template = creating
            ? new ZoneTemplate(Guid.NewGuid(), name, description, isBuiltIn: false, now)
            : await _dbContext.ZoneTemplates
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

        var key = TemplateNamePolicy.Key(name);
        var taken = await _dbContext.ZoneTemplates
            .AsNoTracking()
            .AnyAsync(row => row.NameKey == key && row.Id != template.Id, cancellationToken);
        if (taken)
        {
            return await FailAsync(
                command, name, Error.Of(nameof(ErrorMessages.ZoneTemplateNameTaken), ErrorType.Conflict), cancellationToken);
        }

        template.Replace(name, description, records, now);
        if (creating)
        {
            _dbContext.ZoneTemplates.Add(template);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The unique index on the name refused: another save of the same name won the race.
            return await FailAsync(
                command, name, Error.Of(nameof(ErrorMessages.ZoneTemplateNameTaken), ErrorType.Conflict), cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            DnsAuditActions.ZoneTemplateSaved, template.Name, command.IpAddress, command.UserAgent, cancellationToken);

        var defaultId = await _dbContext.DnsSettings
            .AsNoTracking()
            .Where(row => row.Id == DnsSettings.SingletonId)
            .Select(row => row.DefaultTemplateId)
            .SingleOrDefaultAsync(cancellationToken);

        return Result<ZoneTemplateDetailDto>.Ok(ZoneTemplateMapper.DetailFrom(template, defaultId));
    }

    /// <summary>Weighs everything about the request that can be known without reading the database.</summary>
    /// <param name="name">The normalised name.</param>
    /// <param name="description">The description.</param>
    /// <param name="records">The normalised records.</param>
    /// <returns>The first refusal, or null when the template may be stored.</returns>
    private Error? FindRefusal(string name, string description, IReadOnlyList<TemplateRecordDraft> records)
    {
        if (!TemplateNamePolicy.IsNameValid(name))
        {
            return Error.Of(nameof(ErrorMessages.ZoneTemplateNameInvalid), ErrorType.Validation);
        }

        if (!TemplateNamePolicy.IsDescriptionValid(description))
        {
            return Error.Of(nameof(ErrorMessages.ZoneTemplateDescriptionInvalid), ErrorType.Validation);
        }

        foreach (var record in records)
        {
            var verdict = TemplateRecordRules.Check(record);
            if (!verdict.IsAccepted)
            {
                return Error.Of(TemplateRecordMessages.KeyOf(verdict), ErrorType.Validation);
            }
        }

        // The throwaway zone is never stored: it only lends the template the law a real zone is held to.
        if (!TemplateExpansionPolicy.TryExpand(records, TemplateContext.Sample, out var expansion))
        {
            return Error.Of(nameof(ErrorMessages.ZoneTemplateTokenUnbalanced), ErrorType.Validation);
        }

        var sample = new Zone(Guid.NewGuid(), Guid.Empty, TemplateContext.Sample.Apex, _clock.UtcNow);
        var verdictOfZone = sample.AddTemplateRecords(expansion!.Drafts, _options.Value.MaxRecordsPerZone, _clock.UtcNow);

        return verdictOfZone switch
        {
            RecordRuleVerdict.Accepted => null,
            RecordRuleVerdict.ZoneFull => Error.Of(nameof(ErrorMessages.ZoneTemplateTooManyRecords), ErrorType.Conflict),
            _ => RecordRuleErrors.From(verdictOfZone),
        };
    }

    /// <summary>Journals a refused save and returns it as the typed failure.</summary>
    /// <param name="command">The save that was refused.</param>
    /// <param name="subject">The template name the caller supplied, or the identifier when no template was found.</param>
    /// <param name="error">The typed failure to answer with.</param>
    /// <param name="cancellationToken">Cancels the journal write.</param>
    /// <returns>The failed result carrying <paramref name="error"/>.</returns>
    private async Task<Result<ZoneTemplateDetailDto>> FailAsync(
        SaveZoneTemplateCommand command,
        string subject,
        Error error,
        CancellationToken cancellationToken)
    {
        await _journal.RecordFailureAsync(
            DnsAuditActions.ZoneTemplateSaved, subject, command.IpAddress, command.UserAgent, cancellationToken);

        return Result<ZoneTemplateDetailDto>.Fail(error);
    }
}
