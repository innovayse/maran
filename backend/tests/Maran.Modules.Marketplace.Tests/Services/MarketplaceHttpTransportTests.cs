using System.Net;
using Maran.Modules.Marketplace.Domain.Enums;
using Maran.Modules.Marketplace.Models;
using Maran.Modules.Marketplace.Tests.TestSupport;

namespace Maran.Modules.Marketplace.Tests.Services;

/// <summary>
/// The transport's rules of conversation: one host, no redirect it did not follow itself, bodies capped,
/// a GET repeated and a POST never, the lease attached only to a request that asks for it, and nothing
/// identifying sent. Each refusal is paired with the case it must accept.
/// </summary>
public sealed class MarketplaceHttpTransportTests
{
    /// <summary>A catalogue GET with an optional lease.</summary>
    /// <param name="path">The path and query.</param>
    /// <returns>The request.</returns>
    private static MarketplaceRequest Get(string path = "/api/v1/catalogue")
    {
        return new MarketplaceRequest(HttpMethod.Get, path, "ru", LeaseRequirement.Optional, ResponseLimit.Catalogue);
    }

    /// <summary>A grant POST that requires a lease.</summary>
    /// <returns>The request.</returns>
    private static MarketplaceRequest GrantPost()
    {
        return new MarketplaceRequest(
            HttpMethod.Post, "/api/v1/modules/mail/versions/1.0.0/download-grant", "en",
            LeaseRequirement.Required, ResponseLimit.Grant);
    }

    /// <summary>A redirect answer.</summary>
    /// <param name="location">Where it points.</param>
    /// <returns>The answer.</returns>
    private static HttpResponseMessage Redirect(string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return response;
    }

