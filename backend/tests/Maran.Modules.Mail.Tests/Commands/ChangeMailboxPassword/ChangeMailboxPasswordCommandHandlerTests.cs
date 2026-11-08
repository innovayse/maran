using System.Text.Json;
using Maran.Agent.Client.Services.MailService;
using Maran.Modules.Mail.Commands.ChangeMailboxPassword;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.ChangeMailboxPassword;

/// <summary>
/// Changing a mailbox's password: the hash is what the agent and the row receive, the limits do not move,
/// the old hash is put back when the row refuses, and the plaintext reaches no sink.
/// </summary>
public sealed class ChangeMailboxPasswordCommandHandlerTests
{
    /// <summary>The account that owns the mailbox in these tests.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose mailbox a customer of <see cref="AccountId"/> must not reach.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The new hash reaches the agent with the limits unchanged, then the row, and the change is journaled.</summary>
    [Fact]
    public async Task Changing_the_password_installs_the_hash_then_records_it_and_journals_it()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        var oldHash = mailbox.PasswordHash;

        var result = await MailboxGroupHandlers.ChangePassword(context).HandleAsync(
            new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password, mailbox.Id, "203.0.113.7", "agent/1"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var expectedHash = context.Hasher.Hash(MailboxGroupSeeds.Password);
        Assert.Equal(expectedHash, context.Agent.LastMailbox!.PasswordHash!.Reveal());
        Assert.Equal(
            ["GetStatus()", "SetMailbox(acme,info@example.com,quota=1024,limit=100,disabled=False,hash=True)"],
            context.Agent.Calls);

        var stored = await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.Equal(expectedHash, stored.PasswordHash);
        Assert.NotEqual(oldHash, stored.PasswordHash);
        Assert.Equal(1024, stored.QuotaMib);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailboxPasswordChanged, entry.Action);
        Assert.Equal("info@example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A switched-off mailbox stays switched off: the credential changes, the switch does not.</summary>
    [Fact]
    public async Task Changing_the_password_of_a_disabled_mailbox_keeps_it_disabled()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        mailbox.Disable();
        await context.Database.SaveChangesAsync(CancellationToken.None);

        var result = await MailboxGroupHandlers.ChangePassword(context).HandleAsync(
            new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password, mailbox.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(context.Agent.LastMailbox!.Disabled);
        Assert.False((await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None)).Enabled);
    }

    /// <summary>The plaintext password appears in no call, no audit entry and no stored value.</summary>
    [Fact]
    public async Task The_plaintext_password_appears_in_no_call_entry_or_row()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        var command = new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password, mailbox.Id);

        await MailboxGroupHandlers.ChangePassword(context).HandleAsync(command, CancellationToken.None);

        var password = MailboxGroupSeeds.Password;
        var stored = await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None);
        Assert.DoesNotContain(password, string.Join('\n', context.Agent.Calls), StringComparison.Ordinal);
        Assert.DoesNotContain(password, JsonSerializer.Serialize(context.Entries), StringComparison.Ordinal);
        Assert.DoesNotContain(password, stored.PasswordHash + stored.LocalPart, StringComparison.Ordinal);
        Assert.DoesNotContain(password, command.ToString(), StringComparison.Ordinal);
        Assert.Contains("[redacted]", command.ToString(), StringComparison.Ordinal);
    }

    /// <summary>A host that does not verify the panel's hash scheme refuses the change and the row keeps its hash.</summary>
    [Fact]
    public async Task A_host_that_cannot_verify_the_hash_scheme_refuses_the_change()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        var oldHash = mailbox.PasswordHash;
        context.Agent.Status = context.Agent.Status with { PasswordScheme = AgentMailPasswordScheme.BlowfishCrypt };

        var result = await MailboxGroupHandlers.ChangePassword(context).HandleAsync(
            new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password, mailbox.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailPasswordSchemeUnsupported, result.Error!.Code);
        Assert.DoesNotContain(context.Agent.Calls, call =>
        {
            return call.StartsWith("SetMailbox", StringComparison.Ordinal);
        });
        Assert.Equal(oldHash, (await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None)).PasswordHash);
    }

    /// <summary>An agent that refuses the hash leaves the row's hash — and the customer's working password — alone.</summary>
    [Fact]
    public async Task An_agent_failure_leaves_the_stored_hash_unchanged()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        var oldHash = mailbox.PasswordHash;
        context.Agent.FailNext("SetMailbox", Error.Of(MailboxGroupErrorCodes.AgentUnavailable, ErrorType.Failure));

        var result = await MailboxGroupHandlers.ChangePassword(context).HandleAsync(
            new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password, mailbox.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.AgentUnavailable, result.Error!.Code);
        Assert.Equal(oldHash, (await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None)).PasswordHash);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>A row write that fails after the host accepted the hash sends the OLD hash back.</summary>
    [Fact]
    public async Task A_row_write_failure_puts_the_previous_hash_back_on_the_host()
    {
        using var context = new MailTestContext();
        var mailbox = await ArrangeAsync(context);
        var oldHash = mailbox.PasswordHash;
        context.SaveFailure.Next = new DbUpdateException("connection dropped", new InvalidOperationException("dropped"));

        var result = await MailboxGroupHandlers.ChangePassword(context).HandleAsync(
            new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password, mailbox.Id), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxProvisioningFailed, result.Error!.Code);
        Assert.Equal(3, context.Agent.Calls.Count);
        Assert.Equal(oldHash, context.Agent.LastMailbox!.PasswordHash!.Reveal());
        Assert.Equal(oldHash, (await context.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None)).PasswordHash);
        Assert.Equal(oldHash, mailbox.PasswordHash);
    }

    /// <summary>An unknown mailbox answers not found without touching the host.</summary>
    [Fact]
    public async Task Changing_the_password_of_a_missing_mailbox_is_not_found()
    {
        using var context = new MailTestContext();
        await ArrangeAsync(context);

        var result = await MailboxGroupHandlers.ChangePassword(context).HandleAsync(
            new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password, Guid.NewGuid()), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A customer changing another account's mailbox password gets not found, and the owner's control succeeds.</summary>
    [Fact]
    public async Task A_customer_cannot_change_another_accounts_mailbox_password()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(OtherAccountId), databaseName);
        await owner.SeedEnabledServerAsync();
        owner.Accounts.Add(OtherAccountId, "beta");
        var domain = await owner.SeedDomainAsync(OtherAccountId, "beta.example");
        var mailbox = await owner.SeedMailboxAsync(domain, "info");
        var oldHash = mailbox.PasswordHash;

        using var intruder = new MailTestContext(new CustomerCurrentUser(AccountId), databaseName);
        intruder.Accounts.Add(AccountId, "acme");
        intruder.Accounts.Add(OtherAccountId, "beta");

        var refused = await MailboxGroupHandlers.ChangePassword(intruder).HandleAsync(
            new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password, mailbox.Id), CancellationToken.None);

        Assert.False(refused.IsSuccess);
        Assert.Equal(MailboxGroupErrorCodes.MailboxNotFound, refused.Error!.Code);
        Assert.Empty(intruder.Agent.Calls);
        Assert.Equal(oldHash, (await owner.Database.Mailboxes.AsNoTracking().SingleAsync(CancellationToken.None)).PasswordHash);

        var accepted = await MailboxGroupHandlers.ChangePassword(owner).HandleAsync(
            new ChangeMailboxPasswordCommand(MailboxGroupSeeds.Password, mailbox.Id), CancellationToken.None);

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
