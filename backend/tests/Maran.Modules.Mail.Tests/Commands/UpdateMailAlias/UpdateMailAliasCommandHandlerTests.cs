using Maran.Modules.Mail.Commands.UpdateMailAlias;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Mail.Tests.Commands.UpdateMailAlias;

/// <summary>
/// Changing where an address delivers: the same judgement as a creation, the full set the agent is sent,
/// the rows rewritten, and the previous set restored when they cannot be.
/// </summary>
public sealed class UpdateMailAliasCommandHandlerTests
{
    /// <summary>The account every test in this file works for.</summary>
    private static readonly Guid AccountA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose addresses must stay out of reach.</summary>
    private static readonly Guid AccountB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The caller's address in the audit journal.</summary>
    private const string CallerIp = "203.0.113.9";

    /// <summary>The caller's user agent in the audit journal.</summary>
    private const string CallerAgent = "test-agent";

    /// <summary>The new targets replace the old rows exactly, and the agent and the journal are told.</summary>
    [Fact]
    public async Task Updating_replaces_the_targets_calls_the_agent_and_journals_it()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(
            context, domain, "info", MailAliasKind.Alias, "bob@example.com", "carol@example.com");

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(alias, "dave@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["dave@example.com"], result.Value.Targets);
        Assert.Equal("info@example.com", result.Value.Address);

        var row = await context.Database.MailAliases.Include(candidate => candidate.Targets).SingleAsync();
        Assert.Equal(["dave@example.com"], MailAliasProbe.AddressesOf(row));
        Assert.Equal(1, await context.Database.MailAliasTargets.CountAsync());

        Assert.Equal(["SetAliases(acme,example.com,aliases=1,catchAll=False)"], context.Agent.Calls);
        Assert.Equal(["dave@example.com"], MailAliasProbe.TargetsOf(context.Agent.LastAliases!, "info"));

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailAliasUpdated, entry.Action);
        Assert.Equal("info@example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>The new list may be longer, and the order the caller gave is the order stored.</summary>
    [Fact]
    public async Task A_longer_list_is_stored_in_the_order_given()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(alias, "z@example.com", "a@example.com", "m@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = await context.Database.MailAliases.Include(candidate => candidate.Targets).SingleAsync();
        Assert.Equal(["z@example.com", "a@example.com", "m@example.com"], MailAliasProbe.AddressesOf(row));
        Assert.Equal(3, await context.Database.MailAliasTargets.CountAsync());
    }

    /// <summary>An alias that gains an outside target becomes a forwarder, and one that loses it becomes an alias again.</summary>
    [Fact]
    public async Task The_kind_follows_the_new_targets()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: true);
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");
        var handler = MailAliasHandlers.Update(context);

        var toForwarder = await handler.HandleAsync(Command(alias, "me@example.org"), CancellationToken.None);
        var storedForwarder = (await context.Database.MailAliases.SingleAsync()).Kind;
        var backToAlias = await handler.HandleAsync(Command(alias, "bob@example.com"), CancellationToken.None);

