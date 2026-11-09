using System.Text.Json;
using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Commands.CreateMailbox;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.CreateMailbox;

/// <summary>
/// Creating a mailbox: the plan limit and the quota pool, the shared address space, the order of agent
/// and row, the compensation when the row cannot be written, and the password that must reach nobody but
/// the hasher.
/// </summary>
public sealed class CreateMailboxCommandHandlerTests
{
    /// <summary>The account every test in this file creates mailboxes for.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose domain a customer of <see cref="AccountId"/> must not reach.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>A mailbox within the plan is created: the row, the agent call and the audit entry all agree.</summary>
    [Fact]
    public async Task Creating_a_mailbox_records_the_row_calls_the_agent_and_journals_it()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "  Info ", MailboxGroupSeeds.Password, 512, "203.0.113.7", "agent/1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = await context.Database.Mailboxes.SingleAsync(CancellationToken.None);
        Assert.Equal("info", row.LocalPart);
        Assert.Equal(512, row.QuotaMib);
        Assert.Equal(100, row.SendLimitPerHour);
        Assert.True(row.Enabled);
        Assert.Equal(MailSpamAction.FileToJunk, row.SpamAction);
        Assert.Equal(AccountId, row.AccountId);

        Assert.Equal("info@example.com", result.Value.Address);
        Assert.Equal("acme", result.Value.OwnerName);
        Assert.Equal(MailboxStatus.Active, result.Value.Status);
        Assert.Equal("MailboxStatusActive", result.Value.StatusName);
        Assert.Null(result.Value.UsedMib);

