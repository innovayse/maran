using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Mappers;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Contracts;
using Npgsql;

namespace Maran.Modules.Accounts.Commands.SetResellerBranding;

/// <summary>
/// Handles <see cref="SetResellerBrandingCommand"/> by replacing the branding of the reseller the
/// caller's own token names.
/// </summary>
/// <remarks>
/// <para>
/// The reseller is read from the token (<see cref="ResellerScope.CallerResellerId"/>) and nowhere else,
/// and the reseller row is read through the tenant filter, so there is nothing here to aim at another
/// reseller. A suspended reseller reads its branding but cannot change it, like every other write it
/// makes (<see cref="ResellerScope.RequireActiveAsync"/>).
/// </para>
/// <para>
/// The values were validated by <see cref="SetResellerBrandingCommandValidator"/>. The handler only
/// normalises what a client may have padded (a blank name is no name) and stores the row; a command
/// with nothing set removes it, so "no row" is the one spelling of "the panel's own" and a reseller that
/// cleared its branding is indistinguishable from one that never set any.
/// </para>
/// <para>
/// <b>The first save of a reseller races, and the loser is told so.</b> Two requests with no row yet
/// can both read "none" and both insert; the primary key (the reseller id) is what stops the second,
/// which answers <c>BrandingChangedConcurrently</c> (a conflict) instead of a server error. The
/// loser's values are not applied over the winner's behind its back: the screen re-reads and the person
/// saves again knowing what is stored.
/// </para>
/// </remarks>
public sealed class SetResellerBrandingCommandHandler
{
    /// <summary>The Accounts module's database context.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>States which reseller the caller is and whether it may still write.</summary>
    private readonly ResellerScope _scope;

    /// <summary>This module's audit journal.</summary>
    private readonly AccountAuditJournal _journal;

    /// <summary>The clock the change is stamped with.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    /// <param name="scope">States which reseller the caller is and whether it may still write.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="clock">The clock the change is stamped with.</param>
    public SetResellerBrandingCommandHandler(
        AccountsDbContext dbContext,
        ResellerScope scope,
        AccountAuditJournal journal,
        IClock clock)
    {
        _dbContext = dbContext;
        _scope = scope;
        _journal = journal;
        _clock = clock;
    }

    /// <summary>Replaces the caller's reseller's branding.</summary>
    /// <param name="command">The validated new values.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The branding as stored, <c>ResellerNotFound</c>, <c>ResellerSuspended</c>, or <c>BrandingChangedConcurrently</c>.</returns>
    public async Task<Result<ResellerBrandingDto>> HandleAsync(
        SetResellerBrandingCommand command,
        CancellationToken cancellationToken)
    {
        var refusal = await _scope.RequireActiveAsync(cancellationToken);
        if (refusal is not null)
        {
            await _journal.RecordFailureAsync(
                AccountAuditActions.ResellerBrandingChanged,
                _scope.CallerResellerId?.ToString() ?? string.Empty,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<ResellerBrandingDto>.Fail(refusal);
        }

        if (_scope.CallerResellerId is not { } resellerId)
        {
            // Not a reseller at all: the controller's policy keeps this out, so it is only reached by
            // a message dispatched on the bus. The same answer as a reseller whose row is gone.
            return Result<ResellerBrandingDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        var reseller = await _dbContext.Resellers
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == resellerId, cancellationToken);
        if (reseller is null)
        {
            await _journal.RecordFailureAsync(
                AccountAuditActions.ResellerBrandingChanged,
                resellerId.ToString(),
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<ResellerBrandingDto>.Fail(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
        }

        var displayName = string.IsNullOrWhiteSpace(command.DisplayName) ? null : command.DisplayName.Trim();
        var existing = await _dbContext.ResellerBrandings
            .SingleOrDefaultAsync(b => b.ResellerId == resellerId, cancellationToken);

        ResellerBranding? stored = null;
        if (displayName is null && command.AccentColor is null && command.Logo is null)
        {
            if (existing is not null)
            {
                _dbContext.ResellerBrandings.Remove(existing);
            }
        }
        else if (existing is null)
        {
            stored = new ResellerBranding(resellerId, displayName, command.AccentColor, command.Logo, _clock.UtcNow);
            _dbContext.ResellerBrandings.Add(stored);
        }
        else
        {
            existing.Replace(displayName, command.AccentColor, command.Logo, _clock.UtcNow);
            stored = existing;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent first save inserted this reseller's row between the read above and this
            // insert; the primary key decided it. Nothing of this call was stored.
            await _journal.RecordFailureAsync(
                AccountAuditActions.ResellerBrandingChanged,
                reseller.Name,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);

            return Result<ResellerBrandingDto>.Fail(
                Error.Of(nameof(ErrorMessages.BrandingChangedConcurrently), ErrorType.Conflict));
        }

        await _journal.RecordSuccessAsync(
            AccountAuditActions.ResellerBrandingChanged,
            reseller.Name,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<ResellerBrandingDto>.Ok(ResellerBrandingMapper.ToDto(stored));
    }
}
