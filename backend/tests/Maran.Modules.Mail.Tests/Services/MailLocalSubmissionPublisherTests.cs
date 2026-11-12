using Maran.Modules.Mail.Domain.Enums;
using Maran.Modules.Mail.Domain.Policies;
using Maran.Modules.Mail.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Mail.Tests.Services;

/// <summary>
/// The local submission publisher: the binding is rebuilt whole from the account's own rows and plan, mail
/// that is off and accounts that are not visible send nothing, and a refusal is returned or logged by kind.
/// </summary>
public sealed class MailLocalSubmissionPublisherTests
{
    /// <summary>The agent's code for a figure it refused.</summary>
    private const string Refused = "AgentInvalidInput";

    /// <summary>The account these tests bind.</summary>
    private static readonly Guid AccountId = new("6a2f1c4d-7e08-4a19-9d2b-3c5f8e10ab72");

    /// <summary>Another account whose rows must never appear in the first one's binding.</summary>
    private static readonly Guid OtherAccountId = new("0f5c2a13-8b46-4d7e-a1c9-25e7b4d6f083");

    /// <summary>The binding carries the account's own domains, mailbox logins and plan send limit, sorted.</summary>
    [Fact]
    public async Task The_binding_carries_the_accounts_own_domains_logins_and_plan_limit()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme", new MailLimitsSnapshot(10, 1024, 250, 10240));
        var b = await context.SeedDomainAsync(AccountId, "b.test");
        var a = await context.SeedDomainAsync(AccountId, "a.test");
        await context.SeedMailboxAsync(b, "sales");
        await context.SeedMailboxAsync(a, "info");

