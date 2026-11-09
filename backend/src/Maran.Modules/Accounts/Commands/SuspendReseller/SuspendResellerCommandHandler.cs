using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Microsoft.Extensions.Localization;
using Wolverine;

namespace Maran.Modules.Accounts.Commands.SuspendReseller;

/// <summary>
/// Handles <see cref="SuspendResellerCommand"/>: asks Identity to suspend the reseller's login, then
/// records the reseller as suspended.
/// </summary>
/// <remarks>
/// <para>
/// <b>What a suspension covers, and what it does not.</b> It covers the reseller's LOGIN
/// (<see cref="ResellerSuspending"/>, delivered to Identity) and, because an access token lives for up
/// to fifteen minutes after its login is refused, every mutating Accounts handler refuses a suspended
/// reseller (<c>ResellerSuspended</c>), so a token still in hand can read and cannot write. It does NOT
/// suspend any account the reseller owns: those are its customers' services, and an administrator
/// suspends them separately when that is wanted.
/// </para>
/// <para>
/// <b>Ordering, and which way it fails.</b> The event is INVOKED, inline, before the row is written, so a
/// subscriber that throws aborts the suspension and the exception reaches the caller as a failed
/// request with the reseller still active, the recoverable direction: it can be suspended again. The
/// reverse, a reseller recorded as suspended whose login still signs in, would be a lie to billing. No
/// typed code is invented for it: the subscriber's text has no place in an answer
/// (rules/security.md item 8) and the exception middleware already answers it with the generic failure.
/// </para>
/// <para>
/// <b>Idempotent, and the event is repeated on purpose.</b> Suspending a suspended reseller changes no
/// reseller row (the repeat still journals its own audit entry), but the login is asked again, so a
/// login that somehow came back (a restored backup, a manual edit) is closed by repeating the call.
/// </para>
/// </remarks>
public sealed class SuspendResellerCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>The bus <see cref="ResellerSuspending"/> is invoked on.</summary>
    private readonly IMessageBus _bus;

    /// <summary>Measures what the reseller owns, for the response.</summary>
    private readonly ResellerUsageReader _usage;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>Names the reseller's state in the response, in the request's culture.</summary>
    private readonly IStringLocalizer<DisplayNames> _displayNames;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="bus">The bus <see cref="ResellerSuspending"/> is invoked on.</param>
    /// <param name="usage">Measures what the reseller owns, for the response.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="displayNames">Names the reseller's state in the response, in the request's culture.</param>
    public SuspendResellerCommandHandler(
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

    /// <summary>Suspends the reseller the command names.</summary>
    /// <param name="command">Which reseller to suspend.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The reseller in its new state, or <c>ResellerNotFound</c>.</returns>
    public async Task<Result<ResellerDetailDto>> HandleAsync(
        SuspendResellerCommand command,
        CancellationToken cancellationToken)
    {
        var reseller = await _dbContext.Resellers
            .SingleOrDefaultAsync(r => r.Id == command.ResellerId, cancellationToken);
        if (reseller is null)
        {
            // The subject is the identifier the caller supplied, because no name is known: a probe for a
            // reseller that is not there still leaves a trace naming what was probed for.
            await _journal.RecordFailureAsync(
                AccountAuditActions.ResellerSuspended,
                command.ResellerId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<ResellerDetailDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        await _bus.InvokeAsync(new ResellerSuspending(reseller.Id), cancellationToken);

        reseller.Suspend();
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _journal.RecordSuccessAsync(
            AccountAuditActions.ResellerSuspended,
            reseller.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        var usage = await _usage.ReadAsync(reseller.Id, cancellationToken);
        return Result<ResellerDetailDto>.Ok(ResellerMapper.ToDetailDto(reseller, usage, _displayNames));
    }
}
