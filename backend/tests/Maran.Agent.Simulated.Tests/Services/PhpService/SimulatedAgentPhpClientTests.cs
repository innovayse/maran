using Maran.Agent.Client.Services.PhpService;
using Maran.Agent.Client.Services.SitesService;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Services.PhpService;
using Maran.Agent.Simulated.State;
using Maran.Agent.Simulated.Tests.TestSupport;

namespace Maran.Agent.Simulated.Tests.Services.PhpService;

/// <summary>The host's PHP runtimes: what is installed, what can be, and the install stream's contract.</summary>
public sealed class SimulatedAgentPhpClientTests
{
    /// <summary>A fresh host has the baseline version installed and marked as the default.</summary>
    [Fact]
    public async Task A_fresh_host_lists_the_baseline_version_as_the_default()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var versions = await client.ListVersionsAsync(CancellationToken.None);

        var only = Assert.Single(versions.Value);
        Assert.Equal("8.3", only.Version);
        Assert.Equal(AgentPhpDefaultStatus.Default, only.DefaultStatus);
        Assert.False(string.IsNullOrEmpty(only.FpmSocketDirectory));
    }

    /// <summary>The installable list flags what is already installed.</summary>
    [Fact]
    public async Task The_installable_list_flags_the_installed_versions()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var versions = (await client.ListInstallableVersionsAsync(CancellationToken.None)).Value;

        Assert.True(versions.Single(version => { return version.Version == "8.3"; }).Installed);
        Assert.False(versions.Single(version => { return version.Version == "8.4"; }).Installed);
        Assert.Equal("8.4", versions[0].Version);
    }

    /// <summary>An install reports progress that never goes backward and ends with the version installed.</summary>
    [Fact]
    public async Task Installing_a_version_reports_progress_then_installs_it()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var events = await AsyncStreams.CollectAsync(client.InstallVersionAsync("8.2", CancellationToken.None));

        var terminal = events[^1];
        Assert.Equal(PhpInstallEventKind.Installed, terminal.Kind);
        Assert.Equal("8.2", terminal.Version);
        Assert.Equal(100u, terminal.Percent);
        var progress = events.Take(events.Count - 1).ToList();
        Assert.NotEmpty(progress);
        Assert.All(progress, step => { Assert.Equal(PhpInstallEventKind.Progress, step.Kind); });
        var percents = progress.Select(step => { return step.Percent; }).ToList();
        Assert.Equal(percents.Order().ToList(), percents);
        var listed = (await client.ListVersionsAsync(CancellationToken.None)).Value;
        string[] expectedOrder = ["8.3", "8.2"];
        Assert.Equal(expectedOrder, listed.Select(version => { return version.Version; }));
        Assert.Equal(AgentPhpDefaultStatus.NotDefault, listed[1].DefaultStatus);
    }

    /// <summary>Installing what is already installed ends in the same outcome without doing the work again.</summary>
    [Fact]
    public async Task Installing_an_installed_version_ends_at_once_with_the_same_outcome()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var events = await AsyncStreams.CollectAsync(client.InstallVersionAsync("8.3", CancellationToken.None));

        var only = Assert.Single(events);
        Assert.Equal(PhpInstallEventKind.Installed, only.Kind);
        Assert.Equal("8.3", only.Version);
    }

    /// <summary>A version the repository does not offer ends in one failed event and installs nothing.</summary>
    [Fact]
    public async Task Installing_a_version_the_repository_does_not_offer_fails()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = new SimulatedAgentPhpClient(ledger);

        var events = await AsyncStreams.CollectAsync(client.InstallVersionAsync("9.9", CancellationToken.None));

        var only = Assert.Single(events);
        Assert.Equal(PhpInstallEventKind.Failed, only.Kind);
        Assert.Equal("AgentValidationFailed", only.Error!.Code);
        Assert.Equal(1, ledger.PhpRuntimes.Count);
    }

    /// <summary>Something that is not a version is refused as invalid input.</summary>
    /// <param name="version">The refused text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("8")]
    [InlineData("8.3.1")]
    [InlineData("eight.three")]
    [InlineData("../8.3")]
    public async Task Installing_something_that_is_not_a_version_fails_as_invalid_input(string version)
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var events = await AsyncStreams.CollectAsync(client.InstallVersionAsync(version, CancellationToken.None));

        var only = Assert.Single(events);
        Assert.Equal(PhpInstallEventKind.Failed, only.Kind);
        Assert.Equal("AgentInvalidInput", only.Error!.Code);
    }

    /// <summary>A caller that stopped watching is told so, and the version is not installed.</summary>
    [Fact]
    public async Task A_cancelled_install_ends_cancelled_and_installs_nothing()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = new SimulatedAgentPhpClient(ledger);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var events = await AsyncStreams.CollectAsync(client.InstallVersionAsync("8.2", cancelled.Token));

        Assert.Equal(PhpInstallEventKind.Cancelled, Assert.Single(events).Kind);
        Assert.False(ledger.PhpRuntimes.Contains("8.2"));
    }

    /// <summary>The ledger holds no more runtimes than its cap; the install ends with the limit code.</summary>
    [Fact]
    public async Task Installing_past_the_cap_ends_with_a_limit_failure()
    {
        var ledger = SimulatedHosts.NewLedger(new SimulatedAgentLimits { MaxPhpRuntimes = 1 });
        var client = new SimulatedAgentPhpClient(ledger);

        var events = await AsyncStreams.CollectAsync(client.InstallVersionAsync("8.2", CancellationToken.None));

        var terminal = events[^1];
        Assert.Equal(PhpInstallEventKind.Failed, terminal.Kind);
        Assert.Equal("AgentLimitReached", terminal.Error!.Code);
        Assert.False(ledger.PhpRuntimes.Contains("8.2"));
    }

    /// <summary>A version lists the whole catalogue alphabetically, with the default set loaded.</summary>
    [Fact]
    public async Task Listing_extensions_shows_the_catalogue_with_the_default_set_loaded()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var extensions = (await client.ListExtensionsAsync("8.3", CancellationToken.None)).Value;

        var loaded = extensions
            .Where(extension => { return extension.Enabled; })
            .Select(extension => { return extension.Name; });
        Assert.Equal(SimulatedPhpCatalog.Extensions, extensions.Select(extension => { return extension.Name; }));
        Assert.Equal(SimulatedPhpCatalog.DefaultEnabledExtensions, loaded);
        Assert.All(
            extensions,
            extension => { Assert.Equal(PhpExtensionAvailability.Available, extension.Availability); });
    }

    /// <summary>Extensions of an uninstalled version, or of something that is not a version, are refused.</summary>
    [Fact]
    public async Task Listing_extensions_of_an_uninstalled_or_malformed_version_is_refused()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var uninstalled = await client.ListExtensionsAsync("7.4", CancellationToken.None);
        var malformed = await client.ListExtensionsAsync("seven", CancellationToken.None);

        Assert.Equal("AgentValidationFailed", uninstalled.Error!.Code);
        Assert.Equal("AgentInvalidInput", malformed.Error!.Code);
    }

    /// <summary>Switching an extension reports progress, ends changed, and the list then shows it.</summary>
    [Fact]
    public async Task Switching_an_extension_on_reports_progress_then_changes_it()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var events = await AsyncStreams.CollectAsync(
            client.SetExtensionAsync("8.3", "redis", true, CancellationToken.None));
        var listed = (await client.ListExtensionsAsync("8.3", CancellationToken.None)).Value;

        var terminal = events[^1];
        Assert.Equal(PhpExtensionEventKind.Changed, terminal.Kind);
        Assert.Equal("redis", terminal.Extension);
        Assert.True(terminal.Enabled);
        Assert.All(
            events.Take(events.Count - 1),
            step => { Assert.Equal(PhpExtensionEventKind.Progress, step.Kind); });
        Assert.True(listed.Single(extension => { return extension.Name == "redis"; }).Enabled);
    }

    /// <summary>Switching an extension off, and switching it to the state it is in, both end changed.</summary>
    [Fact]
    public async Task Switching_an_extension_off_works_and_repeating_a_switch_is_a_success()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        await AsyncStreams.CollectAsync(client.SetExtensionAsync("8.3", "gd", false, CancellationToken.None));
        var again = await AsyncStreams.CollectAsync(
            client.SetExtensionAsync("8.3", "gd", false, CancellationToken.None));
        var listed = (await client.ListExtensionsAsync("8.3", CancellationToken.None)).Value;

        Assert.Equal(PhpExtensionEventKind.Changed, again[^1].Kind);
        Assert.False(listed.Single(extension => { return extension.Name == "gd"; }).Enabled);
    }

    /// <summary>An extension is switched per version, so another version is unaffected.</summary>
    [Fact]
    public async Task An_extension_switch_changes_only_its_own_version()
    {
        var ledger = SimulatedHosts.NewLedger();
        var client = new SimulatedAgentPhpClient(ledger);
        await AsyncStreams.CollectAsync(client.InstallVersionAsync("8.2", CancellationToken.None));

        await AsyncStreams.CollectAsync(client.SetExtensionAsync("8.2", "redis", true, CancellationToken.None));
        var other = (await client.ListExtensionsAsync("8.3", CancellationToken.None)).Value;

        Assert.False(other.Single(extension => { return extension.Name == "redis"; }).Enabled);
    }

    /// <summary>An unknown extension, a malformed version or an uninstalled one ends in one failed event.</summary>
    /// <param name="version">The version in the request.</param>
    /// <param name="extension">The extension in the request.</param>
    /// <param name="code">The code the failure carries.</param>
    [Theory]
    [InlineData("8.3", "not_an_extension", "AgentInvalidInput")]
    [InlineData("eight", "redis", "AgentInvalidInput")]
    [InlineData("7.4", "redis", "AgentValidationFailed")]
    public async Task A_refused_extension_switch_ends_in_one_failed_event(string version, string extension, string code)
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var events = await AsyncStreams.CollectAsync(
            client.SetExtensionAsync(version, extension, true, CancellationToken.None));

        var only = Assert.Single(events);
        Assert.Equal(PhpExtensionEventKind.Failed, only.Kind);
        Assert.Equal(code, only.Error!.Code);
    }

    /// <summary>A caller that stopped watching is told so, and the extension is not switched.</summary>
    [Fact]
    public async Task A_cancelled_extension_switch_ends_cancelled_and_changes_nothing()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var events = await AsyncStreams.CollectAsync(client.SetExtensionAsync("8.3", "redis", true, cancelled.Token));
        var listed = (await client.ListExtensionsAsync("8.3", CancellationToken.None)).Value;

        Assert.Equal(PhpExtensionEventKind.Cancelled, Assert.Single(events).Kind);
        Assert.False(listed.Single(extension => { return extension.Name == "redis"; }).Enabled);
    }

    /// <summary>Applying settings rewrites the pool of every one of the account's sites on that version.</summary>
    [Fact]
    public async Task Applying_pool_settings_updates_the_accounts_php_sites_on_that_version()
    {
        var ledger = SimulatedHosts.NewLedger();
        SimulatedHosts.AddAccount(ledger, "acme", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        ledger.Vhosts.Add("a.example.test", PhpVhost("acme", "a.example.test", "8.3"));
        ledger.Vhosts.Add("b.example.test", PhpVhost("acme", "b.example.test", "8.3"));
        var client = new SimulatedAgentPhpClient(ledger);

        var applied = await client.ApplyPoolSettingsAsync(
            "acme", "8.3", 12, [new PhpSettingDto("memory_limit", "512M")], CancellationToken.None);

        Assert.True(applied.IsSuccess);
        foreach (var domain in new[] { "a.example.test", "b.example.test" })
        {
            Assert.True(ledger.Vhosts.TryGet(domain, out var vhost));
            Assert.NotNull(vhost);
            Assert.Equal(12u, vhost.MaxChildren);
            Assert.Equal("memory_limit", Assert.Single(vhost.SettingOverrides).Name);
        }
    }

    /// <summary>The call never creates a pool: an account with no site on the version has none to rewrite.</summary>
    [Fact]
    public async Task Applying_pool_settings_where_the_account_has_no_pool_reports_not_found()
    {
        var ledger = SimulatedHosts.NewLedger();
        SimulatedHosts.AddAccount(ledger, "acme", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        ledger.Vhosts.Add("a.example.test", PhpVhost("acme", "a.example.test", "8.2"));
        var client = new SimulatedAgentPhpClient(ledger);

        var applied = await client.ApplyPoolSettingsAsync("acme", "8.3", 12, [], CancellationToken.None);
        var foreign = await client.ApplyPoolSettingsAsync("globex", "8.2", 12, [], CancellationToken.None);

        Assert.Equal("AgentNotFound", applied.Error!.Code);
        Assert.Equal("AgentNotFound", foreign.Error!.Code);
    }

    /// <summary>A setting with a control character is refused, so a pool cannot be handed a second directive.</summary>
    [Fact]
    public async Task Applying_a_setting_with_a_control_character_is_refused()
    {
        var client = new SimulatedAgentPhpClient(SimulatedHosts.NewLedger());

        var applied = await client.ApplyPoolSettingsAsync(
            "acme", "8.3", 5, [new PhpSettingDto("memory_limit", "1M\nx=y")], CancellationToken.None);

        Assert.Equal("AgentInvalidInput", applied.Error!.Code);
    }

    /// <summary>Versions sort as numbers, so 8.10 is newer than 8.9.</summary>
    [Fact]
    public void Versions_are_ordered_numerically_newest_first()
    {
        var ordered = SimulatedPhpCatalog.NewestFirst(["8.9", "7.4", "8.10", "8.1"]);

        string[] expected = ["8.10", "8.9", "8.1", "7.4"];
        Assert.Equal(expected, ordered);
    }

    /// <summary>The catalog is itself newest first.</summary>
    [Fact]
    public void The_catalog_is_listed_newest_first()
    {
        Assert.Equal(SimulatedPhpCatalog.NewestFirst(SimulatedPhpCatalog.Installable), SimulatedPhpCatalog.Installable);
    }

    /// <summary>Builds a PHP site record bound to <paramref name="version"/>.</summary>
    /// <param name="account">The owning account.</param>
    /// <param name="domain">The primary domain.</param>
    /// <param name="version">The PHP version it is bound to.</param>
    /// <returns>The record.</returns>
    private static SimulatedVhost PhpVhost(string account, string domain, string version)
    {
        return new SimulatedVhost(
            account,
            domain,
            [],
            SiteBackendKind.Php,
            version,
            string.Empty,
            5,
            [],
            true,
            "/home/" + account + "/sites/" + domain);
    }
}
