using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.ReactivateReseller;

/// <summary>
/// Handles <see cref="ReactivateResellerCommand"/>: asks Identity to let the reseller's login sign in
/// again, then records the reseller as active.
/// </summary>
/// <remarks>
/// <para>
/// <b>What it restores.</b> The reseller's LOGIN (<see cref="ResellerResuming"/>, delivered to Identity)
/// and, by the row, its right to change what it owns. It touches no account: a suspension never did, so
/// there is nothing to put back, and an account an administrator suspended separately stays suspended.
/// </para>
/// <para>
/// <b>Ordering, and which way it fails.</b> The event is INVOKED, inline, before the row is written, so a
/// subscriber that throws aborts the reactivation and the exception reaches the caller as a failed
/// request with the reseller still suspended, the recoverable direction: it can be reactivated again.
/// The reverse, a reseller recorded as active whose login still cannot sign in, would be a reseller
/// shown as in service that nobody can reach. No typed code is invented for it, for the reason
/// <c>SuspendResellerCommandHandler</c> gives.
/// </para>
/// <para>
/// <b>Idempotent.</b> Reactivating an active reseller changes no row, and the login is asked again.
/// </para>
/// </remarks>
public sealed class ReactivateResellerCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The bus <see cref="ResellerResuming"/> is invoked on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Measures what the reseller owns, for the response.</summary>
    private readonly ResellerUsageReader _usage;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Names the reseller's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="bus">The bus <see cref="ResellerResuming"/> is invoked on.</param>
    /// <param name="usage">Measures what the reseller owns, for the response.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Names the reseller's state in the response, in the request's culture.</param>
    public ReactivateResellerCommandHandler(
        AccountsDbContext dbContext,
        IMessageBus bus,
        ResellerUsageReader usage,
        AccountAuditJournal journal,
        IStringLocalizer<DisplayNames> displayNames)
    {
        _dbContext = dbContext;
        _bus = bus;
        _usage = usage;
        _journal = journal;
        _displayNames = displayNames;
    }

    /// <summary>Reactivates the reseller the command names.</summary>
    /// <param name="command">Which reseller to reactivate.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The reseller in its new state, or <c>ResellerNotFound</c>.</returns>
    public async Task<Result<ResellerDetailDto>> HandleAsync(
        ReactivateResellerCommand command,
        CancellationToken cancellationToken)
    {
        var reseller = await _dbContext.Resellers
            .SingleOrDefaultAsync(r => r.Id == command.ResellerId, cancellationToken);
        if (reseller is null)
        {
            // The subject is the identifier the caller supplied, because no name is known: a probe for a
            // reseller that is not there still leaves a trace naming what was probed for.
            await _journal.RecordFailureAsync(
                AccountAuditActions.ResellerReactivated,
                command.ResellerId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<ResellerDetailDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        await _bus.InvokeAsync(new ResellerResuming(reseller.Id), cancellationToken);

        reseller.Reactivate();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AccountAuditActions.ResellerReactivated,
            reseller.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var usage = await _usage.ReadAsync(reseller.Id, cancellationToken);
        return Result<ResellerDetailDto>.Ok(ResellerMapper.ToDetailDto(reseller, usage, _displayNames));
    }
}
