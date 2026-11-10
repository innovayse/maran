using System.Net;
using Maran.Host.IntegrationTests.Fixtures;

namespace Maran.Host.IntegrationTests.Marketplace;

/// <summary>
/// The licence-lease credential and the download grant never leave the panel's outbound request: after a
/// whole administrator session of browsing, checking for updates and installing, no panel response, no
/// log line written through the container and no audit entry carries either - while the marketplace's own
/// record of what it received proves both were really sent.
/// </summary>
/// <remarks>
/// <para>
/// The secrets are planted values (<see cref="MarketplaceWorld.LeaseToken"/> on the lease authorizer,
/// <see cref="FakeMarketplace.GrantId"/> and <see cref="FakeMarketplace.DownloadSegment"/> in the signed
/// grant), so "appears nowhere" is a text search for a string that is known to exist. Two positive controls
/// keep the search honest: the fake marketplace saw the lease on the wire and fetched the package from the
/// grant's address, and a line the test itself writes through the container's logger factory is found by
/// the same scan of the captured log.
/// </para>
/// <para>
/// UNOBSERVED HERE: a log line written by a logger that does not come from the container, and the
/// content of the panel process's memory or core dumps.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class MarketplaceSecretsEndpointTests : IAsyncLifetime
{
    /// <summary>The five members the status answer carries and no others: none can hold a credential.</summary>
    private static readonly string[] StatusMembers =
    [
        "enabled",
        "host",
        "hostConfigured",
        "restartRequiredModules",
        "sentItems",
    ];

    /// <summary>The planted secrets the panel must never repeat.</summary>
    private static readonly string[] Secrets =
    [
        MarketplaceWorld.LeaseToken,
        FakeMarketplace.GrantId,
        FakeMarketplace.DownloadSegment,
    ];

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public MarketplaceSecretsEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// The status an administrator reads has exactly the documented members, and neither it nor any other
    /// answer, log line or audit entry of a session that browsed, checked for updates and installed carries
    /// the lease token, the grant id or the grant's address - although the marketplace received the lease on
    /// the grant request and the package from the grant's address.
    /// </summary>
    [Fact]
    public async Task Neither_the_lease_nor_the_grant_appears_in_any_panel_response_log_line_or_audit_entry()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        using var administrator = await world.SignInAsync("admin");
        await MarketplaceWorld.EnableAsync(administrator);

        var browsed = new[]
        {
            await administrator.GetAsync(MarketplaceWorld.ApiRoot + "status"),
            await administrator.GetAsync(MarketplaceWorld.ApiRoot + "catalogue"),
            await administrator.GetAsync(MarketplaceWorld.ApiRoot + "modules/" + MarketplaceWorld.ModuleId),
            await administrator.GetAsync(MarketplaceWorld.ApiRoot + "installed"),
            await administrator.GetAsync(MarketplaceWorld.ApiRoot + "updates"),
        };
        var install = await administrator.SendAsync(
            HttpMethod.Post,
            MarketplaceWorld.ApiRoot + "modules/" + MarketplaceWorld.ModuleId + "/install",
            new { version = MarketplaceWorld.ModuleVersion, acknowledged = true, password = EndpointHarness.Password });
        Assert.Equal(HttpStatusCode.Accepted, install.Status);
        await world.SettledTaskAsync(install.Json().GetProperty("taskId").GetGuid());
        var status = await administrator.GetAsync(MarketplaceWorld.ApiRoot + "status");
        var tasks = await administrator.GetAsync("/api/v1/tasks?pageSize=100");
        var audit = await administrator.GetAsync("/api/v1/audit?pageSize=100");

        Assert.All(browsed, answer => { Assert.Equal(HttpStatusCode.OK, answer.Status); });
        Assert.Equal(HttpStatusCode.OK, tasks.Status);
        Assert.Equal(HttpStatusCode.OK, audit.Status);
        var members = status.Json().EnumerateObject().Select(member => { return member.Name; }).Order().ToArray();
        Assert.Equal(StatusMembers.Order().ToArray(), members);

        // Positive controls: the secrets really travelled, so their absence below is a finding.
        var wire = world.Marketplace.Requests;
        var bearer = "Bearer " + MarketplaceWorld.LeaseToken;
        Assert.Contains(wire, request => { return request.Authorization == bearer; });
        Assert.Contains(wire, request => { return Carries(request.Path, FakeMarketplace.DownloadSegment); });
        Assert.Contains(wire, request => { return Carries(request.Path, "/download-grant"); });

        foreach (var secret in Secrets)
        {
            Assert.DoesNotContain(administrator.Texts, text => { return Carries(text, secret); });
            Assert.DoesNotContain(world.Logs.Lines, line => { return Carries(line, secret); });
        }

        Assert.True(audit.Json().GetProperty("items").GetArrayLength() > 0);
    }

    /// <summary>
    /// The positive control of the log scan: a line the test writes through the container's logger factory,
    /// carrying the planted lease token, is found by the very search that finds nothing above. A capture
    /// that saw no lines at all would fail here instead of passing there.
    /// </summary>
    [Fact]
    public async Task A_planted_secret_in_a_log_line_written_through_the_container_is_found_by_the_scan()
    {
        await using var world = await MarketplaceWorld.BootAsync(_pg);
        Assert.DoesNotContain(world.Logs.Lines, line => { return Carries(line, MarketplaceWorld.LeaseToken); });

        world.LogThroughTheContainer("planted " + MarketplaceWorld.LeaseToken);

        Assert.Contains(world.Logs.Lines, line => { return Carries(line, MarketplaceWorld.LeaseToken); });
    }

    /// <summary>The one search every scan uses, so controls and findings cannot differ in how they look.</summary>
    /// <param name="text">The text to search.</param>
    /// <param name="secret">The planted value.</param>
    /// <returns>True when the text repeats the secret.</returns>
    private static bool Carries(string text, string secret)
    {
        return text.Contains(secret, StringComparison.Ordinal);
    }
}
