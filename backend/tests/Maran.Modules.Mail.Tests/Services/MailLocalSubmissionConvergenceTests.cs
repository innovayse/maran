using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.IntegrationEvents.Handlers;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.Sdk.Events;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Services;

/// <summary>
/// The local submission binding is built from the account's real rows whoever drives the change, converges
/// after a failed push, is reconciled with the accounts that exist, treats an already-unbound user as
/// success, and is never re-bound by a change that races a suspension (issue #518).
/// </summary>
public sealed class MailLocalSubmissionConvergenceTests
{
    /// <summary>The agent's code for a refusal.</summary>
    private const string Refused = "AgentInvalidInput";

    /// <summary>The account these tests bind.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>Another account.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>A reseller-driven or unattended change sends the account's real domains and logins, never an empty binding.</summary>
    [Fact]
    public async Task A_reseller_or_unattended_change_sends_the_real_lists()
    {
        foreach (var principal in new[] { NonAdministratorCurrentUser.Reseller(), NonAdministratorCurrentUser.Anonymous() })
        {
            var database = Guid.NewGuid().ToString();
            using var seed = new MailTestContext(new TestCurrentUser(), database);
            await seed.SeedEnabledServerAsync();
            var domain = await seed.SeedDomainAsync(AccountId, "example.test");
            await seed.SeedMailboxAsync(domain, "info");
            using var driven = new MailTestContext(principal, database);
            driven.Accounts.Add(AccountId, "acme");

            // The control that makes the test mean something: this caller's own filter shows it nothing.
            Assert.Empty(driven.Database.MailDomains);

            await driven.LocalSubmission.TryPublishAfterChangeAsync(AccountId, CancellationToken.None);

            var sent = Assert.Single(driven.Agent.LocalSubmissions);
            Assert.Equal(["example.test"], sent.Domains);
            Assert.Equal(["info@example.test"], sent.MailboxLogins);
        }
    }

    /// <summary>The inverse control: a system read still never mixes in another account's rows.</summary>
    [Fact]
    public async Task A_system_read_still_sends_only_the_accounts_own_rows()
    {
        var database = Guid.NewGuid().ToString();
        using var seed = new MailTestContext(new TestCurrentUser(), database);
        await seed.SeedEnabledServerAsync();
        await seed.SeedDomainAsync(AccountId, "own.test");
        await seed.SeedDomainAsync(OtherAccountId, "foreign.test");
        using var driven = new MailTestContext(NonAdministratorCurrentUser.Reseller(), database);
        driven.Accounts.Add(AccountId, "acme");

        await driven.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        Assert.Equal(["own.test"], Assert.Single(driven.Agent.LocalSubmissions).Domains);
    }

    /// <summary>A push that fails after a committed change is retried by the reconcile pass.</summary>
    [Fact]
    public async Task A_failed_push_is_recorded_and_the_reconcile_pass_pushes_it_again()
    {
        using var context = await ArrangeAsync();
        var account = context.Accounts.Add(AccountId, "acme");
        await context.LocalSubmission.PublishAsync(account, CancellationToken.None);
        await context.SeedDomainAsync(AccountId, "new.test");
        context.Agent.FailNext("SetLocalSubmission", Error.Of(Refused, ErrorType.Failure));

        await context.LocalSubmission.TryPublishAfterChangeAsync(account, CancellationToken.None);
        Assert.Equal(MailSubmissionBindingState.OutOfDate, State(context, "acme"));
        Assert.Equal(2, context.Agent.LocalSubmissions.Count);

        await context.LocalSubmission.ReconcileAsync(pushEveryAccount: false, CancellationToken.None);

        Assert.Equal(3, context.Agent.LocalSubmissions.Count);
        Assert.Equal(["new.test"], context.Agent.LocalSubmissions[2].Domains);
        Assert.Equal(MailSubmissionBindingState.Bound, State(context, "acme"));
    }

    /// <summary>The inverse control: a current binding is not pushed again by the reconcile pass, unless pushing every account is asked for.</summary>
    [Fact]
    public async Task A_current_binding_is_left_alone_unless_every_account_is_pushed()
    {
        using var context = await ArrangeAsync();
        var account = context.Accounts.Add(AccountId, "acme");
        await context.LocalSubmission.PublishAsync(account, CancellationToken.None);

        await context.LocalSubmission.ReconcileAsync(pushEveryAccount: false, CancellationToken.None);
        Assert.Single(context.Agent.LocalSubmissions);

        await context.LocalSubmission.ReconcileAsync(pushEveryAccount: true, CancellationToken.None);
        Assert.Equal(2, context.Agent.LocalSubmissions.Count);
    }

    /// <summary>An account deleted while mail was off keeps its binding on the host; enabling removes it and forgets the row.</summary>
    [Fact]
    public async Task A_binding_of_an_account_deleted_while_mail_was_off_is_removed_when_mail_is_on()
    {
        using var context = await ArrangeAsync();
        var gone = context.Accounts.Add(AccountId, "gone");
        await context.LocalSubmission.PublishAsync(gone, CancellationToken.None);
        context.Accounts.Remove(AccountId);
        context.Database.MailServerSettings.RemoveRange(context.Database.MailServerSettings);
        await context.Database.SaveChangesAsync();

        var removedWhileOff = await context.LocalSubmission.RemoveAsync("gone", CancellationToken.None);
        Assert.True(removedWhileOff.IsSuccess);
        Assert.Empty(context.Agent.LocalSubmissionRemovals);
        Assert.Equal(MailSubmissionBindingState.WithdrawalPending, State(context, "gone"));

        await context.SeedEnabledServerAsync();
        await context.LocalSubmission.ReconcileAsync(pushEveryAccount: true, CancellationToken.None);

        Assert.Equal(["gone"], context.Agent.LocalSubmissionRemovals);
        Assert.Null(State(context, "gone"));
    }

