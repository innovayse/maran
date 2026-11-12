using Maran.Modules.Mail.IntegrationEvents.Handlers;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.Extensions.Logging.Abstractions;

namespace Maran.Modules.Mail.Tests.IntegrationEvents.Handlers;

/// <summary>
/// The account events that move an account's local submission binding: creation, plan change and resumption
/// push it, suspension and deletion remove it first and abort when the agent refuses, and mail that is off
/// does nothing.
/// </summary>
public sealed class LocalSubmissionEventHandlerTests
{
    /// <summary>The agent's code for a refusal.</summary>
    private const string Refused = "AgentInvalidInput";

    /// <summary>The account these tests move.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A created account is bound with the plan's limit and no domains.</summary>
    [Fact]
    public async Task A_created_account_is_bound_with_the_plan_limit_and_no_domains()
    {
        using var context = await ArrangeAsync(new MailLimitsSnapshot(10, 1024, 300, 10240));

        await new AccountCreatedHandler(context.LocalSubmission, NullLogger<AccountCreatedHandler>.Instance)
            .HandleAsync(new AccountCreated(AccountId, "acme", "a@b.test", "en"), CancellationToken.None);

        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Equal(300u, sent.SendLimitPerHour);
        Assert.Empty(sent.Domains);
    }

    /// <summary>A refusal never fails the creation; with mail off nothing is sent (the control).</summary>
    [Fact]
    public async Task A_refusal_does_not_fail_the_creation_and_mail_off_sends_nothing()
    {
        using var refusing = await ArrangeAsync(null);
        refusing.Agent.FailNext("SetLocalSubmission", Error.Of(Refused, ErrorType.Validation));
        using var off = new MailTestContext();
        off.Accounts.Add(AccountId, "acme");
        var message = new AccountCreated(AccountId, "acme", "a@b.test", "en");

        await new AccountCreatedHandler(refusing.LocalSubmission, NullLogger<AccountCreatedHandler>.Instance)
            .HandleAsync(message, CancellationToken.None);
        await new AccountCreatedHandler(off.LocalSubmission, NullLogger<AccountCreatedHandler>.Instance)
            .HandleAsync(message, CancellationToken.None);

        Assert.Single(refusing.Agent.LocalSubmissions);
        Assert.Empty(off.Agent.LocalSubmissions);
    }

    /// <summary>A plan change pushes the new limit; an unreadable account sends nothing.</summary>
    [Fact]
    public async Task A_plan_change_pushes_the_new_limit_and_an_unreadable_account_sends_nothing()
    {
        using var context = await ArrangeAsync(new MailLimitsSnapshot(10, 1024, 500, 10240));
        var handler = new AccountPlanChangedHandler(context.LocalSubmission);

        await handler.HandleAsync(new AccountPlanChanged(AccountId, false), CancellationToken.None);
        await handler.HandleAsync(new AccountPlanChanged(Guid.NewGuid(), false), CancellationToken.None);

        Assert.Equal(500u, Assert.Single(context.Agent.LocalSubmissions).SendLimitPerHour);
    }

    /// <summary>A suspended account's plan change pushes nothing, so it stays unbound.</summary>
    [Fact]
    public async Task A_plan_change_of_a_suspended_account_pushes_nothing()
    {
        using var context = await ArrangeAsync(null, AccountState.Suspended);

        await new AccountPlanChangedHandler(context.LocalSubmission)
            .HandleAsync(new AccountPlanChanged(AccountId, false), CancellationToken.None);

        Assert.Empty(context.Agent.LocalSubmissions);
    }

    /// <summary>Suspension removes the binding BEFORE it suspends the mail.</summary>
    [Fact]
    public async Task Suspension_removes_the_binding_before_suspending_the_mail()
    {
        using var context = await ArrangeAsync(null);

        await new AccountSuspendingHandler(context.Agent, context.Server, context.LocalSubmission)
            .HandleAsync(new AccountSuspending(AccountId, "acme"), CancellationToken.None);

        Assert.Equal(["acme"], context.Agent.LocalSubmissionRemovals);
        Assert.Equal(["SetAccountSuspension(acme,suspended=True)"], context.Agent.Calls);
    }

