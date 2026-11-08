using Maran.Modules.Mail.Commands.CreateMailDomain;
using Maran.Modules.Mail.Common;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Mail.Tests.Commands.CreateMailDomain;

/// <summary>
/// Creating a mail domain: site ownership, server-wide uniqueness, the key that is stored only as
/// ciphertext, and the host being cleaned up when the row cannot be written.
/// </summary>
public sealed class CreateMailDomainCommandHandlerTests
{
    /// <summary>The account that owns the site in these tests.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>Another account, which must never be able to take or see the first one's domain.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>A domain on a site of the account is created, reaches the host once and is journaled.</summary>
    [Fact]
    public async Task A_domain_on_the_accounts_own_site_is_created_provisioned_and_journaled()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        var siteId = await Prepare(context);

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "Example.COM", "10.0.0.1", "agent/1"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("example.com", result.Value.Name);
        Assert.Equal("m20260909-1", result.Value.DkimSelector);
        Assert.Equal(MailDomainStatus.Active, result.Value.Status);
        Assert.Equal("MailDomainStatusActive", result.Value.StatusName);
        Assert.Equal("acme", result.Value.OwnerName);
        Assert.Equal(0, result.Value.MailboxCount);
        Assert.Equal(["ConfigureDomain(acme,example.com,m20260909-1)"], context.Agent.Calls);

        var stored = await context.Database.MailDomains.Include(domain => domain.DkimKeys).SingleAsync();
        Assert.Equal(AccountId, stored.AccountId);
        Assert.Equal(siteId, stored.SiteId);
        var key = Assert.Single(stored.DkimKeys);
        Assert.Equal(DkimKeyState.Active, key.State);

        var entry = Assert.Single(context.Entries);
        Assert.Equal("MailDomainCreated", entry.Action);
        Assert.Equal("example.com", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal("10.0.0.1", entry.IpAddress);
    }

    /// <summary>The private key is stored only as ciphertext and appears in no DTO, audit entry or call string.</summary>
    [Fact]
    public async Task The_private_key_is_stored_only_as_ciphertext_and_never_surfaces()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await Prepare(context);

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        const string Pem = "-----BEGIN PRIVATE KEY-----\nfake-1\n-----END PRIVATE KEY-----\n";
        var stored = await context.Database.DkimKeys.SingleAsync();
        Assert.Equal(ReversibleEncryption.Marker + Pem, stored.PrivateKeyEncrypted);
        Assert.NotEqual(Pem, stored.PrivateKeyEncrypted);

        Assert.DoesNotContain("fake-1", result.Value.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(
            typeof(MailDomainDto).GetProperties(),
            property =>
            {
                return property.Name.Contains("Private", StringComparison.OrdinalIgnoreCase);
            });
        Assert.All(
            context.Entries,
            entry =>
            {
                Assert.DoesNotContain("fake-1", entry.Subject, StringComparison.Ordinal);
            });
        Assert.All(
            context.Agent.Calls,
            call =>
            {
                Assert.DoesNotContain("fake-1", call, StringComparison.Ordinal);
            });
        Assert.NotNull(context.Agent.LastDomain);
        Assert.Equal(Pem, context.Agent.LastDomain!.DkimPrivateKeyPem.Reveal());
    }

