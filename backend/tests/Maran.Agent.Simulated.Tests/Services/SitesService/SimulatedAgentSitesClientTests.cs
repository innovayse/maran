using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.SitesService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Tests.TestSupport;
using Maran.SharedKernel.Results;

namespace Maran.Agent.Simulated.Tests.Services.SitesService;

/// <summary>The vhost behind a domain: idempotency, ownership, caps and the log tail's contract.</summary>
public sealed class SimulatedAgentSitesClientTests
{
    /// <summary>The domain of the site the tests install into.</summary>
    private const string Domain = "shop.example.test";

    /// <summary>What the site is, as the panel passes it; the simulated agent does not read it.</summary>
    private static readonly SiteDescriptor Descriptor = new(
        [],
        SiteBackendKind.Php,
        "8.3",
        string.Empty,
        SiteNodeApp.None,
        SiteWebServer.Nginx,
        false,
        SiteOptionsDescriptor.None);

    /// <summary>The source of the current moment.</summary>
    private readonly FixedClock _clock = new();

    /// <summary>A new site's files live under the account's home, and its vhost serves content.</summary>
    [Fact]
    public async Task Creating_a_php_site_returns_its_document_root_under_the_account_home()
    {
        var (ledger, client) = NewHost();

        var created = await CreatePhpAsync(client, Domain);

        Assert.True(created.IsSuccess);
        Assert.Equal("/home/acme/sites/shop.example.test", created.Value.DocumentRoot);
        Assert.True(ledger.Vhosts.TryGet(Domain, out var vhost));
        Assert.NotNull(vhost);
        Assert.True(vhost.Enabled);
        Assert.Equal("8.3", vhost.PhpVersion);
        Assert.Equal(5u, vhost.MaxChildren);
    }

