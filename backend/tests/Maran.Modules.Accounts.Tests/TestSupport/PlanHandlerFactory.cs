using Maran.Modules.Accounts.Commands.CreatePlan;
using Maran.Modules.Accounts.Commands.DeletePlan;
using Maran.Modules.Accounts.Commands.UpdatePlan;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Sdk.Interfaces;
using Maran.SharedKernel.Interfaces;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// Builds the plan handlers with every collaborator they take, over one context and one caller, so a
/// test states only the parts it varies and a change to a constructor is one edit here.
/// </summary>
public static class PlanHandlerFactory
{
    /// <summary>Builds the creation handler.</summary>
    /// <param name="dbContext">The context the handler and its collaborators share.</param>
    /// <param name="caller">The principal of the request.</param>
    /// <param name="audit">The writer the journal records into.</param>
    /// <param name="displayNames">The display names of the shipped plans.</param>
    /// <returns>The handler.</returns>
    public static CreatePlanCommandHandler Create(
        AccountsDbContext dbContext,
        ICurrentUser caller,
        IAuditWriter audit,
        StubDisplayNames displayNames)
    {
        return new CreatePlanCommandHandler(
            dbContext,
            new ResellerScope(dbContext, caller),
            new ResellerAllocationGuard(dbContext),
            new ResellerNameDirectory(dbContext),
            new PlanNameDirectory(dbContext, displayNames),
            new AccountAuditJournal(audit, caller),
            displayNames);
    }

    /// <summary>Builds the edit handler.</summary>
    /// <param name="dbContext">The context the handler and its collaborators share.</param>
    /// <param name="caller">The principal of the request.</param>
    /// <param name="audit">The writer the journal records into.</param>
    /// <param name="displayNames">The display names of the shipped plans.</param>
    /// <param name="agent">The agent double that owns quotas and runtime ceilings.</param>
    /// <returns>The handler.</returns>
    public static UpdatePlanCommandHandler Update(
        AccountsDbContext dbContext,
        ICurrentUser caller,
        IAuditWriter audit,
        StubDisplayNames displayNames,
        RecordingAgentAccountsClient agent)
    {
        return new UpdatePlanCommandHandler(
            dbContext,
            new ResellerScope(dbContext, caller),
            new ResellerAllocationGuard(dbContext),
            new ResellerNameDirectory(dbContext),
            new AccountDiskQuotaEnforcer(agent),
            new AccountRuntimeLimitsEnforcer(agent),
            new PlanNameDirectory(dbContext, displayNames),
            new AccountAuditJournal(audit, caller),
            displayNames);
    }

    /// <summary>Builds the deletion handler.</summary>
    /// <param name="dbContext">The context the handler and its collaborators share.</param>
    /// <param name="caller">The principal of the request.</param>
    /// <param name="audit">The writer the journal records into.</param>
    /// <returns>The handler.</returns>
    public static DeletePlanCommandHandler Delete(AccountsDbContext dbContext, ICurrentUser caller, IAuditWriter audit)
    {
        return new DeletePlanCommandHandler(
            dbContext,
            new ResellerScope(dbContext, caller),
            new AccountAuditJournal(audit, caller));
    }
}