        Assert.Equal(MailAliasKind.Forwarder, toForwarder.Value.Kind);
        Assert.Equal(MailAliasKind.Forwarder, storedForwarder);
        Assert.Equal(MailAliasKind.Alias, backToAlias.Value.Kind);
        Assert.Equal(MailAliasKind.Alias, (await context.Database.MailAliases.SingleAsync()).Kind);
    }

    /// <summary>An outside target is refused while the host's switch is off, and the rows are untouched.</summary>
    /// <remarks>The inverse control is the test above: with the switch on, the same change is accepted.</remarks>
    [Fact]
    public async Task An_outside_target_is_refused_when_the_host_forbids_external_forwarding()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(alias, "me@example.org"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasExternalTargetNotAllowed, ErrorType.Validation);
        Assert.Empty(context.Agent.Calls);
        var row = await context.Database.MailAliases.Include(candidate => candidate.Targets).SingleAsync();
        Assert.Equal(["bob@example.com"], MailAliasProbe.AddressesOf(row));
        AssertFailureJournalled(context, HostedMailAuditActions.MailAliasUpdated, "info@example.com");
    }

    /// <summary>A catch-all stays a catch-all, is sent as the catch-all, and is journalled as one.</summary>
    [Fact]
    public async Task A_catch_all_stays_a_catch_all_and_is_sent_as_the_catch_all()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var catchAll = await MailAliasSeeds.SeedAliasAsync(
            context, domain, "*", MailAliasKind.CatchAll, "bob@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "sales", MailAliasKind.Alias, "x@example.com");

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(catchAll, "carol@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MailAliasKind.CatchAll, result.Value.Kind);
        Assert.Equal("*@example.com", result.Value.Address);
        Assert.Equal(["carol@example.com"], context.Agent.LastCatchAll);
        Assert.Equal(["sales"], MailAliasProbe.LocalParts(context.Agent.LastAliases!));

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailCatchAllSet, entry.Action);
        Assert.Equal("*@example.com", entry.Subject);
    }

    /// <summary>The agent is sent the other aliases and the catch-all of the domain, and nothing of other domains.</summary>
    [Fact]
    public async Task The_agent_is_sent_the_complete_set_of_the_domain_and_no_other_domains()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var other = await context.SeedDomainAsync(AccountA, "example.net");
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "sales", MailAliasKind.Alias, "x@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "*", MailAliasKind.CatchAll, "boss@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, other, "elsewhere", MailAliasKind.Alias, "z@example.net");

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(alias, "carol@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = context.Agent.LastAliases!;
        Assert.Equal(["info", "sales"], MailAliasProbe.LocalParts(sent));
        Assert.Equal(["carol@example.com"], MailAliasProbe.TargetsOf(sent, "info"));
        Assert.Equal(["x@example.com"], MailAliasProbe.TargetsOf(sent, "sales"));
        Assert.Equal(["boss@example.com"], context.Agent.LastCatchAll);
        Assert.DoesNotContain("elsewhere", MailAliasProbe.LocalParts(sent));
    }

    /// <summary>An address that does not exist is not found, journalled with the identifier probed.</summary>
    [Fact]
    public async Task An_address_that_does_not_exist_is_not_found()
    {
        var (context, _) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var missing = Guid.NewGuid();

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            new UpdateMailAliasCommand(["bob@example.com"], missing, CallerIp, CallerAgent), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasNotFound, ErrorType.NotFound);
        Assert.Empty(context.Agent.Calls);
        AssertFailureJournalled(context, HostedMailAuditActions.MailAliasUpdated, missing.ToString());
    }

    /// <summary>Another account's address answers exactly as a missing one does and is left untouched; the owner's own is found.</summary>
    [Fact]
    public async Task Another_accounts_address_is_not_found_rather_than_forbidden()
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

        var refused = await MailAliasHandlers.Update(attacker).HandleAsync(
            Command(victim, "evil@beta.example"), CancellationToken.None);
        var own = await MailAliasHandlers.Update(owner).HandleAsync(
            Command(victim, "carol@beta.example"), CancellationToken.None);

        AssertRefused(refused.Error, MailAliasErrorCodes.MailAliasNotFound, ErrorType.NotFound);
        Assert.Empty(attacker.Agent.Calls);
        Assert.True(own.IsSuccess);
        var row = await owner.Database.MailAliases.Include(candidate => candidate.Targets).SingleAsync();
        Assert.Equal(["carol@beta.example"], MailAliasProbe.AddressesOf(row));
    }

    /// <summary>Mail that is off refuses the change.</summary>
    [Fact]
    public async Task A_server_with_mail_off_refuses_the_change()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");
        var settings = await context.Database.MailServerSettings.SingleAsync();
        settings.Disable(context.Clock.UtcNow);
        await context.Database.SaveChangesAsync();

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(alias, "carol@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailNotEnabled, ErrorType.Conflict);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>An address whose account the directory cannot resolve is refused before the agent.</summary>
    [Fact]
    public async Task An_address_whose_account_cannot_be_resolved_is_refused_before_the_agent()
    {
        using var context = new MailTestContext(new CustomerCurrentUser(AccountA), Guid.NewGuid().ToString());
        await MailAliasSeeds.SeedServerAsync(context, allowExternalForwarding: false);
        var domain = await context.SeedDomainAsync(AccountA, "example.com");
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(alias, "carol@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.AccountNotFound, ErrorType.NotFound);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A list the policy refuses is refused by the handler too.</summary>
    [Fact]
    public async Task A_target_list_the_policy_refuses_is_refused_with_its_code()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(alias, "no-at-sign"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasTargetInvalid, ErrorType.Validation);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>The agent's refusal is returned as it came and the rows keep their old targets.</summary>
    [Fact]
    public async Task An_agent_failure_is_returned_unchanged_and_leaves_the_rows_alone()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");
        context.Agent.FailNext("SetAliases", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(alias, "carol@example.com"), CancellationToken.None);

        AssertRefused(result.Error, "AgentUnavailable", ErrorType.Failure);
        var row = await context.Database.MailAliases.Include(candidate => candidate.Targets).SingleAsync();
        Assert.Equal(["bob@example.com"], MailAliasProbe.AddressesOf(row));
        AssertFailureJournalled(context, HostedMailAuditActions.MailAliasUpdated, "info@example.com");
    }

    /// <summary>Rows that cannot be rewritten put the previous targets back on the host.</summary>
    [Fact]
    public async Task Rows_that_cannot_be_rewritten_restore_the_previous_set_on_the_host()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var alias = await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "sales", MailAliasKind.Alias, "x@example.com");
        context.SaveFailure.Next = new DbUpdateException("update failed", new InvalidOperationException("db down"));

        var result = await MailAliasHandlers.Update(context).HandleAsync(
            Command(alias, "carol@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasProvisioningFailed, ErrorType.Failure);
        Assert.Equal(2, MailAliasProbe.SetAliasesCalls(context));

        // The LAST call is the restore: the set as it was, with the old targets.
        var restored = context.Agent.LastAliases!;
        Assert.Equal(["bob@example.com"], MailAliasProbe.TargetsOf(restored, "info"));
        Assert.Equal(["x@example.com"], MailAliasProbe.TargetsOf(restored, "sales"));

        var row = await context.Database.MailAliases.Include(candidate => candidate.Targets)
            .SingleAsync(candidate => candidate.LocalPart == "info");
        Assert.Equal(["bob@example.com"], MailAliasProbe.AddressesOf(row));
        AssertFailureJournalled(context, HostedMailAuditActions.MailAliasUpdated, "info@example.com");
    }

    /// <summary>Builds a customer context with mail on, the account known and one domain, <c>example.com</c>.</summary>
    /// <param name="allowExternalForwarding">Whether the host lets a forwarder leave the account's domains.</param>
    /// <returns>The context and the domain.</returns>
    private static async Task<(MailTestContext Context, MailDomain Domain)> ReadyAsync(bool allowExternalForwarding)
    {
        var context = new MailTestContext(new CustomerCurrentUser(AccountA), Guid.NewGuid().ToString());
        context.Accounts.Add(AccountA, "acme");
        await MailAliasSeeds.SeedServerAsync(context, allowExternalForwarding);
        var domain = await context.SeedDomainAsync(AccountA, "example.com");

        return (context, domain);
    }

    /// <summary>Builds an update command with the caller's address and agent stamped.</summary>
    /// <param name="alias">The address to change.</param>
    /// <param name="targets">The new targets.</param>
    /// <returns>The command.</returns>
    private static UpdateMailAliasCommand Command(MailAlias alias, params string[] targets)
    {
        return new UpdateMailAliasCommand(targets, alias.Id, CallerIp, CallerAgent);
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

    /// <summary>Asserts exactly one journal entry, a failure, for the action and subject.</summary>
    /// <param name="context">The assembled module.</param>
    /// <param name="action">The expected action.</param>
    /// <param name="subject">The expected subject.</param>
    private static void AssertFailureJournalled(MailTestContext context, string action, string subject)
    {
        var entry = Assert.Single(context.Entries);
        Assert.Equal(action, entry.Action);
        Assert.Equal(subject, entry.Subject);
        Assert.False(entry.Succeeded);
        Assert.Equal(CallerIp, entry.IpAddress);
    }
}