    /// <summary>A redirect to another host is refused and never followed; one to the same host is followed.</summary>
    [Fact]
    public async Task A_redirect_to_another_host_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Redirect("https://evil.test/catalogue"); });

        var refused = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.RedirectRefused, refused.Error);
        Assert.Equal(1, world.Handler.Count);

        var calls = 0;
        world.Handler.Reply(_ =>
        {
            calls++;
            return calls == 1 ? Redirect("/api/v1/elsewhere") : MarketplaceTestWorld.Raw(HttpStatusCode.OK, "{}"u8.ToArray());
        });

        var followed = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.True(followed.IsSuccess);
        Assert.Equal("https://marketplace.test/api/v1/elsewhere", world.Handler.Requests[^1].Uri.ToString());
    }

    /// <summary>A redirect to plain http on the same host is refused, because the scheme is part of the allow-list.</summary>
    [Fact]
    public async Task A_redirect_to_plain_http_on_the_same_host_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return Redirect("http://marketplace.test/api/v1/catalogue"); });

        var result = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.RedirectRefused, result.Error);
        Assert.Equal(1, world.Handler.Count);
    }

    /// <summary>Three same-host redirects are followed, a fourth is refused.</summary>
    [Fact]
    public async Task More_than_three_redirects_are_refused()
    {
        using var world = new MarketplaceTestWorld();
        var hops = 0;
        world.Handler.Reply(_ =>
        {
            hops++;
            return hops <= 3 ? Redirect($"/hop{hops}") : MarketplaceTestWorld.Raw(HttpStatusCode.OK, "{}"u8.ToArray());
        });
        var three = await world.Transport.SendAsync(Get(), CancellationToken.None);

        hops = -100;
        world.Handler.Reply(_ => { return Redirect("/again"); });
        var endless = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.True(three.IsSuccess);
        Assert.Equal(MarketplaceClientRefusals.RedirectRefused, endless.Error);
    }

    /// <summary>A redirect answering a POST is refused, so the single-use grant request is never replayed elsewhere.</summary>
    [Fact]
    public async Task A_redirect_answering_a_post_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Lease.HasLease = true;
        world.Handler.Reply(_ => { return Redirect("/api/v1/other"); });

        var result = await world.Transport.SendAsync(GrantPost(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.RedirectRefused, result.Error);
        Assert.Equal(1, world.Handler.Count);
    }

    /// <summary>A body one byte over the cap is refused; a body exactly at the cap is accepted.</summary>
    [Fact]
    public async Task A_response_over_the_cap_is_refused()
    {
        using var world = new MarketplaceTestWorld();
        world.Options.MaxCatalogueBytes = 100;

        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, new byte[101]); });
        var over = await world.Transport.SendAsync(Get(), CancellationToken.None);

        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, new byte[100]); });
        var atCap = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.ResponseTooLarge, over.Error);
        Assert.True(atCap.IsSuccess);
        Assert.Equal(100, atCap.Value.Body.Length);
    }

    /// <summary>A body that grows past the cap while it is read is refused even when the host declared no length.</summary>
    [Fact]
    public async Task A_response_without_a_declared_length_is_capped_while_it_is_read()
    {
        using var world = new MarketplaceTestWorld();
        world.Options.MaxCatalogueBytes = 100;
        world.Handler.Reply(_ =>
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new NonSeekableMemoryStream(new byte[5000])),
            };
        });

        var result = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.ResponseTooLarge, result.Error);
    }

    /// <summary>The grant request is sent once even when the host fails; a GET is sent again until it works.</summary>
    [Fact]
    public async Task The_grant_is_never_retried()
    {
        using var world = new MarketplaceTestWorld();
        world.Lease.HasLease = true;
        world.Handler.Reply(_ => { return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); });

        var post = await world.Transport.SendAsync(GrantPost(), CancellationToken.None);

        Assert.Equal(1, world.Handler.Count);
        Assert.Equal(503, post.Value.StatusCode);

        var attempts = 0;
        world.Handler.Reply(_ =>
        {
            attempts++;
            return attempts < 2
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : MarketplaceTestWorld.Raw(HttpStatusCode.OK, "{}"u8.ToArray());
        });

        var get = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.True(get.IsSuccess);
        Assert.Equal(200, get.Value.StatusCode);
        Assert.Equal(2, attempts);
    }

    /// <summary>A POST that fails at the connection is not repeated; a GET that fails the same way is, and succeeds.</summary>
    [Fact]
    public async Task A_post_that_cannot_connect_is_not_repeated_while_a_get_is()
    {
        using var world = new MarketplaceTestWorld();
        world.Lease.HasLease = true;
        world.Handler.Reply(_ => { throw new HttpRequestException("refused"); });

        var post = await world.Transport.SendAsync(GrantPost(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.Unreachable, post.Error);
        Assert.Equal(1, world.Handler.Count);

        var attempts = 0;
        world.Handler.Reply(_ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new HttpRequestException("refused");
            }

            return MarketplaceTestWorld.Raw(HttpStatusCode.OK, "{}"u8.ToArray());
        });

        var get = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.True(get.IsSuccess);
        Assert.Equal(2, attempts);
    }

    /// <summary>A request that outlives the configured deadline is answered as unreachable.</summary>
    [Fact]
    public async Task A_request_that_outlives_the_deadline_is_unreachable()
    {
        using var world = new MarketplaceTestWorld();
        world.Options.RequestTimeoutSeconds = 1;
        world.Lease.HasLease = true;
        world.Handler.Responder = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        var result = await world.Transport.SendAsync(GrantPost(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.Unreachable, result.Error);
    }

    /// <summary>A caller's own cancellation is not swallowed as an unreachable host.</summary>
    [Fact]
    public async Task A_cancelled_caller_cancels_and_is_not_reported_as_unreachable()
    {
        using var world = new MarketplaceTestWorld();
        using var cancel = new CancellationTokenSource();
        world.Lease.HasLease = true;
        world.Handler.Responder = async (_, token) =>
        {
            await cancel.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => { return world.Transport.SendAsync(GrantPost(), cancel.Token); });
    }

    /// <summary>The lease is attached to a request that asks for it when one exists, and to none that does not.</summary>
    [Fact]
    public async Task The_lease_is_attached_only_to_a_request_that_asks_for_it()
    {
        using var world = new MarketplaceTestWorld();
        world.Lease.HasLease = true;
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, "{}"u8.ToArray()); });

        await world.Transport.SendAsync(Get(), CancellationToken.None);
        await world.Transport.SendAsync(
            new MarketplaceRequest(HttpMethod.Get, "/api/v1/keys", "en", LeaseRequirement.None, ResponseLimit.Catalogue),
            CancellationToken.None);

        Assert.True(world.Handler.Requests[0].Headers.ContainsKey("Authorization"));
        Assert.False(world.Handler.Requests[1].Headers.ContainsKey("Authorization"));
        Assert.Equal(1, world.Lease.Calls);
    }

    /// <summary>A request that needs a lease and finds none is not sent at all; an optional one goes out anonymously.</summary>
    [Fact]
    public async Task A_request_needing_a_lease_is_not_sent_without_one()
    {
        using var world = new MarketplaceTestWorld();
        world.Lease.HasLease = false;
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, "{}"u8.ToArray()); });

        var required = await world.Transport.SendAsync(GrantPost(), CancellationToken.None);
        var countAfterRequired = world.Handler.Count;
        var optional = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.LeaseRequired, required.Error);
        Assert.Equal(0, countAfterRequired);
        Assert.True(optional.IsSuccess);
        Assert.False(world.Handler.Requests[0].Headers.ContainsKey("Authorization"));
    }

    /// <summary>The request carries the language and the panel version and no other header that could identify the server.</summary>
    [Fact]
    public async Task The_request_sends_the_language_and_the_panel_version_and_nothing_identifying()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, "{}"u8.ToArray()); });

        await world.Transport.SendAsync(Get("/api/v1/catalogue?channel=stable"), CancellationToken.None);

        var headers = world.Handler.Requests[0].Headers;
        string[] allowed = ["Accept", "Accept-Language", "X-Maran-Panel-Version"];
        Assert.Equal("ru", headers["Accept-Language"]);
        Assert.Equal("1.4.0", headers["X-Maran-Panel-Version"]);
        Assert.All(headers.Keys, name => { Assert.Contains(name, allowed, StringComparer.OrdinalIgnoreCase); });
    }

    /// <summary>The signature and the ETag headers are surfaced with the exact body.</summary>
    [Fact]
    public async Task The_signature_and_etag_headers_are_returned_with_the_body()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return world.SignedResponse(new { a = 1 }, "\"v1\""); });

        var result = await world.Transport.SendAsync(Get(), CancellationToken.None);

        var verified = await world.Checker.VerifyAsync(result.Value.Body, result.Value.Signature, CancellationToken.None);

        Assert.Equal("\"v1\"", result.Value.ETag);
        Assert.True(verified.IsSuccess);
    }

    /// <summary>An address that is not https is refused as not configured and no client is even created.</summary>
    [Fact]
    public async Task A_host_that_is_not_https_is_not_configured_and_no_client_is_created()
    {
        using var world = new MarketplaceTestWorld();
        world.Options.BaseUrl = "http://marketplace.test";

        var result = await world.Transport.SendAsync(Get(), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.HostNotConfigured, result.Error);
        Assert.Equal(0, world.Factory.CreateCount);
        Assert.Equal(0, world.Handler.Count);
    }

    /// <summary>A path that names another host is refused before any request, and an ordinary path is sent.</summary>
    [Fact]
    public async Task A_path_that_names_another_host_is_refused_before_any_request()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, "{}"u8.ToArray()); });

        var refused = await world.Transport.SendAsync(Get("//evil.test/x"), CancellationToken.None);
        var countAfterRefused = world.Handler.Count;
        var accepted = await world.Transport.SendAsync(Get("/api/v1/x"), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.RequestRefused, refused.Error);
        Assert.Equal(0, countAfterRefused);
        Assert.True(accepted.IsSuccess);
    }

    /// <summary>A download address on the configured host opens a readable stream; one on another host is refused before any request.</summary>
    [Fact]
    public async Task A_download_is_opened_only_on_the_configured_host()
    {
        using var world = new MarketplaceTestWorld();
        world.Handler.Reply(_ => { return MarketplaceTestWorld.Raw(HttpStatusCode.OK, [1, 2, 3]); });

        var refused = await world.Transport.OpenDownloadAsync(new Uri("https://evil.test/d/1"), CancellationToken.None);
        var countAfterRefused = world.Handler.Count;
        var opened = await world.Transport.OpenDownloadAsync(new Uri("https://marketplace.test/d/1"), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.GrantHostRefused, refused.Error);
        Assert.Equal(0, countAfterRefused);
        using var download = opened.Value;
        using var copy = new MemoryStream();
        await download.Body.CopyToAsync(copy);
        Assert.Equal(new byte[] { 1, 2, 3 }, copy.ToArray());
        Assert.Equal(3L, download.ContentLength!.Value);
    }

    /// <summary>A download answered with an error status opens nothing and carries no lease, and the grant address is not retried.</summary>
    [Fact]
    public async Task A_download_answered_with_a_failure_is_not_retried_and_carries_no_lease()
    {
        using var world = new MarketplaceTestWorld();
        world.Lease.HasLease = true;
        world.Handler.Reply(_ => { return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); });

        var result = await world.Transport.OpenDownloadAsync(new Uri("https://marketplace.test/d/1"), CancellationToken.None);

        Assert.Equal(MarketplaceClientRefusals.Unreachable, result.Error);
        Assert.Equal(1, world.Handler.Count);
        Assert.False(world.Handler.Requests[0].Headers.ContainsKey("Authorization"));
    }
}
