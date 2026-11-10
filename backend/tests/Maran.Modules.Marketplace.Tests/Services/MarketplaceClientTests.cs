using System.Net;
using System.Text.Json;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Tests.TestSupport;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>
/// The client end to end over a scripted wire: the switch, the cache, the signed answers, the update
/// request and the whole download. The refusals are paired with the accepting case that proves the check is
/// not simply refusing everything.
/// </summary>
public sealed class MarketplaceClientTests
{
    /// <summary>The package bytes the end-to-end download serves.</summary>
    private static readonly byte[] Package = [5, 4, 3, 2, 1];

    /// <summary>A signed page with one module, good for the next hours.</summary>
    /// <param name="world">The test world.</param>
    /// <param name="etag">An ETag to send, or null.</param>
    /// <returns>The answer.</returns>
    private static HttpResponseMessage Page(MarketplaceTestWorld world, string? etag = null)
    {
        var now = world.Clock.UtcNow;
        var module = MarketplaceDocuments.Module(
            "mail", MarketplaceDocuments.Version("1.0.0", MarketplaceTestWorld.Sha256Of(Package), Package.Length));
        return world.SignedResponse(MarketplaceDocuments.Page(now, now.AddHours(12), module), etag);
    }

    /// <summary>A signed details answer for the module "mail" with the version "1.0.0".</summary>
    /// <param name="world">The test world.</param>
    /// <param name="revoked">Whether that version is revoked.</param>
    /// <param name="moduleId">The id the answer names.</param>
    /// <returns>The answer.</returns>
    private static HttpResponseMessage Details(MarketplaceTestWorld world, bool revoked = false, string moduleId = "mail")
    {
        var now = world.Clock.UtcNow;
        var version = MarketplaceDocuments.Version("1.0.0", MarketplaceTestWorld.Sha256Of(Package), Package.Length, revoked);
        return world.SignedResponse(
            MarketplaceDocuments.Details(now, now.AddHours(12), MarketplaceDocuments.Module(moduleId, version), version));
    }

