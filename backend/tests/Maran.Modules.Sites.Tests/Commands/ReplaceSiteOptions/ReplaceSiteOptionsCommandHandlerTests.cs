using Maran.Agent.Client.Services.SitesService;
using Maran.Modules.Sites.Commands.ReplaceSiteOptions;
using Maran.Modules.Sites.Common;
using Maran.Modules.Sites.Domain.Enums;
using Maran.Modules.Sites.Domain.Policies;
using Maran.Modules.Sites.Domain.ValueObjects;
using Maran.Modules.Sites.Persistence;
using Maran.Modules.Sites.Services;
using Maran.Modules.Sites.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Sites.Tests.Commands.ReplaceSiteOptions;

/// <summary>Behavioral contract of <see cref="ReplaceSiteOptionsCommandHandler"/>.</summary>
public sealed class ReplaceSiteOptionsCommandHandlerTests
{
    /// <summary>A replacement is rendered by the agent and then stored, and survives into a fresh context.</summary>
    [Fact]
    public async Task A_replacement_is_rendered_by_the_agent_and_then_stored_and_survives_into_a_fresh_context()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            var result = await Handler(context, account, agent).HandleAsync(
                Command(siteId) with
                {
                    ClientMaxBodySizeMib = 64,
                    StaticCacheDays = 30,
                    Redirects = [new SiteRedirectDto("/old", RedirectMatch.Exact, "/new", RedirectStatus.Permanent)],
                    IpAccessMode = IpAccessMode.AllowList,
                    IpRules = ["203.0.113.0/24"],
                },
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(64, result.Value.ClientMaxBodySizeMib);
        }

        Assert.Equal("enable:acme:example.com", Assert.Single(agent.Calls));