    /// <summary>A static site keeps no PHP version, worker budget or overrides, whatever the request carried.</summary>
    [Fact]
    public async Task Creating_a_static_site_ignores_php_and_proxy_values()
    {
        var (ledger, client) = NewHost();

        var created = await client.CreateAsync(
            "acme",
            Domain,
            [],
            SiteBackendKind.Static,
            "8.3",
            "http://127.0.0.1:9000",
            SiteWebServer.Nginx,
            SiteNodeApp.None,
            7,
            [new PhpSettingDto("memory_limit", "256M")],
            CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.True(ledger.Vhosts.TryGet(Domain, out var vhost));
        Assert.NotNull(vhost);
        Assert.Equal(string.Empty, vhost.PhpVersion);
        Assert.Equal(string.Empty, vhost.ProxyUpstream);
        Assert.Equal(0u, vhost.MaxChildren);
        Assert.Empty(vhost.SettingOverrides);
    }

    /// <summary>The same domain twice is a conflict and the first site is untouched.</summary>
    [Fact]
    public async Task Creating_the_same_domain_twice_reports_already_exists()
    {
        var (ledger, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var again = await CreatePhpAsync(client, Domain);

        Assert.Equal("AgentAlreadyExists", again.Error!.Code);
        Assert.Equal(1, ledger.Vhosts.Count);
    }

    /// <summary>A name another vhost already serves as an alias cannot be claimed as a domain.</summary>
    [Fact]
    public async Task A_domain_another_site_serves_as_an_alias_reports_already_exists()
    {
        var (_, client) = NewHost();
        await client.CreateAsync(
            "acme", Domain, ["www.example.test"], SiteBackendKind.Static, string.Empty, string.Empty, SiteWebServer.Nginx, SiteNodeApp.None, 0, [],
            CancellationToken.None);

        var clash = await CreatePhpAsync(client, "www.example.test");

        Assert.Equal("AgentAlreadyExists", clash.Error!.Code);
    }

    /// <summary>An alias another vhost already serves cannot be claimed either.</summary>
    [Fact]
    public async Task An_alias_another_site_serves_reports_already_exists()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var clash = await client.CreateAsync(
            "acme", "blog.example.test", [Domain], SiteBackendKind.Static, string.Empty, string.Empty, SiteWebServer.Nginx, SiteNodeApp.None, 0, [],
            CancellationToken.None);

        Assert.Equal("AgentAlreadyExists", clash.Error!.Code);
    }

    /// <summary>A site for an account the host does not hold is not found, and nothing is stored.</summary>
    [Fact]
    public async Task A_site_for_an_unknown_account_reports_not_found()
    {
        var (ledger, client) = NewHost();

        var created = await client.CreateAsync(
            "ghost", Domain, [], SiteBackendKind.Static, string.Empty, string.Empty, SiteWebServer.Nginx, SiteNodeApp.None, 0, [], CancellationToken.None);

        Assert.Equal("AgentNotFound", created.Error!.Code);
        Assert.Equal(0, ledger.Vhosts.Count);
    }

    /// <summary>Binding to a version the host has not installed is refused as the real agent does.</summary>
    [Fact]
    public async Task A_php_site_on_an_uninstalled_version_reports_validation_failed()
    {
        var (_, client) = NewHost();

        var created = await client.CreateAsync(
            "acme", Domain, [], SiteBackendKind.Php, "7.4", string.Empty, SiteWebServer.Nginx, SiteNodeApp.None, 5, [], CancellationToken.None);

        Assert.Equal("AgentValidationFailed", created.Error!.Code);
    }

    /// <summary>Values outside their alphabet, or over their caps, are refused before anything is stored.</summary>
    /// <param name="domain">The domain in the request.</param>
    /// <param name="kind">The backend in the request.</param>
    /// <param name="upstream">The proxy upstream in the request.</param>
    [Theory]
    [InlineData("Shop.Example.Test", SiteBackendKind.Static, "")]
    [InlineData("shop..example.test", SiteBackendKind.Static, "")]
    [InlineData(".example.test", SiteBackendKind.Static, "")]
    [InlineData("shop.example.test/", SiteBackendKind.Static, "")]
    [InlineData("shop.example.test", SiteBackendKind.ReverseProxy, "")]
    [InlineData("shop.example.test", (SiteBackendKind)99, "")]
    public async Task A_malformed_request_is_refused_as_invalid_input(
        string domain,
        SiteBackendKind kind,
        string upstream)
    {
        var (ledger, client) = NewHost();

        var created = await client.CreateAsync(
            "acme", domain, [], kind, "8.3", upstream, SiteWebServer.Nginx, SiteNodeApp.None, 5, [], CancellationToken.None);

        Assert.Equal("AgentInvalidInput", created.Error!.Code);
        Assert.Equal(0, ledger.Vhosts.Count);
    }

    /// <summary>A Node site is stored when it carries its runtime facts, and refused when any is missing.</summary>
    /// <param name="version">The Node major in the request.</param>
    /// <param name="port">The application port in the request.</param>
    /// <param name="command">The start command in the request.</param>
    /// <param name="accepted">Whether the site is created.</param>
    [Theory]
    [InlineData("22", 3100u, "npm start", true)]
    [InlineData("", 3100u, "npm start", false)]
    [InlineData("22", 0u, "npm start", false)]
    [InlineData("22", 3100u, "", false)]
    public async Task A_node_site_needs_its_version_port_and_start_command(
        string version,
        uint port,
        string command,
        bool accepted)
    {
        var (ledger, client) = NewHost();

        var created = await client.CreateAsync(
            "acme",
            Domain,
            [],
            SiteBackendKind.Node,
            string.Empty,
            string.Empty,
            SiteWebServer.Nginx,
            new SiteNodeApp(version, port, command),
            0,
            [],
            CancellationToken.None);

        Assert.Equal(accepted, created.IsSuccess);
        Assert.Equal(accepted ? 1 : 0, ledger.Vhosts.Count);
    }

    /// <summary>More aliases or overrides than the caps allow are refused, bounding what a site holds.</summary>
    [Fact]
    public async Task More_aliases_or_overrides_than_the_caps_allow_are_refused()
    {
        var limits = new SimulatedAgentLimits { MaxAliasesPerSite = 1, MaxSettingOverridesPerSite = 1 };
        var (_, client) = NewHost(limits);

        var aliases = await client.CreateAsync(
            "acme", Domain, ["a.example.test", "b.example.test"], SiteBackendKind.Static, string.Empty, string.Empty, SiteWebServer.Nginx, SiteNodeApp.None, 0,
            [], CancellationToken.None);
        var overrides = await client.CreateAsync(
            "acme", Domain, [], SiteBackendKind.Php, "8.3", string.Empty, SiteWebServer.Nginx, SiteNodeApp.None, 5,
            [new PhpSettingDto("a", "1"), new PhpSettingDto("b", "2")], CancellationToken.None);

        Assert.Equal("AgentInvalidInput", aliases.Error!.Code);
        Assert.Equal("AgentInvalidInput", overrides.Error!.Code);
    }

    /// <summary>A setting value carrying a control character is not stored.</summary>
    [Fact]
    public async Task A_setting_value_with_a_control_character_is_refused()
    {
        var (_, client) = NewHost();

        var created = await client.CreateAsync(
            "acme", Domain, [], SiteBackendKind.Php, "8.3", string.Empty, SiteWebServer.Nginx, SiteNodeApp.None, 5,
            [new PhpSettingDto("memory_limit", "256M\nauto_prepend_file=x")], CancellationToken.None);

        Assert.Equal("AgentInvalidInput", created.Error!.Code);
    }

    /// <summary>The ledger holds no more sites than its cap.</summary>
    [Fact]
    public async Task Creating_more_sites_than_the_cap_reports_limit_reached()
    {
        var (ledger, client) = NewHost(new SimulatedAgentLimits { MaxSites = 1 });
        await CreatePhpAsync(client, Domain);

        var second = await CreatePhpAsync(client, "other.example.test");

        Assert.Equal("AgentLimitReached", second.Error!.Code);
        Assert.Equal(1, ledger.Vhosts.Count);
    }

    /// <summary>Rebinding moves the site to an installed version and replaces its pool settings.</summary>
    [Fact]
    public async Task Changing_the_php_version_rebinds_the_site_and_replaces_its_pool_settings()
    {
        var (ledger, client) = NewHost();
        ledger.PhpRuntimes.Add("8.2", new SimulatedPhpRuntime("8.2", "/run/maran-php", false, []));
        await CreatePhpAsync(client, Domain);

        var changed = await client.ChangePhpVersionAsync(
            "acme", Domain, "8.2", Descriptor, 9, [new PhpSettingDto("memory_limit", "512M")], true,
            CancellationToken.None);

        Assert.True(changed.IsSuccess);
        Assert.True(ledger.Vhosts.TryGet(Domain, out var vhost));
        Assert.NotNull(vhost);
        Assert.Equal("8.2", vhost.PhpVersion);
        Assert.Equal(9u, vhost.MaxChildren);
        Assert.Equal("memory_limit", Assert.Single(vhost.SettingOverrides).Name);
    }

    /// <summary>A site without PHP has no version to change, and an uninstalled version is refused.</summary>
    [Fact]
    public async Task Changing_the_php_version_of_a_static_site_or_to_an_uninstalled_one_is_refused()
    {
        var (_, client) = NewHost();
        await client.CreateAsync(
            "acme", Domain, [], SiteBackendKind.Static, string.Empty, string.Empty, SiteWebServer.Nginx, SiteNodeApp.None, 0, [], CancellationToken.None);
        await CreatePhpAsync(client, "php.example.test");

        var staticSite = await client.ChangePhpVersionAsync(
            "acme", Domain, "8.3", Descriptor, 5, [], false, CancellationToken.None);
        var uninstalled = await client.ChangePhpVersionAsync(
            "acme", "php.example.test", "5.6", Descriptor, 5, [], false, CancellationToken.None);

        Assert.Equal("AgentValidationFailed", staticSite.Error!.Code);
        Assert.Equal("AgentValidationFailed", uninstalled.Error!.Code);
    }

    /// <summary>A site is looked up by domain AND account, so one account cannot touch another's site.</summary>
    [Fact]
    public async Task Another_accounts_site_is_not_found()
    {
        var (ledger, client) = NewHost();
        SimulatedHosts.AddAccount(ledger, "globex", _clock.UtcNow);
        await CreatePhpAsync(client, Domain);

        var changed = await client.ChangePhpVersionAsync(
            "globex", Domain, "8.3", Descriptor, 5, [], false, CancellationToken.None);
        var disabled = await client.DisableAsync("globex", Domain, Descriptor, CancellationToken.None);
        var deleted = await client.DeleteAsync("globex", Domain, string.Empty, SiteWebServer.Nginx, CancellationToken.None);

        Assert.Equal("AgentNotFound", changed.Error!.Code);
        Assert.Equal("AgentNotFound", disabled.Error!.Code);
        Assert.Equal("AgentNotFound", deleted.Error!.Code);
        Assert.True(ledger.Vhosts.Contains(Domain));
    }

    /// <summary>Disabling serves the suspension page and enabling restores the content; both may be repeated.</summary>
    [Fact]
    public async Task Disabling_and_enabling_a_site_toggles_it_and_may_be_repeated()
    {
        var (ledger, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        await client.DisableAsync("acme", Domain, Descriptor, CancellationToken.None);
        var disabledAgain = await client.DisableAsync("acme", Domain, Descriptor, CancellationToken.None);
        Assert.True(ledger.Vhosts.TryGet(Domain, out var disabled));
        Assert.NotNull(disabled);
        var wasDisabled = !disabled.Enabled;
        await client.EnableAsync("acme", Domain, Descriptor, CancellationToken.None);
        Assert.True(ledger.Vhosts.TryGet(Domain, out var enabled));
        Assert.NotNull(enabled);

        Assert.True(disabledAgain.IsSuccess);
        Assert.True(wasDisabled);
        Assert.True(enabled.Enabled);
    }

    /// <summary>Enabling or disabling a site that is not there reports not found.</summary>
    [Fact]
    public async Task Enabling_or_disabling_an_unknown_site_reports_not_found()
    {
        var (_, client) = NewHost();

        var enabled = await client.EnableAsync("acme", Domain, Descriptor, CancellationToken.None);
        var disabled = await client.DisableAsync("acme", Domain, Descriptor, CancellationToken.None);

        Assert.Equal("AgentNotFound", enabled.Error!.Code);
        Assert.Equal("AgentNotFound", disabled.Error!.Code);
    }

    /// <summary>Deleting removes the vhost; the certificate record is left to the certificate client.</summary>
    [Fact]
    public async Task Deleting_a_site_removes_its_vhost_and_leaves_its_certificate()
    {
        var (ledger, client) = NewHost();
        await CreatePhpAsync(client, Domain);
        var certificate = new SimulatedCertificate("acme", Domain, _clock.UtcNow.AddDays(30), _clock.UtcNow);
        ledger.Certificates.Add(Domain, certificate);

        var deleted = await client.DeleteAsync("acme", Domain, "8.3", SiteWebServer.Nginx, CancellationToken.None);
        var again = await client.DeleteAsync("acme", Domain, "8.3", SiteWebServer.Nginx, CancellationToken.None);

        Assert.True(deleted.IsSuccess);
        Assert.False(ledger.Vhosts.Contains(Domain));
        Assert.True(ledger.Certificates.Contains(Domain));
        Assert.Equal("AgentNotFound", again.Error!.Code);
    }

    /// <summary>A retired version that is not a version is refused.</summary>
    [Fact]
    public async Task Deleting_with_a_malformed_retired_version_is_refused()
    {
        var (ledger, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var deleted = await client.DeleteAsync("acme", Domain, "eight", SiteWebServer.Nginx, CancellationToken.None);

        Assert.Equal("AgentInvalidInput", deleted.Error!.Code);
        Assert.True(ledger.Vhosts.Contains(Domain));
    }

    /// <summary>There is no web server, so reloading it always succeeds.</summary>
    [Fact]
    public async Task Reloading_the_web_server_succeeds()
    {
        var (_, client) = NewHost();

        Assert.True((await client.ReloadWebServerAsync(SiteWebServer.Nginx, CancellationToken.None)).IsSuccess);
    }

    /// <summary>The tail replays the requested history, oldest first, and ends with one completed event.</summary>
    [Fact]
    public async Task Tailing_replays_the_requested_history_then_completes()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var events = await AsyncStreams.CollectAsync(
            client.TailLogAsync("acme", Domain, SiteLogSource.Access, 5, CancellationToken.None));

        Assert.Equal(6, events.Count);
        Assert.All(events.Take(5), tailed => { Assert.True(Assert.IsType<SiteLogLine>(tailed).Historical); });
        Assert.Equal(SiteLogEndKind.Completed, Assert.IsType<SiteLogEnded>(events[5]).Kind);
    }

    /// <summary>The newest line is stamped with the injected clock and older lines are earlier.</summary>
    [Fact]
    public async Task Tailed_lines_are_stamped_from_the_injected_clock_oldest_first()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var events = await AsyncStreams.CollectAsync(
            client.TailLogAsync("acme", Domain, SiteLogSource.Access, 2, CancellationToken.None));

        Assert.Contains("07/Nov/2026:08:59:00", Assert.IsType<SiteLogLine>(events[0]).Line, StringComparison.Ordinal);
        Assert.Contains("07/Nov/2026:09:00:00", Assert.IsType<SiteLogLine>(events[1]).Line, StringComparison.Ordinal);
    }

    /// <summary>A caller cannot make the agent replay more than its cap, whatever it asks for.</summary>
    [Fact]
    public async Task The_replay_is_capped_whatever_the_caller_asks_for()
    {
        var (_, client) = NewHost(new SimulatedAgentLimits { MaxLogHistoryLines = 10 });
        await CreatePhpAsync(client, Domain);

        var events = await AsyncStreams.CollectAsync(
            client.TailLogAsync("acme", Domain, SiteLogSource.Error, uint.MaxValue, CancellationToken.None));

        Assert.Equal(11, events.Count);
    }

    /// <summary>Asking for no history ends the stream at once, still with a terminal event.</summary>
    [Fact]
    public async Task Tailing_with_no_history_ends_with_only_the_terminal_event()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var events = await AsyncStreams.CollectAsync(
            client.TailLogAsync("acme", Domain, SiteLogSource.Access, 0, CancellationToken.None));

        Assert.Equal(SiteLogEndKind.Completed, Assert.IsType<SiteLogEnded>(Assert.Single(events)).Kind);
    }