        Assert.Equal(
            ["GetStatus()", "SetMailbox(acme,info@example.com,quota=512,limit=100,disabled=False,hash=True)"],
            context.Agent.Calls);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailboxCreated, entry.Action);
        Assert.Equal("info@example.com", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("agent/1", entry.UserAgent);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The send limit the mailbox is created with is the plan's, and spam goes to junk.</summary>
    [Fact]
    public async Task The_mailbox_is_created_with_the_plans_send_limit_and_files_spam_to_junk()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, new MailLimitsSnapshot(10, 1024, 25, 10240));

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(25u, context.Agent.LastMailbox!.SendLimitPerHour);
        Assert.Equal(AgentMailSpamAction.FileToJunk, context.Agent.LastMailbox.SpamAction);
        Assert.False(context.Agent.LastMailbox.Disabled);
        Assert.Equal(25, result.Value.SendLimitPerHour);
    }

    /// <summary>A mailbox beyond the plan's allowance is refused before the agent is called at all.</summary>
    [Fact]
    public async Task Creating_a_mailbox_beyond_the_plans_limit_is_refused_before_the_agent_is_called()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, new MailLimitsSnapshot(1, 1024, 100, 10240));
        await context.SeedMailboxAsync(domain, "first");

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "second", MailboxGroupSeeds.Password, 512),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxLimitReached, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
        Assert.Equal(1, await context.Database.Mailboxes.CountAsync(CancellationToken.None));
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>The inverse control: the same account with one more slot gets its mailbox.</summary>
    [Fact]
    public async Task A_mailbox_within_the_plans_limit_is_created_and_reaches_the_agent()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, new MailLimitsSnapshot(2, 1024, 100, 10240));
        await context.SeedMailboxAsync(domain, "first");

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "second", MailboxGroupSeeds.Password, 512),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(context.Agent.Calls, call =>
        {
            return call.StartsWith("SetMailbox(", StringComparison.Ordinal);
        });
        Assert.Equal(2, await context.Database.Mailboxes.CountAsync(CancellationToken.None));
    }

    /// <summary>A quota the pool cannot hold is refused before the agent is called; the pool's edge is allowed.</summary>
    [Fact]
    public async Task A_quota_beyond_the_pool_is_refused_and_one_that_fills_it_exactly_is_not()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, new MailLimitsSnapshot(10, 1024, 100, 2048));
        await context.SeedMailboxAsync(domain, "first", 1024);

        var refused = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "second", MailboxGroupSeeds.Password, 1025),
            CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailQuotaPoolExhausted, refused.Error!.Code);
        Assert.Equal(ErrorType.Conflict, refused.Error.Type);
        Assert.Empty(context.Agent.Calls);

        var accepted = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "second", MailboxGroupSeeds.Password, 1024),
            CancellationToken.None);

        Assert.True(accepted.IsSuccess);
    }

    /// <summary>An address another mailbox holds is refused as taken, without calling the agent.</summary>
    [Fact]
    public async Task An_address_held_by_a_mailbox_is_refused_as_taken()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);
        await context.SeedMailboxAsync(domain, "info");

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "INFO", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxTaken, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A local part an alias of the same domain holds is refused: one address space per domain.</summary>
    [Fact]
    public async Task An_address_held_by_an_alias_is_refused_as_taken()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);
        await MailboxGroupSeeds.SeedAliasAsync(context, domain, "sales");

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "sales", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxTaken, result.Error!.Code);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>The same local part in another domain of the account is a different address and is allowed.</summary>
    [Fact]
    public async Task The_same_local_part_in_another_domain_is_not_taken()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);
        await context.SeedMailboxAsync(domain, "info");
        var other = await context.SeedDomainAsync(AccountId, "example.org");

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(other.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("info@example.org", result.Value.Address);
    }

    /// <summary>With mail switched off nothing is created and the host is not touched.</summary>
    [Fact]
    public async Task Creating_a_mailbox_while_mail_is_off_is_refused()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailNotEnabled, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A domain that does not exist answers not found and is journaled as a failure.</summary>
    [Fact]
    public async Task Creating_a_mailbox_in_a_missing_domain_is_not_found()
    {
        using var context = new MailTestContext();
        await ArrangeAsync(context, null);

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(Guid.NewGuid(), "info", MailboxGroupSeeds.Password, 512),
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailDomainNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>A domain whose account the directory cannot resolve answers account not found.</summary>
    [Fact]
    public async Task Creating_a_mailbox_for_an_unresolvable_account_is_not_found()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        var domain = await context.SeedDomainAsync(AccountId, "example.com");

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.AccountNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A customer asking for another account's domain gets not found, and the owner's control succeeds.</summary>
    [Fact]
    public async Task A_customer_cannot_create_a_mailbox_in_another_accounts_domain()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(OtherAccountId), databaseName);
        await owner.SeedEnabledServerAsync();
        owner.Accounts.Add(OtherAccountId, "beta");
        var domain = await owner.SeedDomainAsync(OtherAccountId, "beta.example");

        using var intruder = new MailTestContext(new CustomerCurrentUser(AccountId), databaseName);
        intruder.Accounts.Add(AccountId, "acme");
        intruder.Accounts.Add(OtherAccountId, "beta");

        var refused = await MailboxGroupHandlers.Create(intruder).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailDomainNotFound, refused.Error!.Code);
        Assert.Equal(ErrorType.NotFound, refused.Error.Type);
        Assert.Empty(intruder.Agent.Calls);

        var accepted = await MailboxGroupHandlers.Create(owner).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.True(accepted.IsSuccess);
    }

    /// <summary>The agent receives the hash the panel made; the plaintext reaches no sink at all.</summary>
    [Fact]
    public async Task The_agent_receives_the_hash_and_the_password_appears_nowhere_else()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);
        var command = new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512);

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var expectedHash = context.Hasher.Hash(MailboxGroupSeeds.Password);
        Assert.Equal(expectedHash, context.Agent.LastMailbox!.PasswordHash!.Reveal());
        Assert.NotEqual(MailboxGroupSeeds.Password, expectedHash);

        var row = await context.Database.Mailboxes.SingleAsync(CancellationToken.None);
        Assert.Equal(expectedHash, row.PasswordHash);

        var password = MailboxGroupSeeds.Password;
        Assert.DoesNotContain(password, string.Join('\n', context.Agent.Calls), StringComparison.Ordinal);
        Assert.DoesNotContain(password, JsonSerializer.Serialize(context.Entries), StringComparison.Ordinal);
        Assert.DoesNotContain(password, JsonSerializer.Serialize(result.Value), StringComparison.Ordinal);
        Assert.DoesNotContain(password, row.PasswordHash + row.LocalPart, StringComparison.Ordinal);
        Assert.DoesNotContain(password, command.ToString(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", command.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A host that does not verify the panel's hash scheme refuses the mailbox before anything is made.</summary>
    [Fact]
    public async Task A_host_that_cannot_verify_the_hash_scheme_refuses_the_mailbox()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);
        context.Agent.Status = context.Agent.Status with { PasswordScheme = AgentMailPasswordScheme.BlowfishCrypt };

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailPasswordSchemeUnsupported, result.Error!.Code);
        Assert.DoesNotContain(context.Agent.Calls, call =>
        {
            return call.StartsWith("SetMailbox", StringComparison.Ordinal);
        });
        Assert.Empty(context.Database.Mailboxes);
    }

    /// <summary>An agent that refuses the mailbox leaves no row and has nothing to compensate.</summary>
    [Fact]
    public async Task An_agent_failure_leaves_no_row_and_no_compensating_call()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);
        context.Agent.FailNext("SetMailbox", Error.Of(MailboxGroupErrorCodes.AgentUnavailable, ErrorType.Failure));

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.AgentUnavailable, result.Error!.Code);
        Assert.Empty(context.Database.Mailboxes);
        Assert.DoesNotContain(context.Agent.Calls, call =>
        {
            return call.StartsWith("RemoveMailbox", StringComparison.Ordinal);
        });
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>
    /// When another creation took the last slot while this one was on the host, the host's mailbox is removed
    /// again and the refusal says the plan filled up.
    /// </summary>
    [Fact]
    public async Task A_slot_taken_concurrently_removes_the_hosts_mailbox_and_says_so()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);
        context.SlotGate.RefuseNextClaim = true;

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxLimitReachedConcurrently, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(context.Database.Mailboxes);
        Assert.Equal(
            ["GetStatus()", "SetMailbox(acme,info@example.com,quota=512,limit=100,disabled=False,hash=True)",
                "RemoveMailbox(acme,info@example.com,deleteData=True)",],
            context.Agent.Calls);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>A duplicate-key failure is a lost race: taken, and NOT compensated, because the winner owns the mailbox.</summary>
    [Fact]
    public async Task A_unique_violation_answers_taken_and_does_not_remove_the_winners_mailbox()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);
        context.SaveFailure.Next = new DbUpdateException("insert failed", MailboxGroupSeeds.UniqueViolation());

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxTaken, result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.DoesNotContain(context.Agent.Calls, call =>
        {
            return call.StartsWith("RemoveMailbox", StringComparison.Ordinal);
        });
        Assert.Empty(context.Database.Mailboxes);
    }

    /// <summary>Any other write failure is a provisioning failure, and the host's mailbox is removed with its data.</summary>
    [Fact]
    public async Task Any_other_write_failure_removes_the_hosts_mailbox_and_reports_a_provisioning_failure()
    {
        using var context = new MailTestContext();
        var domain = await ArrangeAsync(context, null);
        context.SaveFailure.Next = new DbUpdateException("connection dropped", new InvalidOperationException("dropped"));

        var result = await MailboxGroupHandlers.Create(context).HandleAsync(
            new CreateMailboxCommand(domain.Id, "info", MailboxGroupSeeds.Password, 512), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxProvisioningFailed, result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Contains("RemoveMailbox(acme,info@example.com,deleteData=True)", context.Agent.Calls);
        Assert.Empty(context.Database.Mailboxes);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>Seeds an enabled server, the account and one domain, and returns the domain.</summary>
    /// <param name="context">The context to arrange.</param>
    /// <param name="limits">The account's mail allowances, or <c>null</c> for the defaults.</param>
    /// <returns>The domain <c>example.com</c>, owned by <see cref="AccountId"/>.</returns>
    private static async Task<MailDomain> ArrangeAsync(MailTestContext context, MailLimitsSnapshot? limits)
    {
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme", limits);

        return await context.SeedDomainAsync(AccountId, "example.com");
    }
}
