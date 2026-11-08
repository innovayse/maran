using Maran.Modules.Mail.Commands.DeleteMailDomain;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.DeleteMailDomain;

/// <summary>
/// Deleting a mail domain: every mailbox leaves the host with its data, then the domain, then the rows;
/// another account's domain is invisible; a host that no longer holds something does not stop the deletion.
/// </summary>
public sealed class DeleteMailDomainCommandHandlerTests
{
    /// <summary>The account that owns the domain in these tests.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>Another account whose context must not reach the first one's domain.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>Deletion removes the mailboxes with their data, then the domain, then every row, and journals it.</summary>
    [Fact]
    public async Task Deleting_a_domain_removes_its_mailboxes_then_the_domain_then_every_row()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        var mailbox = await context.SeedMailboxAsync(domain, "info");
        await context.SeedMailboxAsync(domain, "sales");
        await SeedAliasAndAutoresponderAsync(context, domain, mailbox);

        var result = await DomainGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailDomainCommand(domain.Id, "10.0.0.1", "agent/1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, context.Agent.Calls.Count);
        Assert.Equal(
            ["RemoveMailbox(acme,info@example.com,deleteData=True)",
                "RemoveMailbox(acme,sales@example.com,deleteData=True)",],
            context.Agent.Calls.Take(2).Order(StringComparer.Ordinal));
        Assert.Equal("RemoveDomain(acme,example.com)", context.Agent.Calls[^1]);
        Assert.Empty(await context.Database.MailDomains.ToListAsync());
        Assert.Empty(await context.Database.DkimKeys.ToListAsync());
        Assert.Empty(await context.Database.Mailboxes.ToListAsync());
        Assert.Empty(await context.Database.MailAliases.ToListAsync());
        Assert.Empty(await context.Database.MailAliasTargets.ToListAsync());
        Assert.Empty(await context.Database.MailAutoresponders.ToListAsync());

        var entry = Assert.Single(context.Entries);
        Assert.Equal("MailDomainDeleted", entry.Action);
        Assert.Equal("example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A host that no longer holds the mailbox or the domain does not stop the deletion.</summary>
    [Fact]
    public async Task A_host_that_already_lost_the_mailbox_and_domain_does_not_stop_the_deletion()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        await context.SeedMailboxAsync(domain, "info");
        context.Agent.FailNext("RemoveMailbox", Error.Of("AgentNotFound", ErrorType.NotFound));
        context.Agent.FailNext("RemoveDomain", Error.Of("AgentNotFound", ErrorType.NotFound));

        var result = await DomainGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailDomainCommand(domain.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await context.Database.MailDomains.ToListAsync());
        Assert.Empty(await context.Database.Mailboxes.ToListAsync());
    }

    /// <summary>Any other agent failure stops the deletion and keeps every row.</summary>
    [Fact]
    public async Task Another_agent_failure_stops_the_deletion_and_keeps_every_row()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        await context.SeedMailboxAsync(domain, "info");
        context.Agent.FailNext("RemoveMailbox", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await DomainGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailDomainCommand(domain.Id), CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Single(await context.Database.MailDomains.ToListAsync());
        Assert.Single(await context.Database.Mailboxes.ToListAsync());
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>A failing domain removal on the host keeps the rows too.</summary>
    [Fact]
    public async Task A_failing_domain_removal_keeps_the_rows()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");
        context.Agent.FailNext("RemoveDomain", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await DomainGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailDomainCommand(domain.Id), CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Single(await context.Database.MailDomains.ToListAsync());
    }

    /// <summary>A domain that does not exist is not found, and the probe is journaled with the id asked for.</summary>
    [Fact]
    public async Task A_domain_that_does_not_exist_is_not_found()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        var missing = Guid.NewGuid();

        var result = await DomainGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailDomainCommand(missing), CancellationToken.None);

        Assert.Equal("MailDomainNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
        var entry = Assert.Single(context.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(missing.ToString(), entry.Subject);
    }

    /// <summary>An owner the directory cannot resolve is answered as account-not-found and the host is not touched.</summary>
    [Fact]
    public async Task A_domain_whose_account_cannot_be_resolved_never_reaches_the_host()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        var result = await DomainGroupHandlers.Delete(context).HandleAsync(
            new DeleteMailDomainCommand(domain.Id), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(context.Agent.Calls);
        Assert.Single(await context.Database.MailDomains.ToListAsync());
    }

    /// <summary>Another account's domain answers not found and survives; the owner's own context deletes it.</summary>
    [Fact]
    public async Task Another_accounts_domain_answers_not_found_and_survives()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(AccountId), databaseName);
        owner.Accounts.Add(AccountId, "acme");
        var domain = await owner.SeedDomainAsync(AccountId, "example.com");

        using var intruder = new MailTestContext(new CustomerCurrentUser(OtherAccountId), databaseName);
        intruder.Accounts.Add(OtherAccountId, "beta");
        var refused = await DomainGroupHandlers.Delete(intruder).HandleAsync(
            new DeleteMailDomainCommand(domain.Id), CancellationToken.None);

        Assert.Equal("MailDomainNotFound", refused.Error!.Code);
        Assert.Equal(ErrorType.NotFound, refused.Error.Type);
        Assert.Empty(intruder.Agent.Calls);
        Assert.Single(await owner.Database.MailDomains.ToListAsync());

        var accepted = await DomainGroupHandlers.Delete(owner).HandleAsync(
            new DeleteMailDomainCommand(domain.Id), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
        Assert.Empty(await owner.Database.MailDomains.ToListAsync());
    }

    /// <summary>Gives the domain one alias with two targets and an autoresponder on the mailbox.</summary>
    /// <param name="context">The context to seed through.</param>
    /// <param name="domain">The domain the alias lives in.</param>
    /// <param name="mailbox">The mailbox that gets the autoresponder.</param>
    private static async Task SeedAliasAndAutoresponderAsync(
        MailTestContext context,
        MailDomain domain,
        Mailbox mailbox)
    {
        context.Database.MailAliases.Add(new MailAlias(
            Guid.NewGuid(),
            domain.AccountId,
            domain.Id,
            "team",
            MailAliasKind.Forwarder,
            ["a@example.com", "b@example.com"],
            context.Clock.UtcNow));
        context.Database.MailAutoresponders.Add(new MailAutoresponder(
            Guid.NewGuid(), mailbox.Id, "Away", "Back soon", null, null, 7));
        await context.Database.SaveChangesAsync();
    }
}
