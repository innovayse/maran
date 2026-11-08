using Maran.Modules.Mail.Commands.DeleteMailbox;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.DeleteMailbox;

/// <summary>
/// Deleting a mailbox: the host's mailbox and data go first, a host that no longer knows it is accepted, the
/// autoresponder goes with the row, and another account's mailbox is invisible.
/// </summary>
public sealed class DeleteMailboxCommandHandlerTests
{
    /// <summary>The account that owns the mailbox in these tests.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose mailbox a customer of <see cref="AccountId"/> must not reach.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>Deleting removes the host's mailbox with its data, then the row and its autoresponder, and journals it.</summary>
    [Fact]
    public async Task Deleting_a_mailbox_removes_the_host_mailbox_with_its_data_then_the_rows_and_journals_it()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        context.Database.MailAutoresponders.Add(new MailAutoresponder(
            Guid.NewGuid(), mailbox.Id, "Away", "Back soon", null, null, null));
        await context.Database.SaveChangesAsync(CancellationToken.None);

        var result = await MailboxGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailboxCommand(mailbox.Id, "203.0.113.7", "agent/1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ["RemoveMailbox(acme,info@example.com,deleteData=True)"],
            context.Agent.Calls);
        Assert.Empty(context.Database.Mailboxes);
        Assert.Empty(context.Database.MailAutoresponders);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailboxDeleted, entry.Action);
        Assert.Equal("info@example.com", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A host that no longer holds the mailbox is accepted as done, and the row is still removed.</summary>
    [Fact]
    public async Task A_host_that_no_longer_holds_the_mailbox_is_accepted_and_the_row_is_removed()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        context.Agent.FailNext("RemoveMailbox", Error.Of(MailboxGroupErrorCodes.AgentNotFound, ErrorType.NotFound));

        var result = await MailboxGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailboxCommand(mailbox.Id, string.Empty, string.Empty), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.Database.Mailboxes);
    }

    /// <summary>Any other agent failure keeps the row, so the mail it points at stays visible and the delete can be retried.</summary>
    [Fact]
    public async Task Any_other_agent_failure_keeps_the_row()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        context.Agent.FailNext("RemoveMailbox", Error.Of(MailboxGroupErrorCodes.AgentUnavailable, ErrorType.Failure));

        var result = await MailboxGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailboxCommand(mailbox.Id, string.Empty, string.Empty), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.AgentUnavailable, result.Error!.Code);
        Assert.Equal(1, await context.Database.Mailboxes.CountAsync(CancellationToken.None));
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>An unknown mailbox answers not found, journals the probed identifier and calls no agent.</summary>
    [Fact]
    public async Task Deleting_a_missing_mailbox_is_not_found_and_leaves_a_trace()
    {
        using var context = new MailTestContext();
        await ArrangeAsync(context);
        var probed = Guid.NewGuid();

        var result = await MailboxGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailboxCommand(probed, string.Empty, string.Empty), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
        var entry = Assert.Single(context.Entries);
        Assert.Equal(probed.ToString(), entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>With mail switched off nothing is deleted and the host is not touched.</summary>
    [Fact]
    public async Task Deleting_a_mailbox_while_mail_is_off_is_refused()
    {
        using var context = new MailTestContext();

        var result = await MailboxGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailboxCommand(Guid.NewGuid(), string.Empty, string.Empty), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailNotEnabled, result.Error!.Code);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A mailbox whose account the directory cannot resolve is refused, never sent on with a placeholder name.</summary>
    [Fact]
    public async Task Deleting_a_mailbox_of_an_unresolvable_account_is_refused_without_calling_the_agent()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        var mailbox = await context.SeedMailboxAsync(domain, "info");

        var result = await MailboxGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailboxCommand(mailbox.Id, string.Empty, string.Empty), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.AccountNotFound, result.Error!.Code);
        Assert.Empty(context.Agent.Calls);
        Assert.Equal(1, await context.Database.Mailboxes.CountAsync(CancellationToken.None));
    }

    /// <summary>A customer deleting another account's mailbox gets not found, and the owner's control succeeds.</summary>
    [Fact]
    public async Task A_customer_cannot_delete_another_accounts_mailbox()
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

        var refused = await MailboxGroupHandlers.Delete(intruder).HandleAsync(
            new DeleteMailboxCommand(mailbox.Id, string.Empty, string.Empty), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, refused.Error!.Code);
        Assert.Equal(ErrorType.NotFound, refused.Error.Type);
        Assert.Empty(intruder.Agent.Calls);
        Assert.Equal(1, await owner.Database.Mailboxes.CountAsync(CancellationToken.None));

        var accepted = await MailboxGroupHandlers.Delete(owner).HandleAsync(
            new DeleteMailboxCommand(mailbox.Id, string.Empty, string.Empty), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.Empty(owner.Database.Mailboxes);
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
