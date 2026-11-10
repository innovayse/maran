using System.Text.Json;
using Maran.Host.Cli.Models;
using Maran.Host.Extensions;
using Maran.Host.Tests.OpenApi.Fixtures;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Host.Tests.OpenApi;

/// <summary>
/// The document lists the minimal endpoints as well as the controller actions, and the <c>openapi</c> verb,
/// which serves nothing, still maps them: only a mapped endpoint is described, and the verb used to return
/// before the mapping, so the committed baseline lacked the module catalogue, the dashboard and the search.
/// </summary>
public sealed class OpenApiMinimalEndpointsTests : IClassFixture<OpenApiDocumentFixture>
{
    /// <summary>The minimal endpoints the SPA reads, as method and path.</summary>
    private static readonly (string Method, string Path)[] MinimalOperations =
    [
        ("get", "/api/v1/modules"),
        ("get", "/api/v1/dashboard"),
        ("get", "/api/v1/dashboard/attention-count"),
        ("get", "/api/v1/dashboard/first-run"),
        ("post", "/api/v1/search"),
    ];

    /// <summary>The two hosts and their documents.</summary>
    private readonly OpenApiDocumentFixture _documents;

    /// <summary>Captures the shared fixture.</summary>
    /// <param name="documents">The booted hosts and their rendered documents.</param>
    public OpenApiMinimalEndpointsTests(OpenApiDocumentFixture documents)
    {
        _documents = documents;
    }

    /// <summary>Every minimal endpoint the SPA reads is an operation of the rendered document.</summary>
    [Fact]
    public void Every_minimal_endpoint_is_an_operation_of_the_document()
    {
        using var document = JsonDocument.Parse(_documents.First);

        var missing = MinimalOperations
            .Where(operation => { return !OpenApiReader.TryOperation(document.RootElement, operation.Method, operation.Path, out _); })
            .Select(operation => { return $"{operation.Method} {operation.Path}"; })
            .ToList();

        Assert.Empty(missing);
        Assert.False(OpenApiReader.TryOperation(document.RootElement, "get", "/api/v1/no-such-endpoint", out _));
    }

    /// <summary>
    /// Any other verb maps nothing: the mapping is for the document only. Its positive half is
    /// <see cref="Every_minimal_endpoint_is_an_operation_of_the_document"/>, which renders the document the verb writes
    /// from the fully composed host (a bare application cannot bind the endpoints' services).
    /// </summary>
    [Fact]
    public void Another_verb_maps_nothing()
    {
        Assert.Empty(PatternsMappedFor("status"));
    }

    /// <summary>Builds a bare application, applies the mapping for one verb and lists the route patterns it holds.</summary>
    /// <param name="verb">The command-line verb.</param>
    /// <returns>The raw text of every route pattern mapped.</returns>
    private static List<string> PatternsMappedFor(string verb)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddControllers();
        builder.Services.AddHealthChecks();
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(_ => { });
        using var app = builder.Build();

        app.MapPanelOpenApiEndpoints(new CliInvocation(verb, [], false, false, null));

        return app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => { return endpoint.RoutePattern.RawText ?? string.Empty; })
            .ToList();
    }
}
