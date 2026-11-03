using Maran.Modules.Accounts.Commands.ChangeAccountOwnerEmail;
using Maran.Modules.Accounts.Commands.ChangeAccountPlan;
using Maran.Modules.Accounts.Commands.CreateAccount;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.SharedKernel.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Accounts.Tests.TestSupport;

/// <summary>
/// Assembles the account handlers whose constructors take the reseller services, so a test states
/// only the context, the caller and the doubles it is about. The caller passed here must be the one
/// the context was created for: the scope, the journal and the query filters all read it.
/// </summary>
public static class AccountHandlerFactory
{
    /// <summary>The instant the clock reports in every handler built here.</summary>
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Builds the account creation handler.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <param name="currentUser">The caller the context and the services act for.</param>
    /// <param name="agent">The agent double.</param>
    /// <param name="audit">The journal's writer; a fresh one when not stated.</param>
    /// <param name="bus">The bus the owner's login is requested on; a fresh stub when not stated.</param>
    /// <returns>The handler.</returns>
    public static CreateAccountCommandHandler Create(
        AccountsDbContext dbContext,
        ICurrentUser currentUser,
        RecordingAgentAccountsClient agent,
        RecordingAuditWriter? audit = null,
        StubMessageBus? bus = null)
    {
        return new CreateAccountCommandHandler(
            dbContext,
            new ResellerScope(dbContext, currentUser),
            new ResellerAllocationGuard(dbContext),
            new AccountNamespaceClaims(dbContext),
            new ResellerNameDirectory(dbContext),
            agent,
            new AccountRuntimeLimitsEnforcer(agent),
            new FakeClock(Now),
            new AccountAuditJournal(audit ?? new RecordingAuditWriter(), currentUser),
            bus ?? new StubMessageBus(),
            NullLogger<CreateAccountCommandHandler>.Instance,
            new StubDisplayNames());
    }

    /// <summary>Builds the plan change handler.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <param name="currentUser">The caller the context and the services act for.</param>
    /// <param name="agent">The agent double.</param>
    /// <param name="audit">The journal's writer.</param>
    /// <returns>The handler.</returns>
    public static ChangeAccountPlanCommandHandler ChangePlan(
        AccountsDbContext dbContext,
        ICurrentUser currentUser,
        RecordingAgentAccountsClient agent,
        RecordingAuditWriter audit)
    {
        return new ChangeAccountPlanCommandHandler(
            dbContext,
            new ResellerScope(dbContext, currentUser),
            new ResellerAllocationGuard(dbContext),
            new ResellerNameDirectory(dbContext),
            new AccountDiskQuotaEnforcer(agent),
            new AccountRuntimeLimitsEnforcer(agent),
            new AccountAuditJournal(audit, currentUser),
            new StubDisplayNames());
    }

    /// <summary>Builds the owner address change handler.</summary>
    /// <param name="dbContext">The module's context.</param>
    /// <param name="currentUser">The caller the context and the services act for.</param>
    /// <param name="bus">The bus the login's change is requested on.</param>
    /// <param name="audit">The journal's writer.</param>
    /// <returns>The handler.</returns>
    public static ChangeAccountOwnerEmailCommandHandler ChangeOwnerEmail(
        AccountsDbContext dbContext,
        ICurrentUser currentUser,
        StubMessageBus bus,
        RecordingAuditWriter audit)
    {
        return new ChangeAccountOwnerEmailCommandHandler(
            dbContext,
            new ResellerScope(dbContext, currentUser),
            new ResellerNameDirectory(dbContext),
            bus,
            new AccountAuditJournal(audit, currentUser),
            new StubDisplayNames());
    }
}
