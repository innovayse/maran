using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Microsoft.Extensions.Localization;

namespace Maran.Modules.Accounts.Commands.ChangeResellerEnvelope;

/// <summary>Handles <see cref="ChangeResellerEnvelopeCommand"/> by replacing the reseller's envelope.</summary>
/// <remarks>
/// <para>
/// Lowering an envelope below what the reseller owns is accepted on purpose and edits nothing the
/// reseller already has: its plans and accounts keep working, and the next write that would add to what
/// is over the line is refused by the handler that makes it. The response carries the usage beside the
/// new envelope so both sides see "over envelope" instead of an error here.
/// </para>
/// <para>
/// <b>An envelope edit and an allocation exclude each other.</b> The reseller's row is the concurrency
/// token of every allocation (<see cref="Domain.Entities.Reseller.NoteAllocationChanged"/>), and this
/// handler moves the same revision, so a creation that read the old envelope cannot commit after the
/// edit, and an edit cannot commit over a creation it did not see. Whichever write lands second is
/// refused by the database: a creation answers its own conflict code, and an edit that loses surfaces as
/// a failed request the administrator repeats.
/// </para>
/// </remarks>
public sealed class ChangeResellerEnvelopeCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Measures what the reseller owns, for the response.</summary>
    private readonly ResellerUsageReader _usage;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Names the reseller's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="usage">Measures what the reseller owns, for the response.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Names the reseller's state in the response, in the request's culture.</param>
    public ChangeResellerEnvelopeCommandHandler(
        AccountsDbContext dbContext,
        ResellerUsageReader usage,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _usage = usage;
        _journal = journal;
        _displayNames = displayNames;
    }

    /// <summary>Replaces the envelope of the reseller the command names.</summary>
    /// <param name="command">The validated new envelope and the reseller it is for.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The reseller with its new envelope, or <c>ResellerNotFound</c>.</returns>
    public async Task<Result<ResellerDetailDto>> HandleAsync(
        ChangeResellerEnvelopeCommand command,
        CancellationToken cancellationToken)
    {
        var reseller = await _dbContext.Resellers
            .SingleOrDefaultAsync(r => r.Id == command.ResellerId, cancellationToken);
        if (reseller is null)
        {
            // The subject is the identifier the caller supplied, because no name is known: a probe for a
            // reseller that is not there still leaves a trace naming what was probed for.
            await _journal.RecordFailureAsync(
                AccountAuditActions.ResellerEnvelopeChanged,
                command.ResellerId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<ResellerDetailDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        reseller.ChangeEnvelope(ResellerMapper.ToDomain(command.Envelope));
        reseller.NoteAllocationChanged();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AccountAuditActions.ResellerEnvelopeChanged,
            reseller.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var usage = await _usage.ReadAsync(reseller.Id, cancellationToken);
        return Result<ResellerDetailDto>.Ok(ResellerMapper.ToDetailDto(reseller, usage, _displayNames));
    }
}
