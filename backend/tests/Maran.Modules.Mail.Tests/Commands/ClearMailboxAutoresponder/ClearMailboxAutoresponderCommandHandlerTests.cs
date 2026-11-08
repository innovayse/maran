using Maran.Modules.Mail.Commands.ClearMailboxAutoresponder;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.ClearMailboxAutoresponder;

/// <summary>
/// Clearing a mailbox's automatic reply: the host is always told, the row goes, a mailbox with none still
/// succeeds, and another account's mailbox is invisible.
/// </summary>
public sealed class ClearMailboxAutoresponderCommandHandlerTests
{
    /// <summary>The account that owns the mailbox in these tests.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose mailbox a customer of <see cref="AccountId"/> must not reach.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>Clearing tells the host to clear, removes the row, and journals the action.</summary>
    [Fact]
    public async Task Clearing_a_reply_clears_the_host_removes_the_row_and_journals_it()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, withReply: true);

        var result = await MailboxGroupHandlers.ClearAutoresponder(context).HandleAsync(
            new ClearMailboxAutoresponderCommand(mailbox.Id, "203.0.113.7", "agent/1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["SetAutoresponder(acme,info@example.com,set=False)"], context.Agent.Calls);
        Assert.Null(context.Agent.LastAutoresponder);
        Assert.Empty(context.Database.MailAutoresponders);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailAutoresponderCleared, entry.Action);
        Assert.Equal("info@example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A mailbox with no reply still succeeds, and the host is still told, so a stray reply is cleared too.</summary>
    [Fact]
    public async Task Clearing_a_mailbox_with_no_reply_succeeds_and_still_tells_the_host()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, withReply: false);

        var result = await MailboxGroupHandlers.ClearAutoresponder(context).HandleAsync(
            new ClearMailboxAutoresponderCommand(mailbox.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.Agent.Calls);
    }

    /// <summary>An agent that refuses keeps the row, so the panel still shows the reply the host still sends.</summary>
    [Fact]
    public async Task An_agent_failure_keeps_the_row()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context, withReply: true);
        context.Agent.FailNext("SetAutoresponder", Error.Of(MailboxGroupErrorCodes.AgentUnavailable, ErrorType.Failure));

        var result = await MailboxGroupHandlers.ClearAutoresponder(context).HandleAsync(
            new ClearMailboxAutoresponderCommand(mailbox.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.AgentUnavailable, result.Error!.Code);
        Assert.Equal(1, await context.Database.MailAutoresponders.CountAsync(CancellationToken.None));
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>An unknown mailbox answers not found and calls no agent.</summary>
    [Fact]
    public async Task Clearing_a_missing_mailbox_is_not_found()
    {
        using var context = new MailTestContext();
        await ArrangeAsync(context, withReply: true);

        var result = await MailboxGroupHandlers.ClearAutoresponder(context).HandleAsync(
            new ClearMailboxAutoresponderCommand(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, result.Error!.Code);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A customer clearing another account's reply gets not found, the reply survives, and the owner's control succeeds.</summary>
    [Fact]
    public async Task A_customer_cannot_clear_another_accounts_reply()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(OtherAccountId), databaseName);
        await owner.SeedEnabledServerAsync();
        owner.Accounts.Add(OtherAccountId, "beta");
        var domain = await owner.SeedDomainAsync(OtherAccountId, "beta.example");
        var mailbox = await owner.SeedMailboxAsync(domain, "info");
        owner.Database.MailAutoresponders.Add(new MailAutoresponder(
            Guid.NewGuid(), mailbox.Id, "Away", "Back soon", null, null, null));
        await owner.Database.SaveChangesAsync(CancellationToken.None);

        using var intruder = new MailTestContext(new CustomerCurrentUser(AccountId), databaseName);
        intruder.Accounts.Add(AccountId, "acme");
        intruder.Accounts.Add(OtherAccountId, "beta");

        var refused = await MailboxGroupHandlers.ClearAutoresponder(intruder).HandleAsync(
            new ClearMailboxAutoresponderCommand(mailbox.Id), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, refused.Error!.Code);
        Assert.Empty(intruder.Agent.Calls);
        Assert.Equal(1, await owner.Database.MailAutoresponders.CountAsync(CancellationToken.None));

        var accepted = await MailboxGroupHandlers.ClearAutoresponder(owner).HandleAsync(
            new ClearMailboxAutoresponderCommand(mailbox.Id), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.Empty(owner.Database.MailAutoresponders);
    }

    /// <summary>Seeds an enabled server, the account, one domain, one mailbox and optionally its reply.</summary>
    /// <param name="context">The context to arrange.</param>
    /// <param name="withReply">Whether the mailbox already has an automatic reply.</param>
    /// <returns>The mailbox <c>info@example.com</c>.</returns>
    private static async Task<Mailbox> ArrangeAsync(MailTestContext context, bool withReply)
    {
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        var mailbox = await context.SeedMailboxAsync(domain, "info");
        if (withReply)
        {
            context.Database.MailAutoresponders.Add(new MailAutoresponder(
                Guid.NewGuid(), mailbox.Id, "Away", "Back soon", null, null, null));
            await context.Database.SaveChangesAsync(CancellationToken.None);
        }

        return mailbox;
    }
}
