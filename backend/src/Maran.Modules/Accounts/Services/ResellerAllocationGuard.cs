using System.Text.Json;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Policies;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Models;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Resources;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Holds a write that changes what a reseller owns — a plan, an account, a disk quota — to the
/// envelope the administrator sold it, and claims the reseller's row so two such writes cannot both
/// take the last slot.
/// </summary>
/// <remarks>
/// <para>
/// <b>The arithmetic is <see cref="ResellerEnvelopePolicy"/>'s.</b> This type gathers the figures the
/// policy needs (what the reseller owns now) and asks; it holds no comparison of its own, so a boundary
/// is decided in one place and tested without a database.
/// </para>
/// <para>
/// <b>A write owned by the administrator is never asked.</b> Every method takes the owner's id and
/// answers "fits" for <c>null</c>: an administrator's own plans and accounts have no envelope.
/// </para>
/// <para>
/// <b>The claim.</b> A successful admission calls <see cref="Reseller.NoteAllocationChanged"/> on the
/// reseller row loaded into the handler's own context, so the handler's one <c>SaveChanges</c> writes
/// the new plan or account and the reseller's new revision together. A concurrent write that was
/// admitted against the same revision then fails at its save with a
/// <see cref="DbUpdateConcurrencyException"/>, which the handler answers as the same refusal this guard
/// would have given had it run second. The claim is only as strong as that save: a handler MUST call
/// it before its own save and MUST NOT save in between.
/// </para>
/// <para>
/// <b>Counts include every status.</b> A suspended account still holds its slot and its disk, which is
/// what the administrator sold.
/// </para>
/// </remarks>
public sealed class ResellerAllocationGuard
{
    /// <summary>The Accounts module's database context; the reseller row it loads is the one the handler saves.</summary>
    private readonly AccountsDbContext _dbContext;

