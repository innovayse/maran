using Maran.Modules.Mail.Commands.CreateMailAlias;
using Maran.Modules.Mail.Domain.Entities;
using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Mail.Tests.Commands.CreateMailAlias;

/// <summary>
/// Creating an alias, a forwarder or a catch-all: what each address becomes, every typed refusal, the full
/// set the agent is sent, and the compensation when the row cannot be written.
/// </summary>
public sealed class CreateMailAliasCommandHandlerTests
{
    /// <summary>The account every test in this file creates addresses for.</summary>
    private static readonly Guid AccountA = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>A different account, whose domains and addresses must stay out of reach.</summary>
    private static readonly Guid AccountB = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The caller's address in the audit journal.</summary>
    private const string CallerIp = "203.0.113.9";

    /// <summary>The caller's user agent in the audit journal.</summary>
    private const string CallerAgent = "test-agent";

    /// <summary>An alias inside the account is stored as an alias, sent to the agent, and journalled.</summary>
    [Fact]
    public async Task Creating_an_alias_inside_the_account_stores_the_row_calls_the_agent_and_journals_it()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "bob@example.com", "carol@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("info@example.com", result.Value.Address);
        Assert.Equal(MailAliasKind.Alias, result.Value.Kind);
        Assert.Equal("MailAliasKindAlias", result.Value.KindName);
        Assert.Equal("acme", result.Value.OwnerName);
        Assert.Equal(["bob@example.com", "carol@example.com"], result.Value.Targets);

        var row = await context.Database.MailAliases.Include(alias => alias.Targets).SingleAsync();
        Assert.Equal(result.Value.Id, row.Id);
        Assert.Equal(AccountA, row.AccountId);
        Assert.Equal("info", row.LocalPart);
        Assert.Equal(MailAliasKind.Alias, row.Kind);
        Assert.Equal(["bob@example.com", "carol@example.com"], MailAliasProbe.AddressesOf(row));

