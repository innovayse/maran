using Maran.Modules.Mail.Commands.UpdateMailbox;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.UpdateMailbox;

/// <summary>
/// Changing a mailbox's limits and switch: the plan's send ceiling, the quota pool, the order of agent and
/// row, the host being put back when the row refuses, and another account's mailbox being invisible.
/// </summary>
public sealed class UpdateMailboxCommandHandlerTests
{
    /// <summary>The account that owns the mailboxes in these tests.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose mailbox a customer of <see cref="AccountId"/> must not reach.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>New limits and a switched-off mailbox reach the agent first and then the row, and are journaled.</summary>
    [Fact]
    public async Task Updating_a_mailbox_changes_the_host_then_the_row_and_journals_it()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, null);

        var result = await MailboxGroupHandlers.Update(context).HandleAsync(
            new UpdateMailboxCommand(2048, 50, false, mailbox.Id, "203.0.113.7", "agent/1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var stored = await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(2048, stored.QuotaMib);
        Assert.Equal(50, stored.SendLimitPerHour);
        Assert.False(stored.Enabled);

        Assert.Equal("info@example.com", result.Value.Address);
        Assert.Equal(2048, result.Value.QuotaMib);
        Assert.Equal(MailboxStatus.Disabled, result.Value.Status);
        Assert.Equal("MailboxStatusDisabled", result.Value.StatusName);

        Assert.Equal(
            ["SetMailbox(acme,info@example.com,quota=2048,limit=50,disabled=True,hash=False)"],
            context.Agent.Calls);
        Assert.Null(context.Agent.LastMailbox!.PasswordHash);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailboxUpdated, entry.Action);
        Assert.Equal("info@example.com", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A switched-off mailbox can be switched on again, and a suspended account still reads as suspended.</summary>
    [Fact]
    public async Task Switching_a_mailbox_on_again_persists_it_and_a_suspended_account_still_reads_suspended()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, null);
        mailbox.Disable();
        await context.Database.SaveChangesAsync(CancellationToken.None);
        context.Accounts.Add(AccountId, "acme", null, AccountState.Suspended);

        var result = await MailboxGroupHandlers.Update(context).HandleAsync(
            new UpdateMailboxCommand(1024, 100, true, mailbox.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True((await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None)).Enabled);
        Assert.Equal(MailboxStatus.Suspended, result.Value.Status);
        Assert.Equal("MailboxStatusSuspended", result.Value.StatusName);
    }

    /// <summary>A send limit above the plan's is refused before the host is touched; the plan's own figure is allowed.</summary>
    [Fact]
    public async Task A_send_limit_above_the_plan_is_refused_and_the_plans_own_limit_is_not()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, new MailLimitsSnapshot(10, 1024, 100, 10240));

        var refused = await MailboxGroupHandlers.Update(context).HandleAsync(
            new UpdateMailboxCommand(1024, 101, true, mailbox.Id), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxSendLimitAbovePlan, refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(context.Agent.Calls);

        var accepted = await MailboxGroupHandlers.Update(context).HandleAsync(
            new UpdateMailboxCommand(1024, 100, true, mailbox.Id), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
    }

    /// <summary>
    /// A quota the pool cannot hold is refused after the host accepted it: the host is sent the OLD values
    /// again and the row, switch included, is as it was.
    /// </summary>
    [Fact]
    public async Task A_quota_the_pool_cannot_hold_puts_the_host_and_the_row_back()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, new MailLimitsSnapshot(10, 1024, 100, 2048));
        await context.SeedMailboxAsync(await context.Database.MailDomains.SingleAsync(CancellationToken.None), "other");

        var result = await MailboxGroupHandlers.Update(context).HandleAsync(
            new UpdateMailboxCommand(2048, 50, false, mailbox.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailQuotaPoolExhausted, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal(
            ["SetMailbox(acme,info@example.com,quota=2048,limit=50,disabled=True,hash=False)",
                "SetMailbox(acme,info@example.com,quota=1024,limit=100,disabled=False,hash=False)",],
            context.Agent.Calls);

        var stored = await context.Database.Mailboxes.AsNoTracking().SingleAsync(row => row.Id == mailbox.Id, CancellationToken.None);
        Assert.Equal(1024, stored.QuotaMib);
        Assert.Equal(100, stored.SendLimitPerHour);
        Assert.True(stored.Enabled);
        Assert.True(mailbox.Enabled);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>The inverse control: a quota that fills the pool exactly is accepted.</summary>
    [Fact]
    public async Task A_quota_that_fills_the_pool_exactly_is_accepted()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, new MailLimitsSnapshot(10, 1024, 100, 2048));
        await context.SeedMailboxAsync(await context.Database.MailDomains.SingleAsync(CancellationToken.None), "other");

        var result = await MailboxGroupHandlers.Update(context).HandleAsync(
            new UpdateMailboxCommand(1024, 50, true, mailbox.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.Agent.Calls);
    }

    /// <summary>A row write that fails after the host accepted the change puts the host back and says so.</summary>
    [Fact]
    public async Task A_row_write_failure_puts_the_host_back_and_reports_a_provisioning_failure()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, null);
        context.SaveFailure.Next = new DbUpdateException("connection dropped", new InvalidOperationException("dropped"));

        var result = await MailboxGroupHandlers.Update(context).HandleAsync(
            new UpdateMailboxCommand(2048, 50, false, mailbox.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxProvisioningFailed, result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal(2, context.Agent.Calls.Count);
        Assert.Equal(
            "SetMailbox(acme,info@example.com,quota=1024,limit=100,disabled=False,hash=False)",
            context.Agent.Calls[1]);
        Assert.Equal(1024, (await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None)).QuotaMib);
    }

    /// <summary>An agent that refuses the change leaves the row alone.</summary>
    [Fact]
    public async Task An_agent_failure_leaves_the_row_unchanged()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, null);
        context.Agent.FailNext("SetMailbox", Error.Of(MailboxGroupErrorCodes.AgentUnavailable, ErrorType.Failure));

        var result = await MailboxGroupHandlers.Update(context).HandleAsync(
            new UpdateMailboxCommand(2048, 50, false, mailbox.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.AgentUnavailable, result.Error!.Code);
        Assert.Single(context.Agent.Calls);
        var stored = await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(1024, stored.QuotaMib);
        Assert.True(stored.Enabled);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>An unknown mailbox, a server with mail off and an unresolvable account each answer their own code.</summary>
    [Fact]
    public async Task Updating_a_missing_mailbox_or_with_mail_off_or_without_an_account_is_refused()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, null);
        var handler = MailboxGroupHandlers.Update(context);

        var missing = await handler.HandleAsync(
            new UpdateMailboxCommand(1024, 100, true, Guid.NewGuid()), CancellationToken.None);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, missing.Error!.Code);
        Assert.Equal(ErrorType.NotFound, missing.Error.Type);

        using var withoutAccount = new MailTestContext(new TestCurrentUser(), context.DatabaseName);
        var noAccount = await MailboxGroupHandlers.Update(withoutAccount).HandleAsync(
            new UpdateMailboxCommand(1024, 100, true, mailbox.Id), CancellationToken.None);
        Assert.Equal(MailboxGroupErrorCodes.AccountNotFound, noAccount.Error!.Code);

        using var mailOff = new MailTestContext();
        var off = await MailboxGroupHandlers.Update(mailOff).HandleAsync(
            new UpdateMailboxCommand(1024, 100, true, mailbox.Id), CancellationToken.None);
        Assert.Equal(MailboxGroupErrorCodes.MailNotEnabled, off.Error!.Code);
        Assert.Equal(ErrorType.Conflict, off.Error.Type);

        Assert.Empty(context.Agent.Calls);
        Assert.Empty(withoutAccount.Agent.Calls);
        Assert.Empty(mailOff.Agent.Calls);
    }

    /// <summary>A customer updating another account's mailbox gets not found, and the owner's control succeeds.</summary>
    [Fact]
    public async Task A_customer_cannot_update_another_accounts_mailbox()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(OtherAccountId), databaseName);
        await owner.SeedEnabledServerAsync();
        owner.Accounts.Add(OtherAccountId, "beta");
        var domain = await owner.SeedDomainAsync(OtherAccountId, "beta.example");
        var mailbox = await owner.SeedMailboxAsync(domain, "info");

        using var intruder = new MailTestContext(new CustomerCurrentUser(AccountId), databaseName);
        intruder.Accounts.Add(AccountId, "acme");
        intruder.Accounts.Add(OtherAccountId, "beta");

        var refused = await MailboxGroupHandlers.Update(intruder).HandleAsync(
            new UpdateMailboxCommand(2048, 50, false, mailbox.Id), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, refused.Error!.Code);
        Assert.Equal(ErrorType.NotFound, refused.Error.Type);
        Assert.Empty(intruder.Agent.Calls);

        var accepted = await MailboxGroupHandlers.Update(owner).HandleAsync(
            new UpdateMailboxCommand(2048, 50, false, mailbox.Id), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
    }

    /// <summary>Seeds an enabled server, the account, one domain and one mailbox <c>info</c>.</summary>
    /// <param name="context">The context to arrange.</param>
    /// <param name="limits">The account's mail allowances, or <c>null</c> for the defaults.</param>
    /// <returns>The mailbox <c>info@example.com</c> with a 1024 MiB quota and a send limit of 100.</returns>
    private static async Task<Mailbox> ArrangeAsync(MailTestContext context, MailLimitsSnapshot? limits)
    {
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme", limits);
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        return await context.SeedMailboxAsync(domain, "info");
    }
}