    /// <summary>The two logs are different logs.</summary>
    [Fact]
    public async Task The_access_log_and_the_error_log_differ()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var access = await AsyncStreams.CollectAsync(
            client.TailLogAsync("acme", Domain, SiteLogSource.Access, 1, CancellationToken.None));
        var error = await AsyncStreams.CollectAsync(
            client.TailLogAsync("acme", Domain, SiteLogSource.Error, 1, CancellationToken.None));

        Assert.NotEqual(Assert.IsType<SiteLogLine>(access[0]).Line, Assert.IsType<SiteLogLine>(error[0]).Line);
    }

    /// <summary>A log that is not there ends the stream with a failed event naming it, never an exception.</summary>
    [Fact]
    public async Task Tailing_an_unknown_site_ends_with_a_failed_event()
    {
        var (_, client) = NewHost();

        var events = await AsyncStreams.CollectAsync(
            client.TailLogAsync("acme", Domain, SiteLogSource.Access, 5, CancellationToken.None));

        Assert.Equal("AgentNotFound", Assert.IsType<SiteLogFailed>(Assert.Single(events)).ErrorCode);
    }

    /// <summary>A caller that stopped watching is told so, not given an ending that looks natural.</summary>
    [Fact]
    public async Task A_cancelled_tail_ends_with_a_cancelled_event()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var events = await AsyncStreams.CollectAsync(
            client.TailLogAsync("acme", Domain, SiteLogSource.Access, 5, cancelled.Token));

        Assert.Equal(SiteLogEndKind.Cancelled, Assert.IsType<SiteLogEnded>(Assert.Single(events)).Kind);
    }

    /// <summary>A read with no cursor covers the default window and hands back a cursor at the present.</summary>
    [Fact]
    public async Task Summarising_without_a_cursor_reads_the_default_window_and_returns_a_cursor_at_now()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var reading = (await client.SummariseTrafficAsync("acme", Domain, null, CancellationToken.None)).Value;

        Assert.NotEmpty(reading.Days);
        Assert.True(reading.CaughtUp);
        Assert.False(reading.Gap);
        Assert.Equal(0u, reading.UnparsedLines);
        Assert.Equal(SimulatedSiteTraffic.AccessLogFileId, reading.Cursor.FileId);
        Assert.Equal((ulong)_clock.UtcNow.ToUnixTimeSeconds(), reading.Cursor.Offset);
    }

    /// <summary>Reading again from the cursor just returned finds nothing new.</summary>
    [Fact]
    public async Task Summarising_from_the_returned_cursor_at_the_same_moment_finds_nothing_new()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);
        var first = (await client.SummariseTrafficAsync("acme", Domain, null, CancellationToken.None)).Value;

        var second = (await client.SummariseTrafficAsync("acme", Domain, first.Cursor, CancellationToken.None)).Value;

        Assert.Empty(second.Days);
        Assert.Equal(first.Cursor, second.Cursor);
    }

    /// <summary>Reading in two steps and in one count the same requests, so adding deltas does not drift.</summary>
    [Fact]
    public async Task Summarising_in_two_steps_counts_the_same_requests_as_in_one()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);
        var start = (await client.SummariseTrafficAsync("acme", Domain, null, CancellationToken.None)).Value;
        _clock.UtcNow = _clock.UtcNow.AddHours(7);
        var middle = (await client.SummariseTrafficAsync("acme", Domain, start.Cursor, CancellationToken.None)).Value;
        _clock.UtcNow = _clock.UtcNow.AddHours(9);
        var end = (await client.SummariseTrafficAsync("acme", Domain, middle.Cursor, CancellationToken.None)).Value;
        var whole = (await client.SummariseTrafficAsync("acme", Domain, start.Cursor, CancellationToken.None)).Value;

        Assert.Equal(Requests(whole), Requests(middle) + Requests(end));
    }

    /// <summary>Within one read the status codes and the paths add up to the requests counted.</summary>
    [Fact]
    public async Task Statuses_and_paths_add_up_to_the_requests_of_each_day()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var reading = (await client.SummariseTrafficAsync("acme", Domain, null, CancellationToken.None)).Value;

        Assert.All(
            reading.Days,
            day =>
            {
                Assert.Equal(
                    day.Requests,
                    day.Statuses.Aggregate(0UL, (sum, status) => { return sum + status.Requests; }));
                Assert.Equal(
                    day.Requests,
                    day.Paths.Aggregate(day.OtherPathRequests, (sum, path) => { return sum + path.Requests; }));
                Assert.True(day.BodyBytes > 0);
            });
    }

    /// <summary>A cursor for a file that was rotated away, or older than the window, is reported as a gap.</summary>
    [Fact]
    public async Task A_foreign_or_too_old_cursor_is_reported_as_a_gap()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);
        var ancient = new SiteTrafficCursor(SimulatedSiteTraffic.AccessLogFileId, 1);

        var foreign = (await client.SummariseTrafficAsync(
            "acme", Domain, new SiteTrafficCursor("other.log", 5), CancellationToken.None)).Value;
        var old = (await client.SummariseTrafficAsync("acme", Domain, ancient, CancellationToken.None)).Value;

        Assert.True(foreign.Gap);
        Assert.True(old.Gap);
        Assert.True(old.Days.Count <= 15);
    }

    /// <summary>The same site read at the same moment gives the same figures.</summary>
    [Fact]
    public async Task Traffic_is_the_same_on_every_read_at_the_same_moment()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var first = (await client.SummariseTrafficAsync("acme", Domain, null, CancellationToken.None)).Value;
        var second = (await client.SummariseTrafficAsync("acme", Domain, null, CancellationToken.None)).Value;

        Assert.Equal(Requests(first), Requests(second));
        Assert.Equal(first.Days.Select(day => { return day.Date; }), second.Days.Select(day => { return day.Date; }));
    }

    /// <summary>A site that is not there, or belongs to another account, has no traffic to read.</summary>
    [Fact]
    public async Task Summarising_an_unknown_or_foreign_site_reports_not_found()
    {
        var (ledger, client) = NewHost();
        SimulatedHosts.AddAccount(ledger, "globex", _clock.UtcNow);
        await CreatePhpAsync(client, Domain);

        var unknown = await client.SummariseTrafficAsync("acme", "none.example.test", null, CancellationToken.None);
        var foreign = await client.SummariseTrafficAsync("globex", Domain, null, CancellationToken.None);

        Assert.Equal("AgentNotFound", unknown.Error!.Code);
        Assert.Equal("AgentNotFound", foreign.Error!.Code);
    }

    /// <summary>Pruning removes the surplus archives, both logs' worth, and a repeat removes nothing more.</summary>
    [Fact]
    public async Task Pruning_removes_the_surplus_archives_once()
    {
        var (_, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var first = await client.PruneLogsAsync("acme", Domain, 4, CancellationToken.None);
        var second = await client.PruneLogsAsync("acme", Domain, 4, CancellationToken.None);
        var wider = await client.PruneLogsAsync("acme", Domain, 14, CancellationToken.None);

        Assert.Equal(20u, first.Value.RemovedFiles);
        Assert.Equal(0u, second.Value.RemovedFiles);
        Assert.Equal(0u, wider.Value.RemovedFiles);
    }

    /// <summary>A retention outside one to fourteen rotations is refused and nothing is pruned.</summary>
    /// <param name="retain">The refused retention.</param>
    [Theory]
    [InlineData(0u)]
    [InlineData(15u)]
    public async Task Pruning_with_a_retention_outside_the_bounds_is_refused(uint retain)
    {
        var (ledger, client) = NewHost();
        await CreatePhpAsync(client, Domain);

        var pruned = await client.PruneLogsAsync("acme", Domain, retain, CancellationToken.None);

        Assert.Equal("AgentInvalidInput", pruned.Error!.Code);
        Assert.True(ledger.Vhosts.TryGet(Domain, out var vhost));
        Assert.NotNull(vhost);
        Assert.Equal(14u, vhost.RotatedLogs);
    }

    /// <summary>A site that is not there, or belongs to another account, has no archives to prune.</summary>
    [Fact]
    public async Task Pruning_an_unknown_site_reports_not_found()
    {
        var (_, client) = NewHost();

        var pruned = await client.PruneLogsAsync("acme", Domain, 4, CancellationToken.None);

        Assert.Equal("AgentNotFound", pruned.Error!.Code);
    }

    /// <summary>Builds a host with one account and a client over it.</summary>
    /// <param name="limits">The caps to build it with; the defaults when null.</param>
    /// <returns>The ledger and the client.</returns>
    private (SimulatedAgentLedger Ledger, SimulatedAgentSitesClient Client) NewHost(SimulatedAgentLimits? limits = null)
    {
        var ledger = SimulatedHosts.NewLedger(limits);
        SimulatedHosts.AddAccount(ledger, SimulatedHosts.AccountName, _clock.UtcNow);

        return (ledger, new SimulatedAgentSitesClient(ledger, _clock));
    }

    /// <summary>Adds up the requests of every day of a reading.</summary>
    /// <param name="reading">The reading.</param>
    /// <returns>The total.</returns>
    private static ulong Requests(SiteTrafficReading reading)
    {
        return reading.Days.Aggregate(0UL, (sum, day) => { return sum + day.Requests; });
    }

    /// <summary>Creates a PHP site on version 8.3 with a worker budget of five.</summary>
    /// <param name="client">The client to call.</param>
    /// <param name="domain">The primary domain.</param>
    /// <returns>The client's answer.</returns>
    private static Task<Result<CreatedSiteDto>> CreatePhpAsync(SimulatedAgentSitesClient client, string domain)
    {
        return client.CreateAsync(
            "acme", domain, [], SiteBackendKind.Php, "8.3", string.Empty, SiteWebServer.Nginx, SiteNodeApp.None, 5, [], CancellationToken.None);
    }
}