        var result = await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Equal("acme", sent.AccountUsername);
        Assert.Equal(["a.test", "b.test"], sent.Domains);
        Assert.Equal(["info@a.test", "sales@b.test"], sent.MailboxLogins);
        Assert.Equal(250u, sent.SendLimitPerHour);
    }

    /// <summary>The inverse control: another account's rows never leak into the binding.</summary>
    [Fact]
    public async Task Another_accounts_rows_never_appear_in_the_binding()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");
        context.Accounts.Add(OtherAccountId, "globex");
        var own = await context.SeedDomainAsync(AccountId, "own.test");
        var foreign = await context.SeedDomainAsync(OtherAccountId, "foreign.test");
        await context.SeedMailboxAsync(own, "info");
        await context.SeedMailboxAsync(foreign, "info");

        await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Equal(["own.test"], sent.Domains);
        Assert.Equal(["info@own.test"], sent.MailboxLogins);
    }

    /// <summary>A forwarding alias and a catch-all are sent with the deliveries that leave the account; an alias that stays inside is sent with zero.</summary>
    [Fact]
    public async Task A_forwarding_alias_and_a_catch_all_are_sent_with_their_outside_delivery_count()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "a.test");
        await context.SeedMailboxAsync(domain, "desk");
        await MailAliasSeeds.SeedAliasAsync(
            context,
            domain,
            "sales",
            MailAliasKind.Forwarder,
            "one@outside.test",
            "two@outside.test",
            "desk@a.test");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "team", MailAliasKind.Alias, "desk@a.test");
        await MailAliasSeeds.SeedAliasAsync(
            context,
            domain,
            MailboxAddressPolicy.CatchAllLocalPart,
            MailAliasKind.CatchAll,
            "x@outside.test");

        await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Equal(3, sent.AliasFanouts.Count);
        Assert.Equal("@a.test", sent.AliasFanouts[0].Alias);
        Assert.Equal(1u, sent.AliasFanouts[0].ExternalTargets);
        Assert.Equal("sales@a.test", sent.AliasFanouts[1].Alias);
        Assert.Equal(2u, sent.AliasFanouts[1].ExternalTargets);
        // The inside alias is sent too, with zero outside deliveries, so the filter never lets it fall
        // through to the catch-all.
        Assert.Equal("team@a.test", sent.AliasFanouts[2].Alias);
        Assert.Equal(0u, sent.AliasFanouts[2].ExternalTargets);
    }

    /// <summary>An alias of an alias is charged what the inner alias delivers; the inverse control is that the inner list alone is the same figure.</summary>
    [Fact]
    public async Task An_alias_of_an_alias_is_sent_with_the_inner_aliass_outside_deliveries()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");
        var domain = await context.SeedDomainAsync(AccountId, "a.test");
        await MailAliasSeeds.SeedAliasAsync(
            context,
            domain,
            "inner",
            MailAliasKind.Forwarder,
            "one@outside.test",
            "two@outside.test",
            "three@outside.test");
        await MailAliasSeeds.SeedAliasAsync(context, domain, "outer", MailAliasKind.Alias, "inner@a.test");

        await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Equal(["inner@a.test", "outer@a.test"], sent.AliasFanouts.Select(fanout =>
        {
            return fanout.Alias;
        }).Order(StringComparer.Ordinal));
        Assert.All(sent.AliasFanouts, fanout =>
        {
            Assert.Equal(3u, fanout.ExternalTargets);
        });
    }

    /// <summary>The inverse control: another account's forwarder never appears in the binding.</summary>
    [Fact]
    public async Task Another_accounts_forwarder_is_not_in_the_binding()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");
        context.Accounts.Add(OtherAccountId, "globex");
        await context.SeedDomainAsync(AccountId, "own.test");
        var foreign = await context.SeedDomainAsync(OtherAccountId, "foreign.test");
        await MailAliasSeeds.SeedAliasAsync(context, foreign, "sales", MailAliasKind.Forwarder, "x@outside.test");

        await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Empty(sent.AliasFanouts);
    }

    /// <summary>An account with no rows is still bound, with empty lists, so it can send nothing.</summary>
    [Fact]
    public async Task An_account_with_no_mail_rows_is_bound_with_empty_lists()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");

        await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        var sent = Assert.Single(context.Agent.LocalSubmissions);
        Assert.Empty(sent.Domains);
        Assert.Empty(sent.MailboxLogins);
        Assert.Empty(sent.AliasFanouts);
    }

    /// <summary>With mail off nothing is sent; with it on the same call sends (the control).</summary>
    [Fact]
    public async Task Nothing_is_sent_while_mail_is_off_and_the_same_call_sends_once_it_is_on()
    {
        using var context = new MailTestContext();
        context.Accounts.Add(AccountId, "acme");

        var off = await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);
        var removedOff = await context.LocalSubmission.RemoveAsync("acme", CancellationToken.None);
        await context.SeedEnabledServerAsync();
        var on = await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        Assert.True(off.IsSuccess && removedOff.IsSuccess && on.IsSuccess);
        Assert.Empty(context.Agent.LocalSubmissionRemovals);
        Assert.Single(context.Agent.LocalSubmissions);
    }

    /// <summary>An account the tenant scope hides is still bound, because the binding is a system action.</summary>
    [Fact]
    public async Task An_account_the_tenant_scope_hides_is_still_bound()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme");
        context.Accounts.HideFromScopedReads(AccountId);

        var result = await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        Assert.Null(await context.Accounts.FindAsync(AccountId, CancellationToken.None));
        Assert.True(result.IsSuccess);
        Assert.Single(context.Agent.LocalSubmissions);
    }

    /// <summary>An account that does not exist is not found and nothing is sent.</summary>
    [Fact]
    public async Task An_account_that_does_not_exist_is_not_found()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();

        var result = await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("AccountNotFound", result.Error!.Code);
        Assert.Empty(context.Agent.LocalSubmissions);
    }

    /// <summary>A refusal by the agent is returned by publishing and logged, not thrown, after a committed change.</summary>
    [Fact]
    public async Task A_refusal_is_returned_by_publishing_and_swallowed_after_a_committed_change()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        var account = context.Accounts.Add(AccountId, "acme");
        context.Agent.FailNext("SetLocalSubmission", Error.Of(Refused, ErrorType.Validation));

        var returned = await context.LocalSubmission.PublishAsync(account, CancellationToken.None);
        context.Agent.FailNext("SetLocalSubmission", Error.Of(Refused, ErrorType.Validation));
        await context.LocalSubmission.TryPublishAfterChangeAsync(account, CancellationToken.None);

        Assert.False(returned.IsSuccess);
        Assert.Equal(Refused, returned.Error!.Code);
        Assert.Equal(2, context.Agent.LocalSubmissions.Count);
    }

    /// <summary>A suspended account is skipped after a change but is bound by an explicit publish (the resumption).</summary>
    [Fact]
    public async Task A_suspended_account_is_skipped_after_a_change_but_bound_by_an_explicit_publish()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        var account = context.Accounts.Add(AccountId, "acme", null, AccountState.Suspended);

        await context.LocalSubmission.TryPublishAfterChangeAsync(account, CancellationToken.None);
        Assert.Empty(context.Agent.LocalSubmissions);

        await context.LocalSubmission.PublishAsync(account, CancellationToken.None);
        Assert.Single(context.Agent.LocalSubmissions);
    }

    /// <summary>A plan figure of zero is sent as zero, never replaced by a default or read as no limit.</summary>
    [Fact]
    public async Task A_zero_plan_limit_is_sent_as_zero()
    {
        using var context = new MailTestContext();
        await context.SeedEnabledServerAsync();
        context.Accounts.Add(AccountId, "acme", new MailLimitsSnapshot(10, 1024, 0, 10240));

        await context.LocalSubmission.PublishAsSystemAsync(AccountId, CancellationToken.None);

        Assert.Equal(0u, Assert.Single(context.Agent.LocalSubmissions).SendLimitPerHour);
    }
}
