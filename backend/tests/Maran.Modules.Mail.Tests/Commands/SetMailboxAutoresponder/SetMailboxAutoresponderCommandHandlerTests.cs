using Maran.Modules.Mail.Commands.SetMailboxAutoresponder;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.SetMailboxAutoresponder;

/// <summary>
/// Setting a mailbox's automatic reply: the agent first, an upsert of the one row, the previous reply
/// being put back when the row refuses, and another account's mailbox being invisible.
/// </summary>
public sealed class SetMailboxAutoresponderCommandHandlerTests
{
    /// <summary>The account that owns the mailbox in these tests.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose mailbox a customer of <see cref="AccountId"/> must not reach.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The start of the reply's window.</summary>
    private static readonly DateTimeOffset Start = new(2026, 12, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The end of the reply's window.</summary>
    private static readonly DateTimeOffset End = new(2026, 12, 15, 0, 0, 0, TimeSpan.Zero);

    /// <summary>A first reply reaches the agent and is recorded as the mailbox's one row, and the action is journaled.</summary>
    [Fact]
    public async Task Setting_a_first_reply_installs_it_records_it_and_journals_it()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);

        var result = await MailboxGroupHandlers.SetAutoresponder(context).HandleAsync(
            new SetMailboxAutoresponderCommand("Away", "Back soon", Start, End, 7, mailbox.Id, "203.0.113.7", "agent/1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(mailbox.Id, result.Value.MailboxId);
        Assert.Equal("Away", result.Value.Subject);
        Assert.Equal(7, result.Value.ReplyIntervalDays);

        var row = await context.Database.MailAutoresponders.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(mailbox.Id, row.MailboxId);
        Assert.Equal("Back soon", row.Body);
        Assert.Equal(Start, row.StartsAt);
        Assert.Equal(End, row.EndsAt);

        Assert.Equal(["SetAutoresponder(acme,info@example.com,set=True)"], context.Agent.Calls);
        Assert.Equal("Away", context.Agent.LastAutoresponder!.Subject);
        Assert.Equal(7u, context.Agent.LastAutoresponder.ReplyIntervalDays);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailAutoresponderSet, entry.Action);
        Assert.Equal("info@example.com", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A second reply replaces the first: one row per mailbox, holding the newer text.</summary>
    [Fact]
    public async Task Setting_a_second_reply_replaces_the_first_and_keeps_one_row()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        var handler = MailboxGroupHandlers.SetAutoresponder(context);
        await handler.HandleAsync(
            new SetMailboxAutoresponderCommand("Away", "Back soon", null, null, null, mailbox.Id),
            CancellationToken.None);

        var result = await handler.HandleAsync(
            new SetMailboxAutoresponderCommand("Gone", "Back later", Start, null, 3, mailbox.Id),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = await context.Database.MailAutoresponders.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal("Gone", row.Subject);
        Assert.Equal("Back later", row.Body);
        Assert.Equal(3, row.ReplyIntervalDays);
        Assert.Equal(2, context.Agent.Calls.Count);
    }

    /// <summary>A row write that fails after the host accepted a first reply clears the host again.</summary>
    [Fact]
    public async Task A_row_write_failure_on_a_first_reply_clears_the_host_again()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        context.SaveFailure.Next = new DbUpdateException("connection dropped", new InvalidOperationException("dropped"));

        var result = await MailboxGroupHandlers.SetAutoresponder(context).HandleAsync(
            new SetMailboxAutoresponderCommand("Away", "Back soon", null, null, null, mailbox.Id),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxProvisioningFailed, result.Error!.Code);
        Assert.Equal(
            ["SetAutoresponder(acme,info@example.com,set=True)",
                "SetAutoresponder(acme,info@example.com,set=False)",],
            context.Agent.Calls);
        Assert.Null(context.Agent.LastAutoresponder);
        Assert.Empty(context.Database.MailAutoresponders);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>A row write that fails while replacing a reply sends the previous reply back and keeps the old row.</summary>
    [Fact]
    public async Task A_row_write_failure_on_a_replacement_sends_the_previous_reply_back()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        var handler = MailboxGroupHandlers.SetAutoresponder(context);
        await handler.HandleAsync(
            new SetMailboxAutoresponderCommand("Away", "Back soon", null, null, 5, mailbox.Id),
            CancellationToken.None);
        context.SaveFailure.Next = new DbUpdateException("connection dropped", new InvalidOperationException("dropped"));

        var result = await handler.HandleAsync(
            new SetMailboxAutoresponderCommand("Gone", "Back later", null, null, 9, mailbox.Id),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("Away", context.Agent.LastAutoresponder!.Subject);
        Assert.Equal(5u, context.Agent.LastAutoresponder.ReplyIntervalDays);
        var row = await context.Database.MailAutoresponders.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal("Away", row.Subject);
    }

    /// <summary>An agent that refuses the reply leaves no row.</summary>
    [Fact]
    public async Task An_agent_failure_leaves_no_row()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        context.Agent.FailNext("SetAutoresponder", Error.Of(MailboxGroupErrorCodes.AgentUnavailable, ErrorType.Failure));

        var result = await MailboxGroupHandlers.SetAutoresponder(context).HandleAsync(
            new SetMailboxAutoresponderCommand("Away", "Back soon", null, null, null, mailbox.Id),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.AgentUnavailable, result.Error!.Code);
        Assert.Empty(context.Database.MailAutoresponders);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>An unknown mailbox answers not found; with mail off the server refusal comes first.</summary>
    [Fact]
    public async Task Setting_a_reply_on_a_missing_mailbox_or_with_mail_off_is_refused()
    {
        using var context = new MailTestContext();
        await ArrangeAsync(context);
        var missing = await MailboxGroupHandlers.SetAutoresponder(context).HandleAsync(
            new SetMailboxAutoresponderCommand("Away", "Back soon", null, null, null, Guid.NewGuid()),
            CancellationToken.None);

        using var mailOff = new MailTestContext();
        var off = await MailboxGroupHandlers.SetAutoresponder(mailOff).HandleAsync(
            new SetMailboxAutoresponderCommand("Away", "Back soon", null, null, null, Guid.NewGuid()),
            CancellationToken.None);

        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, missing.Error!.Code);
        Assert.Equal(ErrorType.NotFound, missing.Error.Type);
        Assert.Equal(MailboxGroupErrorCodes.MailNotEnabled, off.Error!.Code);
        Assert.Empty(context.Agent.Calls);
        Assert.Empty(mailOff.Agent.Calls);
    }

    /// <summary>A customer setting a reply on another account's mailbox gets not found, and the owner's control succeeds.</summary>
    [Fact]
    public async Task A_customer_cannot_set_a_reply_on_another_accounts_mailbox()
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

        var refused = await MailboxGroupHandlers.SetAutoresponder(intruder).HandleAsync(
            new SetMailboxAutoresponderCommand("Away", "Back soon", null, null, null, mailbox.Id),
            CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, refused.Error!.Code);
        Assert.Empty(intruder.Agent.Calls);
        Assert.Empty(owner.Database.MailAutoresponders);

        var accepted = await MailboxGroupHandlers.SetAutoresponder(owner).HandleAsync(
            new SetMailboxAutoresponderCommand("Away", "Back soon", null, null, null, mailbox.Id),
            CancellationToken.None);

        Assert.True(accepted.IsSuccess);
    }

    /// <summary>Seeds an enabled server, the account, one domain and one mailbox <c>info</c>.</summary>
    /// <param name="context">The context to arrange.</param>
    /// <returns>The mailbox <c>info@example.com</c>.</returns>
    private static async Task<Mailbox> ArrangeAsync(MailTestContext context)
    {
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        return await context.SeedMailboxAsync(domain, "info");
    }
}