        Assert.Equal(["SetAliases(acme,example.com,aliases=1,catchAll=False)"], context.Agent.Calls);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailAliasCreated, entry.Action);
        Assert.Equal("info@example.com", entry.Subject);
        Assert.True(entry.Succeeded);
        Assert.Equal(CallerIp, entry.IpAddress);
        Assert.Equal(CallerAgent, entry.UserAgent);
    }

    /// <summary>A local part typed in capitals is stored in the one spelling the panel keeps.</summary>
    [Fact]
    public async Task The_local_part_is_stored_trimmed_and_lower_cased()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "  Info.Desk ", "Bob@Example.COM"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("info.desk@example.com", result.Value.Address);
        Assert.Equal(["bob@example.com"], result.Value.Targets);
        Assert.Equal(["info.desk"], MailAliasProbe.LocalParts(context.Agent.LastAliases!));
    }

    /// <summary>A forwarder to an outside address is created when the host allows it, and is stored as a forwarder.</summary>
    [Fact]
    public async Task A_forwarder_to_an_outside_address_is_created_when_the_host_allows_external_forwarding()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: true);
        using var owned = context;

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Command(domain, MailAliasKind.Forwarder, "sales", "me@example.org"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MailAliasKind.Forwarder, result.Value.Kind);
        Assert.Equal("MailAliasKindForwarder", result.Value.KindName);
        Assert.Equal(MailAliasKind.Forwarder, (await context.Database.MailAliases.SingleAsync()).Kind);
        Assert.Equal(["me@example.org"], Assert.Single(context.Agent.LastAliases!).Targets);
    }

    /// <summary>An outside target is refused while the host's switch is off, before the agent is touched.</summary>
    /// <remarks>
    /// The inverse control is the test above: the same request against a host that allows it succeeds, so a
    /// handler that refused every forwarder would fail there.
    /// </remarks>
    [Fact]
    public async Task An_outside_target_is_refused_when_the_host_forbids_external_forwarding()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Command(domain, MailAliasKind.Forwarder, "sales", "me@example.org"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasExternalTargetNotAllowed, ErrorType.Validation);
        Assert.Empty(context.Agent.Calls);
        Assert.Empty(context.Database.MailAliases);
        AssertFailureJournalled(context, HostedMailAuditActions.MailAliasCreated, "sales@example.com");
    }

    /// <summary>An alias that names an outside target, and a forwarder that names only own targets, are mismatches.</summary>
    [Fact]
    public async Task A_stated_kind_that_contradicts_where_the_targets_go_is_refused_in_both_directions()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: true);
        using var owned = context;
        var handler = MailAliasHandlers.Create(context);

        var aliasButOutside = await handler.HandleAsync(
            Command(domain, MailAliasKind.Alias, "a", "me@example.org"), CancellationToken.None);
        var forwarderButInside = await handler.HandleAsync(
            Command(domain, MailAliasKind.Forwarder, "b", "bob@example.com"), CancellationToken.None);

        AssertRefused(aliasButOutside.Error, MailAliasErrorCodes.MailAliasKindMismatch, ErrorType.Validation);
        AssertRefused(forwarderButInside.Error, MailAliasErrorCodes.MailAliasKindMismatch, ErrorType.Validation);
        Assert.Empty(context.Agent.Calls);
        Assert.Empty(context.Database.MailAliases);
    }

    /// <summary>A catch-all is stored under the marker local part, sent as the catch-all and journalled as such.</summary>
    [Fact]
    public async Task A_catch_all_is_stored_under_the_marker_and_sent_as_the_catch_all()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Command(domain, MailAliasKind.CatchAll, string.Empty, "bob@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("*@example.com", result.Value.Address);
        Assert.Equal(MailAliasKind.CatchAll, result.Value.Kind);
        Assert.Equal("MailAliasKindCatchAll", result.Value.KindName);

        var row = await context.Database.MailAliases.SingleAsync();
        Assert.Equal("*", row.LocalPart);
        Assert.Equal(MailAliasKind.CatchAll, row.Kind);

        Assert.Empty(context.Agent.LastAliases!);
        Assert.Equal(["bob@example.com"], context.Agent.LastCatchAll);

        var entry = Assert.Single(context.Entries);
        Assert.Equal(HostedMailAuditActions.MailCatchAllSet, entry.Action);
        Assert.Equal("*@example.com", entry.Subject);
        Assert.True(entry.Succeeded);
    }

    /// <summary>A catch-all to an outside address needs the host's switch like any other forwarding.</summary>
    [Fact]
    public async Task A_catch_all_to_an_outside_address_needs_the_hosts_switch()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Command(domain, MailAliasKind.CatchAll, string.Empty, "me@example.org"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasExternalTargetNotAllowed, ErrorType.Validation);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A domain holds one catch-all, and a second is refused with its own code and no agent call.</summary>
    [Fact]
    public async Task A_second_catch_all_in_a_domain_is_a_conflict()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        await MailAliasSeeds.SeedAliasAsync(context, domain, "*", MailAliasKind.CatchAll, "bob@example.com");

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Command(domain, MailAliasKind.CatchAll, string.Empty, "carol@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailCatchAllConflict, ErrorType.Conflict);
        Assert.Empty(context.Agent.Calls);
        Assert.Equal(1, await context.Database.MailAliases.CountAsync());
        AssertFailureJournalled(context, HostedMailAuditActions.MailCatchAllSet, "*@example.com");
    }

    /// <summary>An address another alias holds is taken.</summary>
    [Fact]
    public async Task An_address_another_alias_holds_is_taken()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        await MailAliasSeeds.SeedAliasAsync(context, domain, "info", MailAliasKind.Alias, "bob@example.com");

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "carol@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasTaken, ErrorType.Conflict);
        Assert.Empty(context.Agent.Calls);
        Assert.Equal(1, await context.Database.MailAliases.CountAsync());
    }

    /// <summary>An address a mailbox holds is taken: one address, one meaning, across both tables.</summary>
    [Fact]
    public async Task An_address_a_mailbox_holds_is_taken()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        await context.SeedMailboxAsync(domain, "info");

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "carol@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasTaken, ErrorType.Conflict);
        Assert.Empty(context.Agent.Calls);
        Assert.Empty(context.Database.MailAliases);
    }

    /// <summary>A mailbox that holds a DIFFERENT local part does not take the address.</summary>
    [Fact]
    public async Task A_mailbox_with_another_local_part_does_not_take_the_address()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        await context.SeedMailboxAsync(domain, "bob");

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "bob@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>The same local part in ANOTHER domain of the account is free.</summary>
    [Fact]
    public async Task The_same_local_part_in_another_domain_is_free()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var other = await context.SeedDomainAsync(AccountA, "example.net");
        await MailAliasSeeds.SeedAliasAsync(context, other, "info", MailAliasKind.Alias, "bob@example.net");

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "bob@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>The agent is sent every other alias of the domain and the existing catch-all, and nothing of other domains.</summary>
    [Fact]
    public async Task The_agent_is_sent_the_complete_set_of_the_domain_and_no_other_domains()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var other = await context.SeedDomainAsync(AccountA, "example.net");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "sales", MailAliasKind.Alias, "x@example.com", "y@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "*", MailAliasKind.CatchAll, "boss@example.com");
        await MailAliasSeeds.SeedAliasAsync(context, other, "elsewhere", MailAliasKind.Alias, "z@example.net");

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "bob@example.com"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = context.Agent.LastAliases!;
        Assert.Equal(["info", "sales"], MailAliasProbe.LocalParts(sent));
        Assert.Equal(["x@example.com", "y@example.com"], MailAliasProbe.TargetsOf(sent, "sales"));
        Assert.Equal(["bob@example.com"], MailAliasProbe.TargetsOf(sent, "info"));
        Assert.Equal(["boss@example.com"], context.Agent.LastCatchAll);
        Assert.DoesNotContain("elsewhere", MailAliasProbe.LocalParts(sent));
        Assert.Equal(["SetAliases(acme,example.com,aliases=2,catchAll=True)"], context.Agent.Calls);
    }

    /// <summary>A target on another account's domain is outside this account, even for an administrator who sees both.</summary>
    /// <remarks>
    /// The administrator's context is not narrowed by the tenant filter, so "the account's own domains" must
    /// be narrowed explicitly. The inverse control is the second request, whose target is on the owner's own
    /// domain and is accepted.
    /// </remarks>
    [Fact]
    public async Task An_administrator_creating_in_a_customers_domain_judges_targets_against_the_owners_domains()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(AccountA, "acme");
        context.Accounts.Add(AccountB, "beta");
        await MailAliasSeeds.SeedServerAsync(context, allowExternalForwarding: false);
        var owned = await context.SeedDomainAsync(AccountA, "example.com");
        await context.SeedDomainAsync(AccountB, "other.example");
        var handler = MailAliasHandlers.Create(context);

        var outside = await handler.HandleAsync(
            Alias(owned, "info", "x@other.example"), CancellationToken.None);
        var inside = await handler.HandleAsync(
            Alias(owned, "info", "x@example.com"), CancellationToken.None);

        AssertRefused(outside.Error, MailAliasErrorCodes.MailAliasExternalTargetNotAllowed, ErrorType.Validation);
        Assert.True(inside.IsSuccess);
        Assert.Equal(AccountA, inside.Value.AccountId);
    }

    /// <summary>Mail that was never enabled, or was disabled, refuses the creation before anything else.</summary>
    [Fact]
    public async Task A_server_with_mail_off_refuses_the_creation()
    {
        using var never = Customer(Guid.NewGuid().ToString());
        never.Accounts.Add(AccountA, "acme");
        var neverDomain = await never.SeedDomainAsync(AccountA, "example.com");

        using var disabled = Customer(Guid.NewGuid().ToString());
        disabled.Accounts.Add(AccountA, "acme");
        await MailAliasSeeds.SeedDisabledServerAsync(disabled);
        var disabledDomain = await disabled.SeedDomainAsync(AccountA, "example.com");

        var neverResult = await MailAliasHandlers.Create(never).HandleAsync(
            Alias(neverDomain, "info", "bob@example.com"), CancellationToken.None);
        var disabledResult = await MailAliasHandlers.Create(disabled).HandleAsync(
            Alias(disabledDomain, "info", "bob@example.com"), CancellationToken.None);

        AssertRefused(neverResult.Error, MailAliasErrorCodes.MailNotEnabled, ErrorType.Conflict);
        AssertRefused(disabledResult.Error, MailAliasErrorCodes.MailNotEnabled, ErrorType.Conflict);
        Assert.Empty(never.Agent.Calls);
        Assert.Empty(disabled.Agent.Calls);
    }

    /// <summary>A domain that does not exist is not found, and the refusal is journalled with the identifier probed.</summary>
    [Fact]
    public async Task A_domain_that_does_not_exist_is_not_found()
    {
        var (context, _) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var missing = Guid.NewGuid();

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            new CreateMailAliasCommand(missing, MailAliasKind.Alias, "info", ["bob@example.com"], CallerIp, CallerAgent),
            CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailDomainNotFound, ErrorType.NotFound);
        Assert.Empty(context.Agent.Calls);
        AssertFailureJournalled(context, HostedMailAuditActions.MailAliasCreated, missing.ToString());
    }

    /// <summary>Another account's domain answers exactly as a missing one does, and the owner's own domain is found.</summary>
    [Fact]
    public async Task Another_accounts_domain_is_not_found_rather_than_forbidden()
    {
        var databaseName = Guid.NewGuid().ToString();
        using var owner = new MailTestContext(new CustomerCurrentUser(AccountB), databaseName);
        owner.Accounts.Add(AccountB, "beta");
        await MailAliasSeeds.SeedServerAsync(owner, allowExternalForwarding: false);
        var victim = await owner.SeedDomainAsync(AccountB, "beta.example");

        using var attacker = new MailTestContext(new CustomerCurrentUser(AccountA), databaseName);
        attacker.Accounts.Add(AccountA, "acme");
        attacker.Accounts.Add(AccountB, "beta");

        var refused = await MailAliasHandlers.Create(attacker).HandleAsync(
            Alias(victim, "info", "bob@beta.example"), CancellationToken.None);
        var own = await MailAliasHandlers.Create(owner).HandleAsync(
            Alias(victim, "info", "bob@beta.example"), CancellationToken.None);

        AssertRefused(refused.Error, MailAliasErrorCodes.MailDomainNotFound, ErrorType.NotFound);
        Assert.Empty(attacker.Agent.Calls);
        Assert.True(own.IsSuccess);
        Assert.Equal(1, await owner.Database.MailAliases.CountAsync());
    }

    /// <summary>A domain whose account the directory cannot resolve is refused before the agent, not addressed with a placeholder.</summary>
    [Fact]
    public async Task A_domain_whose_account_cannot_be_resolved_is_refused_before_the_agent()
    {
        using var context = Customer(Guid.NewGuid().ToString());
        await MailAliasSeeds.SeedServerAsync(context, allowExternalForwarding: false);
        var domain = await context.SeedDomainAsync(AccountA, "example.com");

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "bob@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.AccountNotFound, ErrorType.NotFound);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>A target list the policy refuses is refused by the handler too, for a command dispatched on the bus.</summary>
    [Fact]
    public async Task A_target_list_the_policy_refuses_is_refused_with_its_code()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        var handler = MailAliasHandlers.Create(context);

        var invalid = await handler.HandleAsync(Alias(domain, "info", "no-at-sign"), CancellationToken.None);
        var tooMany = await handler.HandleAsync(
            Alias(
                domain,
                "info",
                Enumerable.Range(0, 21)
                    .Select(index =>
                    {
                        return $"u{index}@example.com";
                    })
                    .ToArray()),
            CancellationToken.None);

        AssertRefused(invalid.Error, MailAliasErrorCodes.MailAliasTargetInvalid, ErrorType.Validation);
        AssertRefused(tooMany.Error, MailAliasErrorCodes.MailAliasTargetLimit, ErrorType.Validation);
        Assert.Empty(context.Agent.Calls);
    }

    /// <summary>The agent's refusal is returned as it came, no row is written, and the failure is journalled.</summary>
    [Fact]
    public async Task An_agent_failure_is_returned_unchanged_and_writes_no_row()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        context.Agent.FailNext("SetAliases", Error.Of("AgentUnavailable", ErrorType.Failure));

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "bob@example.com"), CancellationToken.None);

        AssertRefused(result.Error, "AgentUnavailable", ErrorType.Failure);
        Assert.Empty(context.Database.MailAliases);
        Assert.Single(context.Agent.Calls);
        AssertFailureJournalled(context, HostedMailAuditActions.MailAliasCreated, "info@example.com");
    }

    /// <summary>A row that cannot be written puts the previous set back on the host and says provisioning failed.</summary>
    [Fact]
    public async Task A_row_that_cannot_be_written_restores_the_previous_set_on_the_host()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        await MailAliasSeeds.SeedAliasAsync(context, domain, "sales", MailAliasKind.Alias, "x@example.com");
        context.SaveFailure.Next = new DbUpdateException("insert failed", new InvalidOperationException("db down"));

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "bob@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasProvisioningFailed, ErrorType.Failure);
        Assert.Equal(2, MailAliasProbe.SetAliasesCalls(context));

        // The LAST call is the restore: it carries the set as it was, without the address that failed.
        Assert.Equal(["sales"], MailAliasProbe.LocalParts(context.Agent.LastAliases!));
        Assert.Equal(1, await context.Database.MailAliases.CountAsync());
        AssertFailureJournalled(context, HostedMailAuditActions.MailAliasCreated, "info@example.com");
    }

    /// <summary>A lost race for the address is a conflict, not a provisioning failure, and the host is put back.</summary>
    [Fact]
    public async Task A_duplicate_at_the_database_is_answered_as_taken_and_the_host_is_put_back()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        await MailAliasSeeds.SeedAliasAsync(context, domain, "sales", MailAliasKind.Alias, "x@example.com");
        context.SaveFailure.Next = new DbUpdateException("insert failed", UniqueViolation());

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Alias(domain, "info", "bob@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailAliasTaken, ErrorType.Conflict);
        Assert.Equal(["sales"], MailAliasProbe.LocalParts(context.Agent.LastAliases!));
        Assert.Equal(2, MailAliasProbe.SetAliasesCalls(context));
    }

    /// <summary>A lost race for the catch-all is the catch-all conflict.</summary>
    [Fact]
    public async Task A_duplicate_catch_all_at_the_database_is_answered_as_the_catch_all_conflict()
    {
        var (context, domain) = await ReadyAsync(allowExternalForwarding: false);
        using var owned = context;
        context.SaveFailure.Next = new DbUpdateException("insert failed", UniqueViolation());

        var result = await MailAliasHandlers.Create(context).HandleAsync(
            Command(domain, MailAliasKind.CatchAll, string.Empty, "bob@example.com"), CancellationToken.None);

        AssertRefused(result.Error, MailAliasErrorCodes.MailCatchAllConflict, ErrorType.Conflict);
        Assert.Null(context.Agent.LastCatchAll);
    }

    /// <summary>Builds a customer context for <see cref="AccountA"/> on a named database.</summary>
    /// <param name="databaseName">The in-memory database to share.</param>
    /// <returns>The assembled module, seen as that customer.</returns>
    private static MailTestContext Customer(string databaseName)
    {
        return new MailTestContext(new CustomerCurrentUser(AccountA), databaseName);
    }

    /// <summary>Builds a customer context with mail on, the account known and one domain, <c>example.com</c>.</summary>
    /// <param name="allowExternalForwarding">Whether the host lets a forwarder leave the account's domains.</param>
    /// <returns>The context and the domain.</returns>
    private static async Task<(MailTestContext Context, MailDomain Domain)> ReadyAsync(bool allowExternalForwarding)
    {
        var context = Customer(Guid.NewGuid().ToString());
        context.Accounts.Add(AccountA, "acme");
        await MailAliasSeeds.SeedServerAsync(context, allowExternalForwarding);
        var domain = await context.SeedDomainAsync(AccountA, "example.com");

        return (context, domain);
    }

    /// <summary>Builds a creation command with the caller's address and agent stamped.</summary>
    /// <param name="domain">The domain.</param>
    /// <param name="kind">The stated kind.</param>
    /// <param name="localPart">The local part.</param>
    /// <param name="targets">The targets.</param>
    /// <returns>The command.</returns>
    private static CreateMailAliasCommand Command(
        MailDomain domain,
        MailAliasKind kind,
        string localPart,
        params string[] targets)
    {
        return new CreateMailAliasCommand(domain.Id, kind, localPart, targets, CallerIp, CallerAgent);
    }

    /// <summary>Builds a command for an address the targets make an alias, the usual case.</summary>
    /// <param name="domain">The domain.</param>
    /// <param name="localPart">The local part.</param>
    /// <param name="targets">The targets.</param>
    /// <returns>The command.</returns>
    private static CreateMailAliasCommand Alias(MailDomain domain, string localPart, params string[] targets)
    {
        return Command(domain, MailAliasKind.Alias, localPart, targets);
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

    /// <summary>Builds the database failure PostgreSQL raises for a duplicate key.</summary>
    /// <returns>A <see cref="PostgresException"/> carrying SqlState <c>23505</c>.</returns>
    private static PostgresException UniqueViolation()
    {
        return new PostgresException(
            "duplicate key value violates unique constraint \"IX_MailAliases_MailDomainId_LocalPart\"",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation);
    }
}
