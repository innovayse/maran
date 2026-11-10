using System.Net;
using Maran.Host.IntegrationTests.Fixtures;
using Maran.Host.Middleware;
using Maran.Host.Modules;

namespace Maran.Host.IntegrationTests.Modules;

/// <summary>
/// The frame route <c>GET /module-frames/{id}/{version}/{**path}</c> against a real host: a packaged,
/// active module is framed with the frame's own headers, nothing else is, and a call the shell relays for
/// a module reaches only that module's endpoints.
/// </summary>
/// <remarks>
/// <para>
/// Every refusal here is paired with the accepting call it is about, because a route that refused
/// everything would pass a test that only ever asked for something it must refuse.
/// </para>
/// <para>
/// UNOBSERVED HERE: that a browser loads the frame (opaque origin, SRI over CORS, the handshake) and what
/// nginx adds in front of the route; those are the browser specs' and the installer check's to observe.
/// </para>
/// </remarks>
[Collection(SharedDatabase.Name)]
public sealed class ModuleFrameEndpointTests : IAsyncLifetime
{
    /// <summary>The frame's folder URL for the running version.</summary>
    private const string FrameRoot = "/module-frames/hello/1.0.0/";

    /// <summary>This class's own database on the shared server.</summary>
    private readonly TestDatabase _pg;

    /// <summary>Binds this test to the PostgreSQL server the assembly shares.</summary>
    /// <param name="postgres">The shared server, injected by the collection fixture.</param>
    public ModuleFrameEndpointTests(PostgresFixture postgres)
    {
        _pg = new TestDatabase(postgres);
    }

    /// <summary>Prepares the fixture before the tests run.</summary>
    public Task InitializeAsync()
    {
        return _pg.CreateAsync();
    }

    /// <summary>Releases what the fixture allocated, asynchronously.</summary>
    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>The folder URL answers the generated document with the frame's headers.</summary>
    [Fact]
    public async Task The_frame_document_is_generated_and_carries_the_frame_csp_and_nosniff()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        using var client = world.Factory.CreateClient();

        var response = await client.GetAsync(FrameRoot);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>module</title></head><body>"
            + $"<script src=\"entry.js\" integrity=\"{world.Integrity}\" crossorigin=\"anonymous\"></script></body></html>",
            await response.Content.ReadAsStringAsync());
        AssertFrameHeaders(response, "no-store");
    }

    /// <summary>A script of the bundle is served as script with the frame's headers, immutable, and the stream is closed.</summary>
    [Fact]
    public async Task A_script_inside_the_bundle_is_served_with_the_frame_headers_and_closed_afterwards()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        using var client = world.Factory.CreateClient();

        var response = await client.GetAsync(FrameRoot + "entry.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(FramedModuleWorld.EntryScript, await response.Content.ReadAsStringAsync());
        AssertFrameHeaders(response, "public, max-age=31536000, immutable");
        Assert.NotEmpty(world.Assets.Opened);
        Assert.All(world.Assets.Opened, stream => { Assert.True(stream.IsDisposed); });
    }

    /// <summary>A stylesheet in a subfolder is served as a stylesheet.</summary>
    [Fact]
    public async Task A_stylesheet_in_a_subfolder_is_served_as_a_stylesheet()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        using var client = world.Factory.CreateClient();

        var response = await client.GetAsync(FrameRoot + "assets/style.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType!.MediaType);
    }

    /// <summary>A document the package holds is never served, while a script beside it is.</summary>
    [Fact]
    public async Task An_html_file_inside_spa_is_not_served()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        using var client = world.Factory.CreateClient();

        var document = await client.GetAsync(FrameRoot + "page.html");
        var script = await client.GetAsync(FrameRoot + "entry.js");

        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
    }

    /// <summary>A file the package does not hold and a path off the allow-list are 404.</summary>
    [Fact]
    public async Task A_missing_file_and_an_unlisted_extension_are_404()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        using var client = world.Factory.CreateClient();

        var missing = await client.GetAsync(FrameRoot + "absent.js");
        var unlisted = await client.GetAsync(FrameRoot + "archive.zip");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unlisted.StatusCode);
    }

    /// <summary>A degraded module is not framed while the same module, active, is.</summary>
    [Fact]
    public async Task A_degraded_module_is_not_framed()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        using var client = world.Factory.CreateClient();
        var whileActive = await client.GetAsync(FrameRoot);

        await world.DegradeAsync();
        var document = await client.GetAsync(FrameRoot);
        var script = await client.GetAsync(FrameRoot + "entry.js");

        Assert.Equal(HttpStatusCode.OK, whileActive.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, document.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, script.StatusCode);
    }

    /// <summary>
    /// Another version, an unknown module and a module that shipped with the panel all answer 404 and never
    /// 403, while the running version answers 200.
    /// </summary>
    [Fact]
    public async Task An_unknown_version_is_404_not_403()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        using var client = world.Factory.CreateClient();

        var running = await client.GetAsync(FrameRoot);
        var otherVersion = await client.GetAsync("/module-frames/hello/9.9.9/");
        var otherVersionScript = await client.GetAsync("/module-frames/hello/9.9.9/entry.js");
        var unknownModule = await client.GetAsync("/module-frames/nothing/1.0.0/");
        var shippedModule = await client.GetAsync("/module-frames/identity/1.0.0/");

        Assert.Equal(HttpStatusCode.OK, running.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherVersion.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherVersionScript.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownModule.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, shippedModule.StatusCode);
    }

    /// <summary>A call framed for the module that owns the endpoint is served, for another module's endpoint it is refused, and with no frame it is a normal call.</summary>
    [Fact]
    public async Task A_framed_call_through_the_composed_pipeline_to_another_modules_endpoint_is_refused()
    {
        await using var world = await FramedModuleWorld.BootAsync(_pg);
        using var client = world.Factory.CreateClient();

        var plain = await client.GetAsync("/api/v1/setup/state");
        var own = await GetFramedAsync(client, "/api/v1/setup/state", "identity");
        var other = await GetFramedAsync(client, "/api/v1/setup/state", FramedModuleWorld.ModuleId);
        var hostEndpoint = await GetFramedAsync(client, "/api/v1/modules", "identity");

        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal((HttpStatusCode.Forbidden, "ModuleFrameOutOfScope"), await ProblemDocument.SignatureAsync(other));
        Assert.Equal((HttpStatusCode.Forbidden, "ModuleFrameOutOfScope"), await ProblemDocument.SignatureAsync(hostEndpoint));
    }

    /// <summary>Sends a GET that the shell would relay for a module's frame.</summary>
    /// <param name="client">The client.</param>
    /// <param name="path">The route to call.</param>
    /// <param name="moduleId">The module the call is made for.</param>
    /// <returns>The panel's answer.</returns>
    private static Task<HttpResponseMessage> GetFramedAsync(HttpClient client, string path, string moduleId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(ModuleFrameScopeMiddleware.HeaderName, moduleId);

        return client.SendAsync(request);
    }

    /// <summary>Asserts the headers every successful frame answer carries, and the ones it must not.</summary>
    /// <param name="response">The answer.</param>
    /// <param name="cacheControl">The cache policy expected for this kind of answer.</param>
    private static void AssertFrameHeaders(HttpResponseMessage response, string cacheControl)
    {
        Assert.Equal(ModuleFrameHeaders.ContentSecurityPolicy, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal(cacheControl, response.Headers.GetValues("Cache-Control").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.False(response.Headers.Contains("X-Frame-Options"));
    }
}
