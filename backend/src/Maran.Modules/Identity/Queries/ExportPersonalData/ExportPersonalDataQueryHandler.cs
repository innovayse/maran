using System.Globalization;
using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Models;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Resources;
using Maran.Modules.Identity.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Identity.Queries.ExportPersonalData;

/// <summary>
/// Handles <see cref="ExportPersonalDataQuery"/>: checks the subject exists, checks the journal part of
/// the export is within the cap, collects every module's allow-listed sections, journals the export,
/// and hands back the plan the controller then streams (issue #146).
/// </summary>
/// <remarks>
/// <para>
/// <b>The journal entry is written here, before any byte is streamed</b>, so the export is on record
/// even if the client disconnects half way; it names the kind and the subject's id and nothing else.
/// Nothing is journalled for a refusal, because nothing was disclosed.
/// </para>
/// <para>
/// <b>The providers run as the administrator who asked.</b> The tenant filters admit an
/// administrator, so no filter is bypassed to collect an account's rows. The handler trusts the
/// providers' allow-lists and adds none of its own: what an export may contain is decided field by
/// field where the entity is read, by the module that owns the entity.
/// </para>
/// </remarks>
public sealed class ExportPersonalDataQueryHandler
{
    /// <summary>The module's database context, which answers whether a user exists.</summary>
    private readonly IdentityDbContext _dbContext;

    /// <summary>Answers whether a hosting account exists.</summary>
    private readonly IAccountDirectory _accounts;

    /// <summary>Every module's personal-data provider, this module's included.</summary>
    private readonly IEnumerable<IPersonalDataProvider> _providers;

    /// <summary>Chooses the journal rows that are about the subject.</summary>
    private readonly PersonalDataAuditRows _auditRows;

    /// <summary>Writes the export's journal entry.</summary>
    private readonly IdentityAuditJournal _journal;

    /// <summary>The administrator asking.</summary>
    private readonly ICurrentUser _currentUser;

    /// <summary>The panel's clock, which names the file.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The module's database context.</param>
    /// <param name="accounts">Answers whether a hosting account exists.</param>
    /// <param name="providers">Every module's personal-data provider.</param>
    /// <param name="auditRows">Chooses the journal rows that are about the subject.</param>
    /// <param name="journal">Writes the export's journal entry.</param>
    /// <param name="currentUser">The administrator asking.</param>
    /// <param name="clock">The panel's clock.</param>
    public ExportPersonalDataQueryHandler(
        IdentityDbContext dbContext,
        IAccountDirectory accounts,
        IEnumerable<IPersonalDataProvider> providers,
        PersonalDataAuditRows auditRows,
        IdentityAuditJournal journal,
        ICurrentUser currentUser,
        IClock clock)
    {
        _dbContext = dbContext;
        _accounts = accounts;
        _providers = providers;
        _auditRows = auditRows;
        _journal = journal;
        _currentUser = currentUser;
        _clock = clock;
    }

    /// <summary>Plans the export of one subject.</summary>
    /// <param name="query">Which subject, and the caller's address and user agent for the journal.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// The plan, or <c>PrivacySubjectNotFound</c> (404) for an id no user or account holds, or
    /// <c>PersonalDataExportTooLarge</c> (400) when the journal rows about the subject exceed
    /// <see cref="AuditExportLimits.MaxRows"/>. An export is never truncated to fit.
    /// </returns>
    public async Task<Result<PersonalDataExportPlan>> HandleAsync(
        ExportPersonalDataQuery query,
        CancellationToken cancellationToken)
    {
        var subject = new PersonalDataSubject(query.Kind, query.SubjectId);

        if (!await ExistsAsync(subject, cancellationToken))
        {
            return Result<PersonalDataExportPlan>.Fail(
                Error.Of(nameof(ErrorMessages.PrivacySubjectNotFound), ErrorType.NotFound));
        }

        var auditRowCount = await _auditRows.Of(subject).LongCountAsync(cancellationToken);
        if (auditRowCount > AuditExportLimits.MaxRows)
        {
            return Result<PersonalDataExportPlan>.Fail(
                Error.Of(nameof(ErrorMessages.PersonalDataExportTooLarge), ErrorType.Validation));
        }

        var sections = new List<PersonalDataSection>();
        foreach (var provider in _providers)
        {
            sections.AddRange(await provider.ExportAsync(subject, cancellationToken));
        }

        await _journal.RecordIdentifiedAsync(
            SignedInCallerPolicy.Require(_currentUser).UserId,
            IdentityAuditActions.PersonalDataExported,
            string.Create(CultureInfo.InvariantCulture, $"{subject.Kind} {subject.Id:D}"),
            query.IpAddress,
            query.UserAgent,
            succeeded: true,
            cancellationToken);

        return Result<PersonalDataExportPlan>.Ok(
            new PersonalDataExportPlan(subject, sections, auditRowCount, FileNameOf(subject.Kind)));
    }

    /// <summary>Says whether the subject is a user or an account the panel holds.</summary>
    /// <param name="subject">The subject asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns><c>false</c> for an unknown id and for a kind that is neither a user nor an account.</returns>
    private async Task<bool> ExistsAsync(PersonalDataSubject subject, CancellationToken cancellationToken)
    {
        switch (subject.Kind)
        {
            case PersonalDataSubjectKind.User:
                return await _dbContext.Users.AnyAsync(user => user.Id == subject.Id, cancellationToken);
            case PersonalDataSubjectKind.Account:
                return await _accounts.FindAsync(subject.Id, cancellationToken) is not null;
            default:
                return false;
        }
    }

    /// <summary>Builds the download's file name from the kind and the clock only.</summary>
    /// <param name="kind">The subject's kind.</param>
    /// <returns>For example <c>maran-personal-data-account-20261107T093000Z.json</c>.</returns>
    private string FileNameOf(PersonalDataSubjectKind kind)
    {
        var stamp = _clock.UtcNow.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        return $"maran-personal-data-{kind.ToString().ToLowerInvariant()}-{stamp}.json";
    }
}
