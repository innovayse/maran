using Maran.Modules.Marketplace.Domain.Entities;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Persistence;
using Maran.Modules.Marketplace.Resources;
using Npgsql;

namespace Maran.Modules.Marketplace.Services;

/// <summary>
/// Lets at most one operation run on a module at a time: install, update, remove and rollback each claim
/// the module first, and a second claim on the same module is refused with a conflict.
/// </summary>
/// <remarks>
/// <para>
/// <b>The claim is a row, and the database is the arbiter.</b> A unique index on the module id means two
/// callers inserting at once cannot both win, whatever order they run in; the code that checks for an
/// existing row first only decides the common case without an exception.
/// </para>
/// <para>
/// <b>A claim can be abandoned</b> (the process died holding it), so a claim older than
/// <see cref="ModuleOperationLock.StaleAfter"/> is taken over by the next caller, and releasing addresses a
/// claim by its own id: the old owner of a taken-over claim cannot release the new owner's.
/// </para>
/// <para>
/// Different modules never wait on each other: the claim is per module id.
/// </para>
/// <para>
/// <b>The claim is also the record of what is open.</b> Once the task exists the claim names the operation,
/// the version and the task (<see cref="AttachTaskAsync"/>), and a repeat of the same request is answered
/// from it (<see cref="FindOpenAsync"/>). It used to be remembered in process memory, so after a restart a
/// retry met a held claim with nothing to say about it and was told <c>ModuleOperationInProgress</c>
/// instead of being handed the running task's id. Releasing the claim removes the record with it, so there
/// is nothing separate to forget.
/// </para>
/// </remarks>
public sealed class ModuleOperationGate
{
    /// <summary>The module's own database context.</summary>
    private readonly MarketplaceDbContext _context;

    /// <summary>The time source that decides whether a claim has gone stale.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the gate.</summary>
    /// <param name="context">The module's database context.</param>
    /// <param name="clock">The time source.</param>
    public ModuleOperationGate(MarketplaceDbContext context, IClock clock)
    {
        _context = context;
        _clock = clock;
    }

    /// <summary>Claims a module for one operation.</summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="cancellationToken">Cancels the claim.</param>
    /// <returns>
    /// The claim to release when the operation ends, or <c>ModuleOperationInProgress</c> (a conflict) when
    /// another operation holds the module and its claim is not stale.
    /// </returns>
    public async Task<Result<ModuleOperationClaim>> AcquireAsync(string moduleId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        var now = _clock.UtcNow;
        var existing = await _context.ModuleOperationLocks
            .SingleOrDefaultAsync(claim => claim.ModuleId == moduleId, cancellationToken);
        if (existing is not null)
        {
            if (!existing.IsStale(now) || !await TryTakeOverAsync(existing, cancellationToken))
            {
                return InProgress();
            }
        }

        var fresh = new ModuleOperationLock(Guid.NewGuid(), moduleId, now);
        _context.ModuleOperationLocks.Add(fresh);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Another caller's claim landed between our look and our insert; the index refused ours.
            _context.Entry(fresh).State = EntityState.Detached;
            return InProgress();
        }

        return Result<ModuleOperationClaim>.Ok(new ModuleOperationClaim(fresh.Id, fresh.ModuleId));
    }

    /// <summary>Records on the claim which operation, on which version, its task carries.</summary>
    /// <param name="claim">The receipt <see cref="AcquireAsync"/> returned.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="version">The version it installs, or the installed version a remove or rollback acts on.</param>
    /// <param name="taskId">The task that carries its progress.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>
    /// True when the claim was found and now names the task. False when the claim is gone because it went
    /// stale and was taken over; the operation then simply cannot be recognised as a repeat, and a repeat is
    /// answered <c>ModuleOperationInProgress</c>, which is the safe side.
    /// </returns>
    public async Task<bool> AttachTaskAsync(
        ModuleOperationClaim claim,
        MarketplaceOperation operation,
        string? version,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);

        var held = await _context.ModuleOperationLocks
            .SingleOrDefaultAsync(row => row.Id == claim.Id, cancellationToken);
        if (held is null)
        {
            return false;
        }

        held.Attach(operation, version, taskId);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Finds the task of an identical operation that is still running on a module.</summary>
    /// <param name="moduleId">The module's machine id.</param>
    /// <param name="operation">The operation asked for.</param>
    /// <param name="version">The version asked for, or the installed version a remove or rollback acts on.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The open operation when the module's claim is not stale and carries the same operation on the same
    /// version; otherwise null. Read from the claim row, so the answer is the same after a restart and from
    /// any process.
    /// </returns>
    public async Task<OpenModuleOperation?> FindOpenAsync(
        string moduleId,
        MarketplaceOperation operation,
        string? version,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleId);

        var held = await _context.ModuleOperationLocks
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.ModuleId == moduleId, cancellationToken);
        if (held is null || held.IsStale(_clock.UtcNow) || !held.CarriesSame(operation, version))
        {
            return null;
        }

        return new OpenModuleOperation(operation, version, held.TaskId!.Value);
    }

    /// <summary>Releases a claim so the module can be operated on again.</summary>
    /// <param name="claim">The receipt <see cref="AcquireAsync"/> returned.</param>
    /// <param name="cancellationToken">Cancels the release.</param>
    /// <returns>
    /// Resolves once the claim is gone. A claim that is already gone, because it went stale and was taken
    /// over, is not an error: it is the new owner's now, and this call leaves it alone.
    /// </returns>
    public async Task ReleaseAsync(ModuleOperationClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);

        var held = await _context.ModuleOperationLocks
            .SingleOrDefaultAsync(row => row.Id == claim.Id, cancellationToken);
        if (held is null)
        {
            return;
        }

        _context.ModuleOperationLocks.Remove(held);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Someone deleted the same row first: the claim is released either way.
            _context.Entry(held).State = EntityState.Detached;
        }
    }

    /// <summary>Deletes a stale claim so a new one can be inserted in its place.</summary>
    /// <param name="stale">The stale claim.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>False when another caller took it over first, which means the module is busy again.</returns>
    private async Task<bool> TryTakeOverAsync(ModuleOperationLock stale, CancellationToken cancellationToken)
    {
        _context.ModuleOperationLocks.Remove(stale);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            _context.Entry(stale).State = EntityState.Detached;
            return false;
        }
    }

    /// <summary>The refusal a second operation on one module receives.</summary>
    /// <returns>The conflict failure.</returns>
    private static Result<ModuleOperationClaim> InProgress()
    {
        return Result<ModuleOperationClaim>.Fail(
            Error.Of(nameof(ErrorMessages.ModuleOperationInProgress), ErrorType.Conflict));
    }

    /// <summary>Tells whether a failed save was the unique index refusing a second claim.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>True for SQLSTATE 23505.</returns>
    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
