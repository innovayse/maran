using Maran.Modules.Mail.Commands.DeleteMailAlias;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.DeleteMailAlias;

/// <summary>
/// Deleting an address: the full set the agent is sent without it, the rows that go, and the previous set
/// restored when they cannot.
/// </summary>
public sealed class DeleteMailAliasCommandHandlerTests
{
    /// <summary>The account every test in this file works for.</summary>
    private static readonly Guid AccountA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose addresses must stay out of reach.</summary>
    private static readonly Guid AccountB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The caller's address in the audit journal.</summary>
    private const string CallerIp = "203.0.113.9";

    /// <summary>The caller's user agent in the audit journal.</summary>
    private const string CallerAgent = "test-agent";

    /// <summary>The address and its target rows go, the agent is sent the rest, and the journal says so.</summary>
    [Fact]
    public async Task Deleting_removes_the_rows_sends_the_rest_of_the_set_and_journals_it()
    {
        var (context, domain) = await ReadyAsync();
        using var owned = context;
        var other = await context.SeedDomainAsync(AccountA, "example.net");
        var alias = await MailAliasSeeds.SeedAliasAsync(
            context, domain, "info", MailAliasKind.Alias, "bob@example.com", "carol@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "sales", MailAliasKind.Alias, "x@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "*", MailAliasKind.CatchAll, "boss@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, other, "elsewhere", MailAliasKind.Alias, "z@example.net");

        var result = await MailAliasHandlers.Delete(context).HandleAsync(Command(alias), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["*", "elsewhere", "sales"], await MailAliasProbe.StoredLocalPartsAsync(context));

        // The deleted address took its own target rows and no one else's.
        Assert.Equal(3, await context.Database.MailAliasTargets.CountAsync());

        Assert.Equal(["SetAliases(acme,example.com,aliases=1,catchAll=True)"], context.Agent.Calls);
        Assert.Equal(["sales"], MailAliasProbe.LocalParts(context.Agent.LastAliases!));
        Assert.Equal(["boss@example.com"], context.Agent.LastCatchAll);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailAliasDeleted, entry.Action);
        Assert.Equal("info@example.com", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(CallerIp, entry.IpAddress);
        Assert.Equal(CallerAgent, entry.UserAgent);
    }

    /// <summary>Deleting the last address sends an empty set and no catch-all, which removes everything the domain had.</summary>
    [Fact]
    public async Task Deleting_the_last_address_sends_an_empty_set()
    {
        var (context, domain) = await ReadyAsync();
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");

        var result = await MailAliasHandlers.Delete(context).HandleAsync(Command(alias), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(context.Agent.LastAliases!);
        Assert.Null(context.Agent.LastCatchAll);
        Assert.Empty(await MailAliasProbe.StoredLocalPartsAsync(context));
        Assert.Equal(0, await context.Database.MailAliasTargets.CountAsync());
    }

    /// <summary>Deleting the catch-all sends no catch-all, keeps the aliases, and is journalled as the catch-all.</summary>
    [Fact]
    public async Task Deleting_the_catch_all_sends_no_catch_all_and_keeps_the_aliases()
    {
        var (context, domain) = await ReadyAsync();
        using var owned = context;
        var catchAll = await MailAliasSeeds.SeedAliasAsync(
            context, domain, "*", MailAliasKind.CatchAll, "boss@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "sales", MailAliasKind.Alias, "x@example.com");

        var result = await MailAliasHandlers.Delete(context).HandleAsync(Command(catchAll), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(context.Agent.LastCatchAll);
        Assert.Equal(["sales"], MailAliasProbe.LocalParts(context.Agent.LastAliases!));
        Assert.Equal(["sales"], await MailAliasProbe.StoredLocalPartsAsync(context));

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailCatchAllCleared, entry.Action);
        Assert.Equal("*@example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>An address that does not exist is not found, journalled with the identifier probed.</summary>
    [Fact]
    public async Task An_address_that_does_not_exist_is_not_found()
    {
        var (context, _) = await ReadyAsync();
        using var owned = context;
        var missing = Guid.NewGuid();

        var result = await MailAliasHandlers.Delete(context).HandleAsync(
            new DeleteMailAliasCommand(missing, CallerIp, CallerAgent), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasNotFound, ErrorType.NotFound);
        Assert.Empty(context.Agent.Calls);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailAliasDeleted, entry.Action);
        Assert.Equal(missing.ToString(), entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Another account's address answers exactly as a missing one does and survives; the owner's own is deleted.</summary>
    /// <remarks>
    /// The zero agent calls and the surviving row are the destructive half: a cross-account delete that
    /// answered 404 after removing the address would satisfy a status-only assertion.
    /// </remarks>
    [Fact]
    public async Task Another_accounts_address_is_not_found_rather_than_forbidden_and_survives()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(AccountB), databaseName);
        owner.Accounts.Add(AccountB, "beta");
        await MailAliasSeeds.SeedServerAsync(owner, allowExternalForwarding: false);
        var domain = await owner.SeedDomainAsync(AccountB, "beta.example");
        var victim = await MailAliasSeeds.SeedAliasAsync(
            owner, domain, "info", MailAliasKind.Alias, "bob@beta.example");

        using var attacker = new MailTestContext(new CustomerCurrentUser(AccountA), databaseName);
        attacker.Accounts.Add(AccountA, "acme");
        attacker.Accounts.Add(AccountB, "beta");

        var refused = await MailAliasHandlers.Delete(attacker).HandleAsync(Command(victim), CancellationToken.None);

        AssertRefused(refused.Error, MailAliasErrorCodes.MailAliasNotFound, ErrorType.NotFound);
        Assert.Empty(attacker.Agent.Calls);
        Assert.Equal(["info"], await MailAliasProbe.StoredLocalPartsAsync(owner));

        var own = await MailAliasHandlers.Delete(owner).HandleAsync(Command(victim), CancellationToken.None);

        Assert.True(own.IsSuccess);
        Assert.Empty(await MailAliasProbe.StoredLocalPartsAsync(owner));
    }

    /// <summary>An address whose account the directory cannot resolve is refused before the agent.</summary>
    [Fact]
    public async Task An_address_whose_account_cannot_be_resolved_is_refused_before_the_agent()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountA), Guid.NewGuid().ToString());
        var domain = await context.SeedDomainAsync(AccountA, "example.com");
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");

        var result = await MailAliasHandlers.Delete(context).HandleAsync(Command(alias), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.AccountNotFound, ErrorType.NotFound);
        Assert.Empty(context.Agent.Calls);
        Assert.Equal(["info"], await MailAliasProbe.StoredLocalPartsAsync(context));
    }

    /// <summary>The agent's refusal is returned as it came and the rows stay.</summary>
    [Fact]
    public async Task An_agent_failure_is_returned_unchanged_and_the_rows_stay()
    {
        var (context, domain) = await ReadyAsync();
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");
        context.Agent.FailNext("SetAliases", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await MailAliasHandlers.Delete(context).HandleAsync(Command(alias), CancellationToken.None);

        AssertRefused(result.Error, "AgentUnavailable", ErrorType.Failure);
        Assert.Equal(["info"], await MailAliasProbe.StoredLocalPartsAsync(context));
        Assert.Equal(1, await context.Database.MailAliasTargets.CountAsync());

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailAliasDeleted, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A refused catch-all deletion is journalled as the catch-all being cleared, as a failure.</summary>
    [Fact]
    public async Task A_refused_catch_all_deletion_is_journalled_as_cleared_and_failed()
    {
        var (context, domain) = await ReadyAsync();
        using var owned = context;
        var catchAll = await MailAliasSeeds.SeedAliasAsync(
            context, domain, "*", MailAliasKind.CatchAll, "boss@example.com");
        context.Agent.FailNext("SetAliases", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await MailAliasHandlers.Delete(context).HandleAsync(Command(catchAll), CancellationToken.None);

        AssertRefused(result.Error, "AgentUnavailable", ErrorType.Failure);
        Assert.Equal(["*"], await MailAliasProbe.StoredLocalPartsAsync(context));

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailCatchAllCleared, entry.Action);
        Assert.Equal("*@example.com", entry.Subject);
        Assert.False(entry.Succeeded);
    }

    /// <summary>Rows that cannot be deleted put the address back on the host.</summary>
    [Fact]
    public async Task Rows_that_cannot_be_deleted_restore_the_previous_set_on_the_host()
    {
        var (context, domain) = await ReadyAsync();
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "sales", MailAliasKind.Alias, "x@example.com");
        context.SaveFailure.Next = new DbUpdateException("delete failed", new InvalidOperationException("db down"));

        var result = await MailAliasHandlers.Delete(context).HandleAsync(Command(alias), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasProvisioningFailed, ErrorType.Failure);
        Assert.Equal(2, MailAliasProbe.SetAliasesCalls(context));

        // The LAST call is the restore: the set as it was, with the address that failed to go.
        Assert.Equal(["info", "sales"], MailAliasProbe.LocalParts(context.Agent.LastAliases!));
        Assert.Equal(["info", "sales"], await MailAliasProbe.StoredLocalPartsAsync(context));

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailAliasDeleted, entry.Action);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A server with mail switched off still lets the customer clear what they set up.</summary>
    [Fact]
    public async Task A_server_with_mail_off_still_deletes()
    {
        var (context, domain) = await ReadyAsync();
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");
        var settings = await context.Database.MailServerSettings.SingleAsync();
        settings.Disable(context.Clock.UtcNow);
        await context.Database.SaveChangesAsync();

        var result = await MailAliasHandlers.Delete(context).HandleAsync(Command(alias), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(await MailAliasProbe.StoredLocalPartsAsync(context));
    }

    /// <summary>Builds a customer context with mail on, the account known and one domain, <c>example.com</c>.</summary>
    /// <returns>The context and the domain.</returns>
    private static async Task<(MailTestContext Context, MailDomain Domain)> ReadyAsync()
    {
        var context = new MailTestContext(new CustomerCurrentUser(AccountA), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountA, "acme");
        await MailAliasSeeds.SeedServerAsync(context, allowExternalForwarding: false);
        var domain = await context.SeedDomainAsync(AccountA, "example.com");

        return (context, domain);
    }

    /// <summary>Builds a deletion command with the caller's address and agent stamped.</summary>
    /// <param name="alias">The address to delete.</param>
    /// <returns>The command.</returns>
    private static DeleteMailAliasCommand Command(MailAlias alias)
    {
        return new DeleteMailAliasCommand(alias.Id, CallerIp, CallerAgent);
    }

    /// <summary>Asserts a failure carries the code and kind expected.</summary>
    /// <param name="error">The failure.</param>
    /// <param name="code">The expected code.</param>
    /// <param name="type">The expected kind.</param>
    private static void AssertRefused(Error? error, string code, ErrorType type)
    {
        Assert.NotNull(error);
        Assert.Equal(code, error.Code);
        Assert.Equal(type, error.Type);
    }
}