        // Read back through a SECOND context: the handler's own tracker returns the modified entity
        // whether or not SaveChangesAsync was ever called.
        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var stored = (await reader.Sites.SingleAsync()).Options!;
        Assert.Equal(64, stored.ClientMaxBodySizeMib);
        Assert.Equal(30, stored.StaticCacheDays);
        Assert.Equal("/old", Assert.Single(stored.Redirects).Path);
        Assert.Equal(IpAccessMode.AllowList, stored.IpAccessMode);
        Assert.Equal("203.0.113.0/24", Assert.Single(stored.IpRules).Cidr);
    }

    /// <summary>The descriptor handed to the agent carries the new options, with a hash and never the password.</summary>
    [Fact]
    public async Task The_descriptor_handed_to_the_agent_carries_the_new_options_with_a_hash_and_never_the_password()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, agent).HandleAsync(
            Command(siteId) with
            {
                BasicAuthUsers = [new SiteBasicAuthUserInputDto("alice", "correct horse battery")],
                Redirects = [new SiteRedirectDto("/blog/", RedirectMatch.Prefix, "https://example.org/blog/", RedirectStatus.Temporary)],
            },
            CancellationToken.None);

        var options = Assert.Single(agent.Descriptors).Options;
        var user = Assert.Single(options.BasicAuthUsers);
        Assert.Equal("alice", user.Name);
        Assert.True(BasicAuthPasswordHash.IsHash(user.PasswordHash));
        Assert.DoesNotContain("correct horse battery", user.PasswordHash, StringComparison.Ordinal);
        var rule = Assert.Single(options.Redirects);
        Assert.Equal(RedirectMatchKind.Prefix, rule.Match);
        Assert.Equal(302u, rule.StatusCode);
    }

    /// <summary>A request limit is rendered by the agent, stored, and shown in the answer.</summary>
    [Fact]
    public async Task A_request_limit_is_rendered_by_the_agent_stored_and_shown_in_the_answer()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            var result = await Handler(context, account, agent).HandleAsync(
                Command(siteId) with { RequestRate = new SiteRequestRateDto(10, 20) },
                CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Equal(new SiteRequestRateDto(10, 20), result.Value.RequestRate);
        }

        Assert.Equal(new RequestRateDescriptor(10, 20), Assert.Single(agent.Descriptors).Options.RequestRate);

        // Read back through a SECOND context, for the reason the first test gives.
        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Equal(new SiteRequestRate(10, 20), (await reader.Sites.SingleAsync()).Options!.RequestRate);
    }

    /// <summary>Replacing a limited site's options with a set that states no limit removes the limit.</summary>
    [Fact]
    public async Task Replacing_a_limited_sites_options_with_a_set_that_states_no_limit_removes_the_limit()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com", SiteOptionsSamples.Full());
        var agent = new RecordingAgentSitesClient();
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            var result = await Handler(context, account, agent).HandleAsync(Command(siteId), CancellationToken.None);

            Assert.True(result.IsSuccess);
            Assert.Null(result.Value.RequestRate);
        }

        Assert.Null(Assert.Single(agent.Descriptors).Options.RequestRate);
        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Null((await reader.Sites.SingleAsync()).Options!.RequestRate);
    }

    /// <summary>The audit entry names the request limit as a changed group and never its values.</summary>
    [Fact]
    public async Task The_audit_entry_names_the_request_limit_as_a_changed_group_and_never_its_values()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, new RecordingAgentSitesClient(), audit: audit).HandleAsync(
            Command(siteId) with { RequestRate = new SiteRequestRateDto(731, 419) },
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Contains(SiteOptionGroups.RequestRate, entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("731", entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("419", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>The password never reaches the stored row, the answer or the audit entry.</summary>
    [Fact]
    public async Task The_password_never_reaches_the_stored_row_the_answer_or_the_audit_entry()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, new RecordingAgentSitesClient(), audit: audit).HandleAsync(
            Command(siteId) with { BasicAuthUsers = [new SiteBasicAuthUserInputDto("alice", "correct horse battery")] },
            CancellationToken.None);

        Assert.Equal(["alice"], result.Value.BasicAuthUsers);
        var stored = Assert.Single((await context.Sites.SingleAsync()).Options!.BasicAuthUsers);
        Assert.True(BasicAuthPasswordHash.IsHash(stored.PasswordHash));
        var entry = Assert.Single(audit.Entries);
        Assert.DoesNotContain("correct horse battery", entry.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain(stored.PasswordHash, entry.Subject, StringComparison.Ordinal);
        Assert.Contains("alice", entry.Subject, StringComparison.Ordinal);
    }

    /// <summary>A user already on the site who states no password keeps the stored hash.</summary>
    [Fact]
    public async Task A_user_already_on_the_site_who_states_no_password_keeps_the_stored_hash()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com", SiteOptionsSamples.Full());
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            Command(siteId) with { BasicAuthUsers = [new SiteBasicAuthUserInputDto("alice", null)] },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(SiteOptionsSamples.StoredHash, Assert.Single(Assert.Single(agent.Descriptors).Options.BasicAuthUsers).PasswordHash);
        Assert.Equal(SiteOptionsSamples.StoredHash, Assert.Single((await context.Sites.SingleAsync()).Options!.BasicAuthUsers).PasswordHash);
    }

    /// <summary>A user the site does not have yet must state a password.</summary>
    [Fact]
    public async Task A_user_the_site_does_not_have_yet_must_state_a_password()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            Command(siteId) with { BasicAuthUsers = [new SiteBasicAuthUserInputDto("bob", null)] },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteBasicAuthPasswordRequired", result.Error!.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Empty(agent.Calls);
    }

    /// <summary>A user left out of the replacement is removed.</summary>
    [Fact]
    public async Task A_user_left_out_of_the_replacement_is_removed()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com", SiteOptionsSamples.Full());
        var agent = new RecordingAgentSitesClient();
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            await Handler(context, account, agent).HandleAsync(Command(siteId), CancellationToken.None);
        }

        Assert.Empty(Assert.Single(agent.Descriptors).Options.BasicAuthUsers);
        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var stored = (await reader.Sites.SingleAsync()).Options!;
        Assert.Empty(stored.BasicAuthUsers);
        Assert.Empty(stored.Redirects);
        Assert.Empty(stored.IpRules);
        Assert.Null(stored.IpAccessMode);
    }

    /// <summary>An agent refusal stores nothing and journals the failure.</summary>
    [Fact]
    public async Task An_agent_refusal_stores_nothing_and_journals_the_failure()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com", SiteOptionsSamples.Full());
        var agent = new RecordingAgentSitesClient(Error.Of("AgentValidationFailed", ErrorType.Validation));
        var audit = new RecordingAuditWriter();
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            var result = await Handler(context, account, agent, audit: audit).HandleAsync(
                Command(siteId) with { StaticCacheDays = 7 },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("AgentValidationFailed", result.Error!.Code);
        }

        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var stored = (await reader.Sites.SingleAsync()).Options!;
        Assert.Equal(30, stored.StaticCacheDays);
        Assert.Single(stored.Redirects);
        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal("example.com", entry.Subject);
    }

    /// <summary>A disabled site is stored without asking the agent, so it is not switched back on.</summary>
    [Fact]
    public async Task A_disabled_site_is_stored_without_asking_the_agent_so_it_is_not_switched_back_on()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com", disabled: true);
        var agent = new RecordingAgentSitesClient();
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            var result = await Handler(context, account, agent).HandleAsync(
                Command(siteId) with { StaticCacheDays = 7 },
                CancellationToken.None);

            Assert.True(result.IsSuccess);
        }

        Assert.Empty(agent.Calls);
        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = await reader.Sites.SingleAsync();
        Assert.Equal(SiteStatus.Disabled, site.Status);
        Assert.Equal(7, site.Options!.StaticCacheDays);
    }

    /// <summary>Another tenants site answers not found, touches neither the agent nor the row, and is journalled.</summary>
    [Fact]
    public async Task Another_tenants_site_answers_not_found_touches_neither_the_agent_nor_the_row_and_is_journalled()
    {
        // The IDOR test (rules/testing.md Definition of Done 3): 403 would confirm the id names a real site.
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var theirSiteId = await SeedAsync(database, theirs, "theirs.example.com");
        var agent = new RecordingAgentSitesClient();
        var audit = new RecordingAuditWriter();
        await using (var context = SitesTestContext.Create(FakeCurrentUser.Customer(mine), database))
        {
            var result = await Handler(context, mine, agent, audit: audit).HandleAsync(
                Command(theirSiteId) with { StaticCacheDays = 7 },
                CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal("SiteNotFound", result.Error!.Code);
            Assert.Equal(ErrorType.NotFound, result.Error.Type);
        }

        Assert.Empty(agent.Calls);
        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        Assert.Null((await reader.Sites.SingleAsync()).Options);
        var entry = Assert.Single(audit.Entries);
        Assert.False(entry.Succeeded);
        Assert.Equal(theirSiteId.ToString(), entry.Subject);
    }

    /// <summary>A success is journalled with the domain, the groups that changed and the user names.</summary>
    [Fact]
    public async Task A_success_is_journalled_with_the_domain_the_groups_that_changed_and_the_user_names()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(
            database, account, "example.com", SiteOptionsSamples.Full(), aliases: ["www.example.com"]);
        var audit = new RecordingAuditWriter();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        await Handler(context, account, new RecordingAgentSitesClient(), audit: audit).HandleAsync(
            Command(siteId) with
            {
                CanonicalHost = CanonicalHost.Www,
                ClientMaxBodySizeMib = 64,
                StaticCacheDays = 99,
                Redirects = [new SiteRedirectDto("/old", RedirectMatch.Exact, "/new", RedirectStatus.Permanent)],
                BasicAuthUsers = [new SiteBasicAuthUserInputDto("alice", null)],
                IpAccessMode = IpAccessMode.AllowList,
                IpRules = ["203.0.113.0/24"],
                RequestRate = new SiteRequestRateDto(10, 20),
            },
            CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(SiteAuditActions.SiteOptionsChanged, entry.Action);
        Assert.True(entry.Succeeded);
        Assert.Equal("example.com: cache; basic-auth users: alice", entry.Subject);
    }

    /// <summary>Saying the same thing twice converges to the same state.</summary>
    [Fact]
    public async Task Saying_the_same_thing_twice_converges_to_the_same_state()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var request = Command(siteId) with
        {
            Redirects = [new SiteRedirectDto("/old", RedirectMatch.Exact, "/new", RedirectStatus.Permanent)],
            IpAccessMode = IpAccessMode.DenyList,
            IpRules = ["198.51.100.0/24"],
        };
        await using (var first = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            await Handler(first, account, new RecordingAgentSitesClient()).HandleAsync(request, CancellationToken.None);
        }

        await using (var second = SitesTestContext.Create(FakeCurrentUser.Customer(account), database))
        {
            await Handler(second, account, new RecordingAgentSitesClient()).HandleAsync(request, CancellationToken.None);
        }

        await using var reader = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var stored = (await reader.Sites.SingleAsync()).Options!;
        Assert.Single(stored.Redirects);
        Assert.Single(stored.IpRules);
        Assert.Single(await reader.SiteOptions.ToListAsync());
    }

    /// <summary>A canonical host needs both names of the pair to be the site's own.</summary>
    [Fact]
    public async Task A_canonical_host_needs_both_names_of_the_pair_to_be_the_sites_own()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            Command(siteId) with { CanonicalHost = CanonicalHost.Www },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteCanonicalHostNotServed", result.Error!.Code);
        Assert.Empty(agent.Calls);
    }

    /// <summary>A canonical host is accepted when the site claims the plain name and its www form, either way round.</summary>
    [Theory]
    [InlineData("example.com", "www.example.com")]
    [InlineData("www.example.com", "example.com")]
    public async Task A_canonical_host_is_accepted_when_the_site_claims_the_plain_name_and_its_www_form_either_way_round(
        string domain,
        string alias)
    {
        // The inverse control of the test above, so the check cannot be satisfied by refusing everything.
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, domain, aliases: [alias]);
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, new RecordingAgentSitesClient()).HandleAsync(
            Command(siteId) with { CanonicalHost = CanonicalHost.Apex },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(CanonicalHost.Apex, result.Value.CanonicalHost);
    }

    /// <summary>A save that fails after the agent accepted renders the vhost again from the row as it was.</summary>
    [Fact]
    public async Task A_save_that_fails_after_the_agent_accepted_renders_the_vhost_again_from_the_row_as_it_was()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com", SiteOptionsSamples.Full());
        var agent = new RecordingAgentSitesClient();
        var failing = new FailingSaveInterceptor();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database, failing);
        failing.Armed = true;

        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await Handler(context, account, agent).HandleAsync(
                Command(siteId) with { StaticCacheDays = 7 },
                CancellationToken.None);
        });

        Assert.Equal(["enable:acme:example.com", "enable:acme:example.com"], agent.Calls);
        Assert.Equal(7u, agent.Descriptors[0].Options.StaticCacheDays);
        // The second render is the compensation: the options as they were stored, 30 days and all.
        Assert.Equal(30u, agent.Descriptors[1].Options.StaticCacheDays);
        Assert.Equal(SiteOptionsSamples.StoredHash, Assert.Single(agent.Descriptors[1].Options.BasicAuthUsers).PasswordHash);
    }

    /// <summary>The requests cancellation token reaches the agent.</summary>
    [Fact]
    public async Task The_requests_cancellation_token_reaches_the_agent()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);
        using var cancellation = new CancellationTokenSource();

        await Handler(context, account, agent).HandleAsync(Command(siteId), cancellation.Token);

        Assert.Equal(cancellation.Token, Assert.Single(agent.Tokens));
    }

    /// <summary>An upload size on a static site and cache headers on a proxied one are refused before the agent is asked.</summary>
    [Theory]
    [InlineData(SiteBackendType.Static, true)]
    [InlineData(SiteBackendType.ReverseProxy, false)]
    public async Task An_option_the_backend_has_no_place_for_is_refused_before_the_agent_is_asked(
        SiteBackendType backend,
        bool uploadSize)
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = new Maran.Modules.Sites.Domain.Entities.Site(
            Guid.NewGuid(), account, "example.com", [], backend, string.Empty,
            backend == SiteBackendType.ReverseProxy ? "127.0.0.1:8080" : string.Empty,
            "/home/acme/sites/example.com", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        seed.Sites.Add(site);
        await seed.SaveChangesAsync();
        var agent = new RecordingAgentSitesClient();
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, agent).HandleAsync(
            Command(site.Id) with
            {
                ClientMaxBodySizeMib = uploadSize ? 64 : null,
                StaticCacheDays = uploadSize ? null : 7,
            },
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("SiteOptionNotForBackend", result.Error!.Code);
        Assert.Empty(agent.Calls);
    }

    /// <summary>The same options on a PHP site are accepted.</summary>
    [Fact]
    public async Task The_same_options_on_a_php_site_are_accepted()
    {
        var account = Guid.NewGuid();
        var database = Guid.NewGuid().ToString();
        var siteId = await SeedAsync(database, account, "example.com");
        await using var context = SitesTestContext.Create(FakeCurrentUser.Customer(account), database);

        var result = await Handler(context, account, new RecordingAgentSitesClient()).HandleAsync(
            Command(siteId) with { ClientMaxBodySizeMib = 64, StaticCacheDays = 7 },
            CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    /// <summary>Builds a command that states nothing.</summary>
    /// <param name="siteId">The site to replace the options of.</param>
    private static ReplaceSiteOptionsCommand Command(Guid siteId)
    {
        return new ReplaceSiteOptionsCommand(
            siteId, CanonicalHost.None, null, null, [], [], null, [], null, "198.51.100.7", "tests");
    }

    /// <summary>Builds the handler under test.</summary>
    /// <param name="context">The context bound to the calling principal.</param>
    /// <param name="accountId">The account the caller owns.</param>
    /// <param name="agent">The agent double.</param>
    /// <param name="audit">The journal double.</param>
    private static ReplaceSiteOptionsCommandHandler Handler(
        SitesDbContext context,
        Guid accountId,
        RecordingAgentSitesClient agent,
        RecordingAuditWriter? audit = null)
    {
        var accounts = new StubAccountDirectory(new AccountSnapshot(
            accountId,
            "acme",
            MaxSites: 5,
            MaxDatabases: 2,
            MaxSftpUsers: 3,
            MaxCronEntries: 7,
            MaxPhpWorkersPerPool: 10,
            DiskQuotaMb: 1_024, AccountState.Active));
        return new ReplaceSiteOptionsCommandHandler(
            context,
            accounts,
            agent,
            new SiteAuditJournal(audit ?? new RecordingAuditWriter(), FakeCurrentUser.Customer(accountId)),
            SitesTestContext.SiteNames(),
            new RecordingLogger<ReplaceSiteOptionsCommandHandler>());
    }

    /// <summary>Seeds one site and returns its id.</summary>
    /// <param name="database">The shared in-memory database.</param>
    /// <param name="accountId">The owning account.</param>
    /// <param name="domain">The site's primary domain.</param>
    /// <param name="options">Options to store with it, or null for none.</param>
    /// <param name="disabled">Whether the site is disabled.</param>
    /// <param name="aliases">Additional hostnames answered by the same vhost.</param>
    private static async Task<Guid> SeedAsync(
        string database,
        Guid accountId,
        string domain,
        SiteOptionsValues? options = null,
        bool disabled = false,
        string[]? aliases = null)
    {
        await using var seed = SitesTestContext.Create(FakeCurrentUser.Admin(), database);
        var site = SitesTestContext.PhpSite(accountId, domain, "8.3", aliases ?? []);
        if (options is not null)
        {
            site.ReplaceOptions(options);
        }

        if (disabled)
        {
            site.Disable();
        }

        seed.Sites.Add(site);
        await seed.SaveChangesAsync();
        return site.Id;
    }
}
