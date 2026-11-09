using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.ValueObjects;
using Maran.Modules.Accounts.Resources;

namespace Maran.Modules.Accounts.Services;

/// <summary>
/// Decides which ceilings a new account holds in place of its plan's: the figures the creator stated,
/// held against the plan and against who is creating (issue #194).
/// </summary>
/// <remarks>
/// <para>
/// <b>An administrator may state any figure the agent accepts, a reseller may only LOWER.</b> A plan is
/// what a reseller was allowed to sell (it was held to the reseller's own envelope when it was made), so
/// an account of that plan with a larger process ceiling than the plan states would be a way round the
/// envelope. The comparison is with the plan, not with the envelope, because the plan is already inside
/// the envelope and is what the customer was promised. A plan that states no process ceiling is
/// unlimited, and any positive figure is lower than that.
/// </para>
/// <para>
/// <b>The inode and open-file ceilings have nothing to be above.</b> A plan states neither, so every
/// account on it is unlimited for both and a stated figure can only lower. The wire's zero for either
/// means "no limit", which is exactly what following the plan gives, so it is stored as not stated
/// (<see cref="AccountLimitOverrides"/> explains why a stored zero would be a second spelling).
/// </para>
/// <para>
/// The ranges themselves (a process limit of at least one, and every figure inside what the agent
/// accepts) are the command validator's; this class holds the figures to the plan and the caller.
/// </para>
/// </remarks>
public sealed class AccountLimitOverrideResolver
{
    /// <summary>Says whether the caller is a reseller.</summary>
    private readonly ResellerScope _scope;

    /// <summary>Creates the resolver.</summary>
    /// <param name="scope">Says whether the caller is a reseller.</param>
    public AccountLimitOverrideResolver(ResellerScope scope)
    {
        _scope = scope;
    }

    /// <summary>Resolves the stated figures against the plan and the caller.</summary>
    /// <param name="inodeLimit">The inode ceiling stated, <c>0</c> for none, or <c>null</c> to follow the plan.</param>
    /// <param name="processLimit">The process ceiling stated, or <c>null</c> to follow the plan.</param>
    /// <param name="openFilesLimit">The open-files ceiling stated, <c>0</c> for none, or <c>null</c> to follow the plan.</param>
    /// <param name="plan">The plan the account is created against.</param>
    /// <returns>
    /// The overrides the account will hold, or <c>AccountLimitAbovePlan</c> when a reseller states a
    /// process ceiling above its plan's.
    /// </returns>
    public Result<AccountLimitOverrides> Resolve(
        long? inodeLimit,
        int? processLimit,
        int? openFilesLimit,
        Plan plan)
    {
        if (_scope.CallerResellerId is not null
            && processLimit is { } stated
            && plan.RuntimeLimits.MaxProcesses.Maximum is { } planMaximum
            && stated > planMaximum)
        {
            return Result<AccountLimitOverrides>.Fail(
                Error.Of(nameof(ErrorMessages.AccountLimitAbovePlan), ErrorType.Validation));
        }

        return Result<AccountLimitOverrides>.Ok(
            AccountLimitOverrides.Of(
                inodeLimit is 0 ? null : inodeLimit,
                processLimit,
                openFilesLimit is 0 ? null : openFilesLimit));
    }
}