    /// <summary>Scripts the whole download: details, then the grant, then the package bytes.</summary>
    /// <param name="world">The test world.</param>
    /// <param name="revoked">Whether the version is revoked.</param>
    private static void ScriptDownload(MarketplaceTestWorld world, bool revoked = false)
    {
        world.Lease.HasLease = true;
        world.Handler.Reply(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/download-grant", StringComparison.Ordinal))
            {
                return world.SignedResponse(MarketplaceDocuments.Grant(
                    "https://marketplace.test/downloads/g1",
                    MarketplaceTestWorld.Sha256Of(Package),
                    Package.Length,
                    world.Clock.UtcNow.AddMinutes(5)));
            }

            return path.StartsWith("/downloads/", StringComparison.Ordinal)
                ? MarketplaceTestWorld.Raw(HttpStatusCode.OK, Package)
                : Details(world, revoked);
        });
    }

    /// <summary>A switched-off marketplace sends nothing and never asks for a client; switched on, the same call reaches the wire.</summary>
    [Fact]
    public async Task A_switched_off_marketplace_makes_no_call_and_creates_no_client()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Page(world); });
        world.Switch.Enabled = false;

        var off = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.Disabled, off.Error);
        Assert.Equal(0, world.Handler.Count);
        Assert.Equal(0, world.Factory.CreateCount);
        Assert.Equal(0, world.Lease.Calls);

        world.Switch.Enabled = true;
        var on = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        Assert.True(on.IsSuccess);
        Assert.Equal(1, world.Handler.Count);
    }

    /// <summary>Switching off hides a page that was cached while the marketplace was on.</summary>
    [Fact]
    public async Task Switching_off_hides_what_was_cached()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Page(world); });
        await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        world.Switch.Enabled = false;
        var result = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.Disabled, result.Error);
    }

    /// <summary>A verified catalogue is returned, and its fields are those the service sent.</summary>
    [Fact]
    public async Task A_signed_fresh_catalogue_is_returned()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Page(world); });

        var result = await world.Client.ListCatalogueAsync("stable", "ru", "ma il", "c1", CancellationToken.None);

        var module = Assert.Single(result.Value.Items);
        Assert.Equal("mail", module.Id);
        Assert.Equal("1.0.0", module.Latest!.Version);
        Assert.Equal("channel=stable&locale=ru&search=ma%20il&cursor=c1", world.Handler.Requests[0].Uri.Query.TrimStart('?'));
    }

    /// <summary>An unsigned, stale or wrongly signed catalogue is refused; a good one in the same position is not.</summary>
    [Fact]
    public async Task An_unverifiable_catalogue_is_refused_with_the_reason()
    {
        using var world = new MarketplaceTestWorld();
        using var other = new MarketplaceSigner();
        var now = world.Clock.UtcNow;
        var module = MarketplaceDocuments.Module("mail", null);

        world.Handler.Reply(_ => { return MarketplaceTestWorld.UnsignedResponse(MarketplaceDocuments.Page(now, now.AddHours(1), module)); });
        var unsigned = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        world.Handler.Reply(_ => { return MarketplaceTestWorld.SignedBy(other, MarketplaceDocuments.Page(now, now.AddHours(1), module)); });
        var wrongKey = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        world.Handler.Reply(_ => { return world.SignedResponse(MarketplaceDocuments.Page(now.AddHours(-30), now.AddHours(1), module)); });
        var stale = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        world.Handler.Reply(_ => { return world.SignedResponse(MarketplaceDocuments.Page(now, now.AddHours(1), module)); });
        var good = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, unsigned.Error);
        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, wrongKey.Error);
        Assert.Equal(MarketplaceClientRefusals.CatalogueStale, stale.Error);
        Assert.True(good.IsSuccess);
    }

    /// <summary>A body that is correctly signed but not the documented shape is refused rather than guessed at.</summary>
    [Fact]
    public async Task A_signed_body_of_the_wrong_shape_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return world.SignedResponse(new { items = "not a list" }); });

        var result = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.ResponseInvalid, result.Error);
    }

    /// <summary>A second request inside the time-to-live is served from memory; after it, the service is asked again.</summary>
    [Fact]
    public async Task A_cached_catalogue_is_served_without_a_request_until_it_expires()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Page(world, "\"v1\""); });

        await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);
        await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);
        Assert.Equal(1, world.Handler.Count);

        await world.Client.ListCatalogueAsync("stable", "ru", null, null, CancellationToken.None);
        Assert.Equal(2, world.Handler.Count);

        world.Clock.Advance(TimeSpan.FromMinutes(11));
        await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);
        Assert.Equal(3, world.Handler.Count);
    }

    /// <summary>Invalidating the cache makes the next request go to the service.</summary>
    [Fact]
    public async Task An_invalidated_cache_asks_the_service_again()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Page(world); });
        await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        world.Cache.Invalidate();
        await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        Assert.Equal(2, world.Handler.Count);
    }

    /// <summary>An expired entry is revalidated with its ETag, and a 304 renews it without a body.</summary>
    [Fact]
    public async Task An_expired_entry_is_revalidated_with_its_etag()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Page(world, "\"v1\""); });
        await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        world.Clock.Advance(TimeSpan.FromMinutes(11));
        world.Handler.Reply(_ => { return new HttpResponseMessage(HttpStatusCode.NotModified); });
        var renewed = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        Assert.True(renewed.IsSuccess);
        Assert.Equal("\"v1\"", world.Handler.Requests[1].Headers["If-None-Match"]);

        await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);
        Assert.Equal(2, world.Handler.Count);
    }

    /// <summary>A 304 cannot revive a page whose signature has since grown too old.</summary>
    [Fact]
    public async Task A_not_modified_answer_does_not_revive_a_stale_page()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Page(world, "\"v1\""); });
        await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        world.Clock.Advance(TimeSpan.FromHours(13));
        world.Handler.Reply(_ => { return new HttpResponseMessage(HttpStatusCode.NotModified); });
        var result = await world.Client.ListCatalogueAsync("stable", "en", null, null, CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueStale, result.Error);
    }

    /// <summary>The module details are returned, and an answer about another module is refused.</summary>
    [Fact]
    public async Task Details_about_another_module_are_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Details(world); });
        var right = await world.Client.GetModuleAsync("mail", "en", CancellationToken.None);

        world.Handler.Reply(_ => { return Details(world, moduleId: "dns"); });
        var substituted = await world.Client.GetModuleAsync("mail", "en", CancellationToken.None);

        Assert.True(right.IsSuccess);
        Assert.Equal("1.0.0", right.Value.Versions[0].Version);
        Assert.Equal(MarketplaceClientRefusals.ResponseInvalid, substituted.Error);
    }

    /// <summary>The service's failure codes select the panel's behaviour.</summary>
    [Theory]
    [InlineData(403, "entitlement_missing", "EntitlementMissing")]
    [InlineData(403, "lease_expired", "LeaseRequired")]
    [InlineData(409, "incompatible_panel", "IncompatiblePanel")]
    [InlineData(410, "revoked", "PackageRevoked")]
    [InlineData(404, "not_found", "ModuleNotInCatalogue")]
    public async Task The_services_problem_codes_select_the_behaviour(int status, string code, string expected)
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Problem((HttpStatusCode)status, code); });

        var result = await world.Client.GetModuleAsync("mail", "en", CancellationToken.None);

        Assert.Equal(expected, result.Error!.Code);
    }

    /// <summary>The update check sends the channel and each installed module's id, version and digest, and nothing else.</summary>
    [Fact]
    public async Task The_update_check_sends_only_the_channel_and_the_installed_triples()
    {
        using var world = new MarketplaceTestWorld();
        var now = world.Clock.UtcNow;
        world.Handler.Reply(_ => { return world.SignedResponse(MarketplaceDocuments.Updates(now, now.AddHours(1))); });

        var result = await world.Client.CheckUpdatesAsync(
            [new InstalledModuleReference("mail", "1.0.0", new string('a', 64))], "stable", "en", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var sent = world.Handler.Requests[0];
        Assert.Equal(HttpMethod.Post, sent.Method);
        using var json = JsonDocument.Parse(sent.Body!);
        Assert.Equal(["channel", "installed"], json.RootElement.EnumerateObject().Select(p => { return p.Name; }).Order().ToArray());
        Assert.Equal(
            ["id", "sha256", "version"],
            json.RootElement.GetProperty("installed")[0].EnumerateObject().Select(p => { return p.Name; }).Order().ToArray());
    }

    /// <summary>A refused update check is a failure, and the same call with a signed answer succeeds.</summary>
    [Fact]
    public async Task An_unsigned_update_report_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        var now = world.Clock.UtcNow;
        world.Handler.Reply(_ => { return MarketplaceTestWorld.UnsignedResponse(MarketplaceDocuments.Updates(now, now.AddHours(1))); });

        var result = await world.Client.CheckUpdatesAsync([], "stable", "en", CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, result.Error);
    }

    /// <summary>The whole download works: details, grant, package, and the file is deleted with the source.</summary>
    [Fact]
    public async Task A_package_is_downloaded_through_the_grant_and_deleted_with_its_source()
    {
        using var world = new MarketplaceTestWorld();
        ScriptDownload(world);

        var result = await world.Client.DownloadPackageAsync("mail", "1.0.0", CancellationToken.None);

        var source = result.Value;
        var files = Directory.GetFiles(world.PackageDirectory);
        Assert.Single(files);
        Assert.Equal(Package, await File.ReadAllBytesAsync(files[0]));
        Assert.Equal("mail", source.Expected.ModuleId);
        Assert.Equal(MarketplaceTestWorld.Sha256Of(Package), source.Expected.Sha256);
        Assert.Contains(world.Handler.Requests, request => { return request.Method == HttpMethod.Post; });

        await source.DisposeAsync();
        Assert.Empty(Directory.GetFiles(world.PackageDirectory));
    }

    /// <summary>The grant request is sent once, carries the lease and no body secret, and the grant id is never in an error.</summary>
    [Fact]
    public async Task A_refused_grant_is_one_request_and_reports_the_code()
    {
        using var world = new MarketplaceTestWorld();
        world.Lease.HasLease = true;
        world.Handler.Reply(request =>
        {
            return request.Method == HttpMethod.Post
                ? MarketplaceTestWorld.Problem(HttpStatusCode.Forbidden, "entitlement_missing")
                : Details(world);
        });

        var result = await world.Client.DownloadPackageAsync("mail", "1.0.0", CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.EntitlementMissing, result.Error);
        Assert.Equal(1, world.Handler.Requests.Count(request => { return request.Method == HttpMethod.Post; }));
    }

    /// <summary>A revoked version is refused before a grant is asked for; the same version not revoked is downloaded.</summary>
    [Fact]
    public async Task A_revoked_version_is_refused_before_a_grant_is_requested()
    {
        using var world = new MarketplaceTestWorld();
        ScriptDownload(world, revoked: true);

        var result = await world.Client.DownloadPackageAsync("mail", "1.0.0", CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.PackageRevoked, result.Error);
        Assert.DoesNotContain(world.Handler.Requests, request => { return request.Method == HttpMethod.Post; });
    }

    /// <summary>A version the catalogue does not list is refused before a grant is requested.</summary>
    [Fact]
    public async Task A_version_the_catalogue_does_not_list_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        ScriptDownload(world);

        var result = await world.Client.DownloadPackageAsync("mail", "9.9.9", CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.ModuleNotInCatalogue, result.Error);
        Assert.DoesNotContain(world.Handler.Requests, request => { return request.Method == HttpMethod.Post; });
    }

    /// <summary>Without a lease no grant is requested.</summary>
    [Fact]
    public async Task A_download_without_a_lease_requests_no_grant()
    {
        using var world = new MarketplaceTestWorld();
        ScriptDownload(world);
        world.Lease.HasLease = false;

        var result = await world.Client.DownloadPackageAsync("mail", "1.0.0", CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.LeaseRequired, result.Error);
        Assert.DoesNotContain(world.Handler.Requests, request => { return request.Method == HttpMethod.Post; });
    }

    /// <summary>A grant on another host fails the download after the grant request and before any download request.</summary>
    [Fact]
    public async Task A_grant_pointing_at_another_host_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Lease.HasLease = true;
        world.Handler.Reply(request =>
        {
            return request.Method == HttpMethod.Post
                ? world.SignedResponse(MarketplaceDocuments.Grant(
                    "https://evil.test/d/1",
                    MarketplaceTestWorld.Sha256Of(Package),
                    Package.Length,
                    world.Clock.UtcNow.AddMinutes(5)))
                : Details(world);
        });

        var result = await world.Client.DownloadPackageAsync("mail", "1.0.0", CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.GrantHostRefused, result.Error);
        Assert.Equal(2, world.Handler.Count);
    }

    /// <summary>An unsigned grant is refused, so a forged grant cannot name a download.</summary>
    [Fact]
    public async Task An_unsigned_grant_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Lease.HasLease = true;
        world.Handler.Reply(request =>
        {
            return request.Method == HttpMethod.Post
                ? MarketplaceTestWorld.UnsignedResponse(MarketplaceDocuments.Grant(
                    "https://marketplace.test/d/1",
                    MarketplaceTestWorld.Sha256Of(Package),
                    Package.Length,
                    world.Clock.UtcNow.AddMinutes(5)))
                : Details(world);
        });

        var result = await world.Client.DownloadPackageAsync("mail", "1.0.0", CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, result.Error);
    }

    /// <summary>The key status counts the pinned keys without a network call.</summary>
    [Fact]
    public async Task The_key_status_counts_the_pinned_keys_without_a_call()
    {
        using var world = new MarketplaceTestWorld();

        var status = await world.Client.GetKeyStatusAsync(CancellationToken.None);

        Assert.Equal(new MarketplaceKeyStatus(1, 0), status.Value);
        Assert.Equal(0, world.Handler.Count);
    }

    /// <summary>A key list signed by a pinned key is accepted by the refresh, and one signed by an unknown key is not.</summary>
    [Fact]
    public async Task Refreshing_the_keys_accepts_only_a_list_a_trusted_key_signed()
    {
        using var world = new MarketplaceTestWorld();
        using var newKey = new MarketplaceSigner();
        var now = world.Clock.UtcNow;
        var keys = MarketplaceDocuments.Keys(now, now.AddHours(1), newKey.PublicKeyPem);

        world.Handler.Reply(_ => { return MarketplaceTestWorld.SignedBy(newKey, keys); });
        var refused = await world.Client.RefreshKeysAsync(CancellationToken.None);

        world.Handler.Reply(_ => { return world.SignedResponse(keys); });
        var accepted = await world.Client.RefreshKeysAsync(CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.CatalogueUnsigned, refused.Error);
        Assert.Equal(new MarketplaceKeyStatus(1, 1), accepted.Value);
        Assert.All(world.Handler.Requests, request => { Assert.False(request.Headers.ContainsKey("Authorization")); });
    }
}
