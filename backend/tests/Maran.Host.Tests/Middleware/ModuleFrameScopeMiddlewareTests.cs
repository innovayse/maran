using System.Net;
using System.Reflection;
using System.Text.Json;
using Maran.Host.Middleware;
using Maran.Host.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Maran.Host.Tests.Middleware;

/// <summary>
/// Behavioral contract of <see cref="ModuleFrameScopeMiddleware"/>: a call the shell relays for one
/// module's screen reaches only that module's own endpoints. Driven through a minimal test server whose
/// routes carry the controller metadata of two real modules, the owner map built from the real registry,
/// and the Sdk's own translation of a refusal.
/// </summary>
/// <remarks>
/// UNOBSERVED HERE: the composed pipeline's order (routing, authentication, this middleware, endpoint);
/// the probe host places the middleware after routing by hand. That order is held by the integration
/// test that sends a framed call to the real host.
/// </remarks>
public sealed class ModuleFrameScopeMiddlewareTests
{
    /// <summary>The module whose controller the "own" route carries.</summary>
    private const string OwnerId = "firewall";

    /// <summary>The module whose controller the "other" route carries.</summary>
    private const string OtherId = "cron";

    /// <summary>A call framed for the module that owns the endpoint is served.</summary>
    [Fact]
    public async Task A_framed_call_to_its_own_modules_endpoint_is_served()
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();

        var response = await SendAsync(client, "/own", OwnerId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A call framed for one module to an endpoint another module owns is refused with the scope code.</summary>
    [Fact]
    public async Task A_framed_call_to_another_modules_endpoint_is_refused()
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();

        var response = await SendAsync(client, "/other", OwnerId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("problem+json", response.Content.Headers.ContentType!.MediaType, StringComparison.Ordinal);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("ModuleFrameOutOfScope", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(403, body.RootElement.GetProperty("status").GetInt32());
    }

    /// <summary>The same call with the header naming the endpoint's real owner is served - the refusal is about ownership, not the route.</summary>
    [Fact]
    public async Task The_endpoint_refused_to_another_modules_frame_is_served_to_its_own()
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();

        var response = await SendAsync(client, "/other", OtherId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>A call with no frame header is a normal call and is not held to any module.</summary>
    [Fact]
    public async Task A_call_with_no_frame_header_is_a_normal_call()
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();

        var own = await SendAsync(client, "/own", frame: null);
        var other = await SendAsync(client, "/other", frame: null);
        var ownerless = await SendAsync(client, "/ownerless", frame: null);

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownerless.StatusCode);
    }

    /// <summary>A framed call to an endpoint no module owns is refused.</summary>
    [Fact]
    public async Task A_framed_call_to_an_endpoint_no_module_owns_is_refused()
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();

        var response = await SendAsync(client, "/ownerless", OwnerId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A header that is not a well-formed module id is refused, whatever the endpoint. A value of nothing
    /// at all is not among the cases: the in-process request dictionary treats an empty value as an absent
    /// header, so the test server never hands it to the middleware; a blank value is the nearest case it can.
    /// </summary>
    /// <param name="header">The header value the shell never sends.</param>
    [Theory]
    [InlineData(" ")]
    [InlineData("Firewall")]
    [InlineData("fire wall")]
    [InlineData("../firewall")]
    [InlineData("1firewall")]
    [InlineData("firewallfirewallfirewallfirewallfirewall")]
    public async Task A_malformed_frame_header_is_refused(string header)
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();

        var response = await SendAsync(client, "/own", header);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Two header values are refused, even when one of them names the owner.</summary>
    [Fact]
    public async Task A_frame_header_sent_twice_is_refused()
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/own");
        request.Headers.Add(ModuleFrameScopeMiddleware.HeaderName, [OwnerId, OwnerId]);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A framed call that matched no endpoint passes through and is answered 404, as it would be without the header.</summary>
    [Fact]
    public async Task A_framed_call_that_matched_no_endpoint_passes_through_to_a_404()
    {
        using var host = await BuildHostAsync();
        using var client = host.GetTestClient();

        var framed = await SendAsync(client, "/nothing-here", OwnerId);
        var plain = await SendAsync(client, "/nothing-here", frame: null);

        Assert.Equal(HttpStatusCode.NotFound, framed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, plain.StatusCode);
    }

    /// <summary>Sends a GET, with the frame header when one is given.</summary>
    /// <param name="client">The test client.</param>
    /// <param name="path">The route to call.</param>
    /// <param name="frame">The header value, or null for none.</param>
    /// <returns>The server's answer.</returns>
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string? frame)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (frame is not null)
        {
            request.Headers.TryAddWithoutValidation(ModuleFrameScopeMiddleware.HeaderName, frame);
        }

        return client.SendAsync(request);
    }

    /// <summary>The first controller type a shipped module declares.</summary>
    /// <param name="moduleId">The module's id.</param>
    /// <returns>A controller type from the module's assembly.</returns>
    private static TypeInfo ControllerOf(string moduleId)
    {
        var module = ModuleRegistry.All.Single(candidate => { return candidate.Manifest.Id == moduleId; });

        return module.GetType().Assembly.GetTypes()
            .First(type => { return !type.IsAbstract && typeof(ControllerBase).IsAssignableFrom(type); })
            .GetTypeInfo();
    }

    /// <summary>Builds a test host: two routes owned by two real modules, one owned by none, behind the middleware.</summary>
    /// <returns>The started host.</returns>
    private static async Task<IHost> BuildHostAsync()
    {
        var owners = new ModuleEndpointOwners(ModuleRegistry.All);
        var ownedByFirewall = new ControllerActionDescriptor { ControllerTypeInfo = ControllerOf(OwnerId) };
        var ownedByCron = new ControllerActionDescriptor { ControllerTypeInfo = ControllerOf(OtherId) };

        var hostBuilder = new HostBuilder().ConfigureWebHost(webBuilder =>
        {
            webBuilder.UseTestServer();
            webBuilder.ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddControllers();
                services.AddSingleton(owners);
            });
            webBuilder.Configure(app =>
            {
                app.UseRouting();
                app.UseMiddleware<ModuleFrameScopeMiddleware>();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapGet("/own", () => Results.Ok()).WithMetadata(ownedByFirewall);
                    endpoints.MapGet("/other", () => Results.Ok()).WithMetadata(ownedByCron);
                    endpoints.MapGet("/ownerless", () => Results.Ok());
                });
            });
        });

        return await hostBuilder.StartAsync();
    }
}