    /// <summary>A suspended account's binding is removed by the reconcile pass; an active neighbour is pushed. A user the ledger never knew is left alone.</summary>
    [Fact]
    public async Task The_reconcile_pass_removes_a_suspended_accounts_binding_and_pushes_the_active_one()
    {
        using var context = await ArrangeAsync();
        var suspended = context.Accounts.Add(AccountId, "sleeper");
        context.Accounts.Add(OtherAccountId, "worker");
        await context.LocalSubmission.PublishAsync(suspended, CancellationToken.None);
        context.Accounts.Add(AccountId, "sleeper", null, AccountState.Suspended);
        context.Agent.LocalSubmissions.Clear();

        await context.LocalSubmission.ReconcileAsync(pushEveryAccount: true, CancellationToken.None);

        Assert.Equal(["sleeper"], context.Agent.LocalSubmissionRemovals);
        Assert.Equal(
            ["worker"],
            context.Agent.LocalSubmissions.Select(sent =>
            {
                return sent.AccountUsername;
            }).ToList());
        Assert.Equal(MailSubmissionBindingState.Withdrawn, State(context, "sleeper"));
    }

    /// <summary>A removal the agent answers with NotFound is a success; any other refusal is returned and stays owed.</summary>
    [Fact]
    public async Task An_already_unbound_user_is_a_successful_removal_and_another_refusal_is_not()
    {
        using var context = await ArrangeAsync();
        context.Agent.FailNext("RemoveLocalSubmission", Error.Of("AgentNotFound", ErrorType.NotFound));

        var unbound = await context.LocalSubmission.RemoveAsync("acme", CancellationToken.None);

        Assert.True(unbound.IsSuccess);
        Assert.Equal(MailSubmissionBindingState.Withdrawn, State(context, "acme"));

        context.Agent.FailNext("RemoveLocalSubmission", Error.Of(Refused, ErrorType.Failure));
        var refused = await context.LocalSubmission.RemoveAsync("acme", CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailSubmissionBindingState.WithdrawalPending, State(context, "acme"));
    }

    /// <summary>A domain change that arrives after a suspension removed the binding does not bind the account again.</summary>
    [Fact]
    public async Task A_change_after_a_suspension_does_not_bind_the_account_again()
    {
        using var context = await ArrangeAsync();
        var account = context.Accounts.Add(AccountId, "acme");
        await context.LocalSubmission.PublishAsync(account, CancellationToken.None);
        await context.LocalSubmission.RemoveAsync("acme", CancellationToken.None);
        context.Agent.LocalSubmissions.Clear();

        // The suspension event precedes the state change, so the snapshot a racing change holds still says Active.
        await context.LocalSubmission.TryPublishAfterChangeAsync(account, CancellationToken.None);

        Assert.Empty(context.Agent.LocalSubmissions);

        // The control: the resumption's explicit publish binds the account again, and then a change pushes.
        await context.LocalSubmission.PublishAsync(account, CancellationToken.None);
        await context.LocalSubmission.TryPublishAfterChangeAsync(account, CancellationToken.None);

        Assert.Equal(2, context.Agent.LocalSubmissions.Count);
    }

    /// <summary>A suspension that is abandoned after the binding was removed puts the binding back.</summary>
    [Fact]
    public async Task An_abandoned_suspension_puts_the_binding_back()
    {
        using var context = await ArrangeAsync();
        context.Accounts.Add(AccountId, "acme");
        context.Agent.FailNext("SetAccountSuspension", Error.Of(Refused, ErrorType.Failure));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
        {
            return new AccountSuspendingHandler(
                    context.Agent, context.Server, context.LocalSubmission)
                .HandleAsync(new AccountSuspending(AccountId, "acme"), CancellationToken.None);
        });

        Assert.Equal(["acme"], context.Agent.LocalSubmissionRemovals);
        Assert.Equal("acme", Assert.Single(context.Agent.LocalSubmissions).AccountUsername);
        Assert.Equal(MailSubmissionBindingState.Bound, State(context, "acme"));
    }

    /// <summary>Builds a context with mail on.</summary>
    /// <returns>The context.</returns>
    private static async Task<MailTestContext> ArrangeAsync()
    {
        var context = new MailTestContext();
        await context.SeedEnabledServerAsync();

        return context;
    }

    /// <summary>Reads the ledger state of one user.</summary>
    /// <param name="context">The test context.</param>
    /// <param name="username">System username.</param>
    /// <returns>The state, or <c>null</c> when the ledger has no row.</returns>
    private static MailSubmissionBindingState? State(MailTestContext context, string username)
    {
        return context.Database.MailSubmissionBindings
            .AsNoTracking()
            .Where(row => row.Username == username)
            .Select(row => (MailSubmissionBindingState?)row.State)
            .SingleOrDefault();
    }
}