    /// <summary>A server with mail switched off refuses before the agent is touched.</summary>
    [Fact]
    public async Task A_server_with_mail_off_refuses_the_domain_before_the_agent_is_called()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountId, "acme");
        context.Sites.Serve("example.com", AccountId);

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("MailNotEnabled", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>An account the caller cannot see reads as not found.</summary>
    [Fact]
    public async Task An_account_the_directory_does_not_show_is_not_found()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await context.SeedEnabledServerAsync();
        context.Sites.Serve("example.com", AccountId);

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A name that is no site's domain is refused as not an owned site.</summary>
    [Fact]
    public async Task A_name_that_is_no_sites_domain_is_refused()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.Equal("MailDomainNotOwnedSite", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A domain is refused when the site that serves it belongs to another account.</summary>
    [Fact]
    public async Task A_domain_is_refused_when_the_site_belongs_to_another_account()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");
        context.Sites.Serve("example.com", OtherAccountId);

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.Equal("MailDomainNotOwnedSite", result.Error!.Code);
        Assert.Empty(context.Agent.Calls);
        Assert.Empty(await context.Database.MailDomains.ToListAsync());
    }

    /// <summary>A domain another account already holds is refused, though the caller cannot see that row.</summary>
    [Fact]
    public async Task A_domain_another_account_holds_is_refused_even_though_the_caller_cannot_see_it()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var neighbour = new MailTestContext(new CustomerCurrentUser(OtherAccountId), databaseName);
        await neighbour.SeedDomainAsync(OtherAccountId, "example.com");

        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), databaseName);
        await Prepare(context);
        Assert.Empty(await context.Database.MailDomains.ToListAsync());

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.Equal("MailDomainTaken", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>The host already holding the name reads as a taken domain, not as an agent fault.</summary>
    [Fact]
    public async Task The_host_reporting_an_existing_domain_reads_as_taken()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await Prepare(context);
        context.Agent.FailNext("ConfigureDomain", Error.Of("AgentAlreadyExists", ErrorType.Conflict));

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.Equal("MailDomainTaken", result.Error!.Code);
        Assert.Empty(await context.Database.MailDomains.ToListAsync());
    }

    /// <summary>Any other agent failure passes through untouched and writes no row.</summary>
    [Fact]
    public async Task Another_agent_failure_passes_through_and_writes_no_row()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await Prepare(context);
        context.Agent.FailNext("ConfigureDomain", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.Equal("AgentUnavailable", result.Error!.Code);
        Assert.Empty(await context.Database.MailDomains.ToListAsync());
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>A row that cannot be written after the host succeeded removes the host domain again.</summary>
    [Fact]
    public async Task A_row_that_cannot_be_written_removes_the_domain_from_the_host()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await Prepare(context);
        context.SaveFailure.Next = new DbUpdateException("save failed", new InvalidOperationException("db down"));

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.Equal("MailDomainProvisioningFailed", result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal(
            ["ConfigureDomain(acme,example.com,m20260909-1)", "RemoveDomain(acme,example.com)"],
            context.Agent.Calls);
        Assert.False(Assert.Single(context.Entries).Succeeded);
    }

    /// <summary>Losing the unique-index race is a taken domain and does NOT remove the winner's host state.</summary>
    [Fact]
    public async Task Losing_the_unique_index_race_is_taken_and_does_not_compensate()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await Prepare(context);
        context.SaveFailure.Next = new DbUpdateException(
            "save failed",
            new PostgresException(
                "duplicate key value violates unique constraint \"IX_MailDomains_Name\"",
                "ERROR",
                "ERROR",
                PostgresErrorCodes.UniqueViolation));

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.Equal("MailDomainTaken", result.Error!.Code);
        Assert.Equal(["ConfigureDomain(acme,example.com,m20260909-1)"], context.Agent.Calls);
    }

    /// <summary>A suspended account's new domain reads as suspended.</summary>
    [Fact]
    public async Task A_suspended_accounts_domain_reads_as_suspended()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountId), Guid.NewGuid().ToString());
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme", state: AccountState.Suspended);
        context.Sites.Serve("example.com", AccountId);

        var result = await DomainGroupHandlers.Create(context).HandleAsync(
            new CreateMailDomainCommand(AccountId, "example.com"), CancellationToken.None);

        Assert.Equal(MailDomainStatus.Suspended, result.Value.Status);
    }

    /// <summary>Seeds an enabled server, the account and a site serving example.com for it.</summary>
    /// <param name="context">The context to seed.</param>
    /// <returns>The identifier of the site serving the domain.</returns>
    private static async Task<Guid> Prepare(MailTestContext context)
    {
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");

        return context.Sites.Serve("example.com", AccountId);
    }
}
