using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Queries.GetMailboxAutoresponder;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Queries.GetMailboxAutoresponder;

/// <summary>
/// Reading a mailbox's automatic reply: the caller's own reply is found, an own mailbox without one and a
/// mailbox that is not the caller's answer differently, and another account's reply is invisible.
/// </summary>
public sealed class GetMailboxAutoresponderQueryHandlerTests
{
    /// <summary>The account whose mailboxes the caller in these tests owns.</summary>
    private static readonly Guid CustomerA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose reply must not be readable by <see cref="CustomerA"/>.</summary>
    private static readonly Guid CustomerB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The caller's own reply is returned with every field.</summary>
    [Fact]
    public async Task The_callers_own_reply_is_returned()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(CustomerA), Guid.NewGuid().ToString());
        var mailbox = await SeedWithReplyAsync(context, CustomerA, "acme.example");

        var result = await MailboxGroupHandlers.GetAutoresponder(context).HandleAsync(
            new GetMailboxAutoresponderQuery(mailbox.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(mailbox.Id, result.Value.MailboxId);
        Assert.Equal("Away", result.Value.Subject);
        Assert.Equal("Back soon", result.Value.Body);
        Assert.Equal(4, result.Value.ReplyIntervalDays);
    }

    /// <summary>A mailbox of the caller's own with no reply answers its own not-found code.</summary>
    [Fact]
    public async Task An_own_mailbox_without_a_reply_answers_reply_not_found()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(CustomerA), Guid.NewGuid().ToString());
        var domain = await context.SeedDomainAsync(CustomerA, "acme.example");
        var mailbox = await context.SeedMailboxAsync(domain, "info");

        var result = await MailboxGroupHandlers.GetAutoresponder(context).HandleAsync(
            new GetMailboxAutoresponderQuery(mailbox.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailAutoresponderNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>A mailbox that does not exist answers mailbox not found.</summary>
    [Fact]
    public async Task A_missing_mailbox_answers_mailbox_not_found()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(CustomerA), Guid.NewGuid().ToString());

        var result = await MailboxGroupHandlers.GetAutoresponder(context).HandleAsync(
            new GetMailboxAutoresponderQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    /// <summary>A customer asking for another account's reply gets mailbox not found, and the owner's control reads it.</summary>
    [Fact]
    public async Task Another_accounts_reply_is_invisible_and_the_owner_reads_it()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(CustomerB), databaseName);
        var mailbox = await SeedWithReplyAsync(owner, CustomerB, "beta.example");
        using var intruder = new MailTestContext(new CustomerCurrentUser(CustomerA), databaseName);

        var refused = await MailboxGroupHandlers.GetAutoresponder(intruder).HandleAsync(
            new GetMailboxAutoresponderQuery(mailbox.Id), CancellationToken.None);
        var accepted = await MailboxGroupHandlers.GetAutoresponder(owner).HandleAsync(
            new GetMailboxAutoresponderQuery(mailbox.Id), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, refused.Error!.Code);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>Seeds a domain, a mailbox and its reply for an account.</summary>
    /// <param name="context">The context to seed through.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domainName">The domain's name.</param>
    /// <returns>The mailbox that has the reply.</returns>
    private static async Task<Mailbox> SeedWithReplyAsync(MailTestContext context, Guid accountId, string domainName)
    {
        var domain = await context.SeedDomainAsync(accountId, domainName);
        var mailbox = await context.SeedMailboxAsync(domain, "info");
        context.Database.MailAutoresponders.Add(new MailAutoresponder(
            Guid.NewGuid(), mailbox.Id, "Away", "Back soon", null, null, 4));
        await context.Database.SaveChangesAsync(CancellationToken.None);

        return mailbox;
    }
}