    /// <summary>A refused removal aborts the suspension before the mail is touched; with mail off nothing happens.</summary>
    [Fact]
    public async Task A_refused_removal_aborts_the_suspension_before_the_mail_is_suspended()
    {
        using var context = await ArrangeAsync(null);
        context.Agent.FailNext("RemoveLocalSubmission", Error.Of(Refused, ErrorType.Failure));
        using var off = new MailTestContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return new AccountSuspendingHandler(context.Agent, context.Server, context.LocalSubmission)
                .HandleAsync(new AccountSuspending(AccountId, "acme"), CancellationToken.None);
        });
        await new AccountSuspendingHandler(off.Agent, off.Server, off.LocalSubmission)
            .HandleAsync(new AccountSuspending(AccountId, "acme"), CancellationToken.None);

        Assert.Empty(context.Agent.Calls);
        Assert.Empty(off.Agent.LocalSubmissionRemovals);
    }

    /// <summary>Resumption resumes the mail, then binds the account although it still reads as suspended.</summary>
    [Fact]
    public async Task Resumption_resumes_the_mail_then_binds_the_account()
    {
        using var context = await ArrangeAsync(new MailLimitsSnapshot(10, 1024, 120, 10240), AccountState.Suspended);
        await context.SeedDomainAsync(AccountId, "example.test");

        await new AccountResumingHandler(context.Agent, context.Server, context.LocalSubmission)
            .HandleAsync(new AccountResuming(AccountId, "acme"), CancellationToken.None);

        Assert.Equal(["SetAccountSuspension(acme,suspended=False)"], context.Agent.Calls);
        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Equal(["example.test"], sent.Domains);
        Assert.Equal(120u, sent.SendLimitPerHour);
    }

    /// <summary>A reseller-driven resume binds the account although the reseller's scope shows it nothing.</summary>
    [Fact]
    public async Task A_resume_the_tenant_scope_cannot_see_still_binds_the_account()
    {
        using var context = await ArrangeAsync(new MailLimitsSnapshot(10, 1024, 90, 10240), AccountState.Suspended);
        context.Accounts.HideFromScopedReads(AccountId);

        await new AccountResumingHandler(context.Agent, context.Server, context.LocalSubmission)
            .HandleAsync(new AccountResuming(AccountId, "acme"), CancellationToken.None);

        Assert.Equal(90u, Assert.Single(context.Agent.LocalSubmissions).SendLimitPerHour);
    }

    /// <summary>A refused binding aborts the resumption.</summary>
    [Fact]
    public async Task A_refused_binding_aborts_the_resumption()
    {
        using var context = await ArrangeAsync(null, AccountState.Suspended);
        context.Agent.FailNext("SetLocalSubmission", Error.Of(Refused, ErrorType.Failure));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return new AccountResumingHandler(context.Agent, context.Server, context.LocalSubmission)
                .HandleAsync(new AccountResuming(AccountId, "acme"), CancellationToken.None);
        });
    }

    /// <summary>Deletion removes the binding; a refusal aborts it with the rows untouched.</summary>
    [Fact]
    public async Task Deletion_removes_the_binding_and_a_refusal_aborts_it_with_the_rows_untouched()
    {
        using var context = await ArrangeAsync(null);
        await context.SeedDomainAsync(AccountId, "example.test");
        context.Agent.FailNext("RemoveLocalSubmission", Error.Of(Refused, ErrorType.Failure));
        var handler = new AccountDeletingHandler(context.Database, context.LocalSubmission);
        var message = new AccountDeleting(AccountId, "acme");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return handler.HandleAsync(message, CancellationToken.None);
        });
        Assert.Single(context.Database.MailDomains);

        await handler.HandleAsync(message, CancellationToken.None);

        Assert.Equal(["acme", "acme"], context.Agent.LocalSubmissionRemovals);
        Assert.Empty(context.Database.MailDomains);
    }

    /// <summary>Builds a context with mail on and one account.</summary>
    /// <param name="limits">The plan's mail limits, or <c>null</c> for the default.</param>
    /// <param name="state">The account's state.</param>
    /// <returns>The context.</returns>
    private static async Task<MailTestContext> ArrangeAsync(
        MailLimitsSnapshot? limits,
        AccountState state = AccountState.Active)
    {
        var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme", limits, state);

        return context;
    }
}