    /// <summary>Creates the guard.</summary>
    /// <param name="dbContext">The Accounts module's database context.</param>
    public ResellerAllocationGuard(AccountsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>Whether the plan may be sold to an account owned by <paramref name="ownerId"/>: it must be that owner's own.</summary>
    /// <param name="ownerId">The account's owning reseller, or <c>null</c> for the administrator's.</param>
    /// <param name="plan">The plan the account is to be sold from.</param>
    /// <returns>
    /// <c>null</c> when the plan has the same owner; otherwise <c>PlanOwnerMismatch</c>. Without this an
    /// administrator could move a reseller's account to an administrator's plan and the account would
    /// vanish from the reseller's joined list.
    /// </returns>
    public static ResellerRefusal? RequirePlanOwnedBy(Guid? ownerId, Plan plan)
    {
        return plan.ResellerId == ownerId
            ? null
            : new ResellerRefusal(Error.Of(nameof(ErrorMessages.PlanOwnerMismatch), ErrorType.Conflict));
    }

    /// <summary>Admits one more account of <paramref name="plan"/> under the owner's envelope and claims the reseller.</summary>
    /// <param name="ownerId">The owning reseller, or <c>null</c> for the administrator's own account.</param>
    /// <param name="plan">The plan the new account is sold from; its disk quota joins the pool.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// <c>null</c> when it fits; <c>ResellerNotFound</c> when the owner does not exist;
    /// <c>ResellerAccountLimitReached</c> or <c>ResellerDiskPoolExceeded</c> when it does not.
    /// </returns>
    public async Task<ResellerRefusal?> AdmitAccountAsync(Guid? ownerId, Plan plan, CancellationToken cancellationToken)
    {
        if (ownerId is not { } id)
        {
            return null;
        }

        var reseller = await LoadAsync(id, cancellationToken);
        if (reseller is null)
        {
            return NotFound();
        }

        var accountsOwned = await _dbContext.Accounts.CountAsync(account => account.ResellerId == id, cancellationToken);
        if (!ResellerEnvelopePolicy.AccountAllowed(reseller.Envelope, accountsOwned))
        {
            return new ResellerRefusal(Error.Of(nameof(ErrorMessages.ResellerAccountLimitReached), ErrorType.Conflict));
        }

        var allocated = await AllocatedDiskMbAsync(id, cancellationToken);
        if (!ResellerEnvelopePolicy.DiskPoolAllowed(reseller.Envelope, allocated + plan.DiskQuotaMb))
        {
            return new ResellerRefusal(Error.Of(nameof(ErrorMessages.ResellerDiskPoolExceeded), ErrorType.Conflict));
        }

        reseller.NoteAllocationChanged();
        return null;
    }

    /// <summary>Admits moving an account from one plan to another under the owner's disk pool and claims the reseller.</summary>
    /// <param name="ownerId">The account's owning reseller, or <c>null</c> for the administrator's own.</param>
    /// <param name="current">The plan the account is on now.</param>
    /// <param name="target">The plan it is to move to.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// <c>null</c> when it fits; <c>ResellerNotFound</c> when the owner does not exist;
    /// <c>ResellerDiskPoolExceeded</c> when the move would take the pool over its size.
    /// </returns>
    /// <remarks>
    /// Only a LARGER disk quota is judged. A move that keeps or lowers the figure never needs the pool,
    /// so an envelope lowered below current usage still lets a reseller move accounts to smaller plans,
    /// which is how it gets back under it.
    /// </remarks>
    public async Task<ResellerRefusal?> AdmitPlanChangeAsync(
        Guid? ownerId,
        Plan current,
        Plan target,
        CancellationToken cancellationToken)
    {
        if (ownerId is not { } id)
        {
            return null;
        }

        var reseller = await LoadAsync(id, cancellationToken);
        if (reseller is null)
        {
            return NotFound();
        }

        if (target.DiskQuotaMb > current.DiskQuotaMb)
        {
            var allocated = await AllocatedDiskMbAsync(id, cancellationToken);
            var after = allocated - current.DiskQuotaMb + target.DiskQuotaMb;
            if (!ResellerEnvelopePolicy.DiskPoolAllowed(reseller.Envelope, after))
            {
                return new ResellerRefusal(Error.Of(nameof(ErrorMessages.ResellerDiskPoolExceeded), ErrorType.Conflict));
            }
        }

        if (target.DiskQuotaMb != current.DiskQuotaMb)
        {
            reseller.NoteAllocationChanged();
        }

        return null;
    }

    /// <summary>Admits one more plan with <paramref name="limits"/> under the owner's envelope and claims the reseller.</summary>
    /// <param name="ownerId">The owning reseller, or <c>null</c> for the administrator's own plan.</param>
    /// <param name="limits">The figures of the plan to create.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// <c>null</c> when it fits; <c>ResellerNotFound</c> when the owner does not exist;
    /// <c>ResellerPlanExceedsEnvelope</c> (carrying the offending figures) or
    /// <c>ResellerPlanLimitReached</c> when it does not.
    /// </returns>
    public async Task<ResellerRefusal?> AdmitNewPlanAsync(
        Guid? ownerId,
        PlanLimits limits,
        CancellationToken cancellationToken)
    {
        if (ownerId is not { } id)
        {
            return null;
        }

        var reseller = await LoadAsync(id, cancellationToken);
        if (reseller is null)
        {
            return NotFound();
        }

        var exceeds = ExceedsCeilings(reseller, limits);
        if (exceeds is not null)
        {
            return exceeds;
        }

        var plansOwned = await _dbContext.Plans.CountAsync(plan => plan.ResellerId == id, cancellationToken);
        if (!ResellerEnvelopePolicy.PlanAllowed(reseller.Envelope, plansOwned))
        {
            return new ResellerRefusal(Error.Of(nameof(ErrorMessages.ResellerPlanLimitReached), ErrorType.Conflict));
        }

        reseller.NoteAllocationChanged();
        return null;
    }

    /// <summary>
    /// Admits the new figures of an existing plan under the owner's envelope: every ceiling, and the
    /// disk pool when the quota is raised. Claims the reseller when the quota changes.
    /// </summary>
    /// <param name="ownerId">The plan's owning reseller, or <c>null</c> for the administrator's own plan.</param>
    /// <param name="plan">The plan being edited, still holding the figures it has now.</param>
    /// <param name="limits">The figures the edit asks for.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>
    /// <c>null</c> when it fits; <c>ResellerNotFound</c> when the owner does not exist;
    /// <c>ResellerPlanExceedsEnvelope</c> (carrying the offending figures) or
    /// <c>ResellerDiskPoolExceeded</c> when it does not.
    /// </returns>
    /// <remarks>
    /// The pool is recomputed with the new quota in place of the plan's current one for every account
    /// already on the plan, because raising a plan's quota raises every one of those accounts' disk
    /// at once: checking it only at account creation would let a reseller outgrow its pool by editing a
    /// plan after the accounts exist.
    /// </remarks>
    public async Task<ResellerRefusal?> AdmitPlanEditAsync(
        Guid? ownerId,
        Plan plan,
        PlanLimits limits,
        CancellationToken cancellationToken)
    {
        if (ownerId is not { } id)
        {
            return null;
        }

        var reseller = await LoadAsync(id, cancellationToken);
        if (reseller is null)
        {
            return NotFound();
        }

        var exceeds = ExceedsCeilings(reseller, limits);
        if (exceeds is not null)
        {
            return exceeds;
        }

        if (limits.DiskQuotaMb == plan.DiskQuotaMb)
        {
            return null;
        }

        if (limits.DiskQuotaMb > plan.DiskQuotaMb)
        {
            var onPlan = await _dbContext.Accounts.CountAsync(account => account.PlanId == plan.Id, cancellationToken);
            var allocated = await AllocatedDiskMbAsync(id, cancellationToken);
            var after = allocated + ((long)(limits.DiskQuotaMb - plan.DiskQuotaMb) * onPlan);
            if (!ResellerEnvelopePolicy.DiskPoolAllowed(reseller.Envelope, after))
            {
                return new ResellerRefusal(Error.Of(nameof(ErrorMessages.ResellerDiskPoolExceeded), ErrorType.Conflict));
            }
        }

        reseller.NoteAllocationChanged();
        return null;
    }

    /// <summary>Loads the reseller into the context, tracked, so the handler's save carries its claim.</summary>
    /// <param name="resellerId">The reseller to load.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The reseller, or <c>null</c> when no such reseller is in the caller's scope.</returns>
    private async Task<Reseller?> LoadAsync(Guid resellerId, CancellationToken cancellationToken)
    {
        return await _dbContext.Resellers.SingleOrDefaultAsync(reseller => reseller.Id == resellerId, cancellationToken);
    }

    /// <summary>The disk the reseller has promised: the plan quota of every account it owns, in megabytes.</summary>
    /// <param name="resellerId">The reseller whose accounts are summed.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The sum; zero for a reseller that owns no account.</returns>
    private async Task<long> AllocatedDiskMbAsync(Guid resellerId, CancellationToken cancellationToken)
    {
        // Expression lambdas: EF Core translates the join and the sum into SQL.
        return await _dbContext.Accounts
            .AsNoTracking()
            .Where(account => account.ResellerId == resellerId)
            .Join(
                _dbContext.Plans.AsNoTracking(),
                account => account.PlanId,
                plan => plan.Id,
                (account, plan) => (long)plan.DiskQuotaMb)
            .SumAsync(cancellationToken);
    }

    /// <summary>Judges a plan's figures against the envelope's ceilings.</summary>
    /// <param name="reseller">The reseller whose envelope applies.</param>
    /// <param name="limits">The figures of the plan.</param>
    /// <returns><c>null</c> when every figure fits; otherwise the refusal naming each one that does not.</returns>
    private static ResellerRefusal? ExceedsCeilings(Reseller reseller, PlanLimits limits)
    {
        var violations = ResellerEnvelopePolicy.Violations(reseller.Envelope, limits);
        if (violations.Count == 0)
        {
            return null;
        }

        var fields = violations
            .Select(violation =>
            {
                return JsonNamingPolicy.CamelCase.ConvertName(violation);
            })
            .ToList();

        return new ResellerRefusal(
            Error.Of(nameof(ErrorMessages.ResellerPlanExceedsEnvelope), ErrorType.Validation),
            ProblemExtension.Of("envelope", new PlanEnvelopeViolationsDto(fields)));
    }

    /// <summary>The refusal for a reseller the caller's scope holds no row for.</summary>
    /// <returns>A <c>ResellerNotFound</c> refusal.</returns>
    private static ResellerRefusal NotFound()
    {
        return new ResellerRefusal(Error.Of(nameof(ErrorMessages.ResellerNotFound), ErrorType.NotFound));
    }
}
