using System.Text.Json;
using System.Text.RegularExpressions;
using Maran.Host.OpenApi;
using Maran.Host.Tests.OpenApi.Fixtures;

namespace Maran.Host.Tests.OpenApi;

/// <summary>
/// The document the panel's controllers produce: reproducible, complete, and honest about credentials.
/// Every assertion reads the rendered JSON the way a consumer of the committed baseline would.
/// </summary>
public sealed class OpenApiDocumentWriterTests : IClassFixture<OpenApiDocumentFixture>
{
    /// <summary>What a timestamp in an ISO-8601 document looks like, up to the hour.</summary>
    private static readonly Regex Timestamp = new(
        @"\d{4}-\d{2}-\d{2}T\d{2}:",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    /// <summary>The two hosts and their documents.</summary>
    private readonly OpenApiDocumentFixture _documents;

    /// <summary>Captures the shared fixture.</summary>
    /// <param name="documents">The booted hosts and their rendered documents.</param>
    public OpenApiDocumentWriterTests(OpenApiDocumentFixture documents)
    {
        _documents = documents;
    }

    /// <summary>Two hosts booted separately render byte-identical documents, and so does a second render.</summary>
    [Fact]
    public void Two_emissions_from_separately_booted_hosts_are_the_same_bytes()
    {
        Assert.NotEmpty(_documents.First);
        Assert.Equal(_documents.First, _documents.Second);
        Assert.Equal(_documents.First, _documents.Writer.Render());
    }

    /// <summary>
    /// No timestamp, machine path or carriage return reaches the document; the detector sees a planted one.
    /// </summary>
    [Fact]
    public void The_document_carries_no_timestamp_no_machine_path_and_no_carriage_return()
    {
        Assert.True(Timestamp.IsMatch("generated 2026-11-05T10:00:00Z"));

        Assert.DoesNotMatch(Timestamp, _documents.First);
        Assert.DoesNotContain(AppContext.BaseDirectory.TrimEnd('/'), _documents.First, StringComparison.Ordinal);
        Assert.DoesNotContain(Environment.CurrentDirectory, _documents.First, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', _documents.First);
        Assert.EndsWith("}\n", _documents.First, StringComparison.Ordinal);
    }

    /// <summary>
    /// Paths and component schemas are listed in ordinal key order, so a moved controller changes nothing.
    /// </summary>
    [Fact]
    public void Paths_and_component_schemas_are_in_ordinal_order()
    {
        using var document = JsonDocument.Parse(_documents.First);
        var paths = document.RootElement.GetProperty("paths").EnumerateObject().Select(Name).ToList();
        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Select(Name).ToList();

        Assert.True(paths.Count > 1);
        Assert.Equal(paths.Order(StringComparer.Ordinal).ToList(), paths);
        Assert.Equal(schemas.Order(StringComparer.Ordinal).ToList(), schemas);
    }

    /// <summary>Every controller action the host composes is documented under its own route and method.</summary>
    [Fact]
    public void Every_controller_action_is_listed_under_its_route_and_method()
    {
        using var document = JsonDocument.Parse(_documents.First);
        var described = _documents.Descriptions
            .Where(description => { return description.HttpMethod is not null && description.GroupName is null; })
            .ToList();
        var missing = described
            .Where(description =>
            {
                return !OpenApiReader.TryOperation(
                    document.RootElement,
                    description.HttpMethod!,
                    OpenApiReader.PathOf(description),
                    out _);
            })
            .Select(description => { return $"{description.HttpMethod} {OpenApiReader.PathOf(description)}"; })
            .ToList();

        Assert.True(described.Count > 50, $"only {described.Count} actions were described; the census saw too little");
        Assert.Empty(missing);
    }

    /// <summary>Both credentials the panel accepts are declared: a bearer JWT, and the X-Api-Key header.</summary>
    [Fact]
    public void The_security_schemes_declare_the_bearer_token_and_the_api_key_header()
    {
        using var document = JsonDocument.Parse(_documents.First);
        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");

        var bearer = schemes.GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        Assert.Equal("JWT", bearer.GetProperty("bearerFormat").GetString());

        var apiKey = schemes.GetProperty("ApiKey");
        Assert.Equal("apiKey", apiKey.GetProperty("type").GetString());
        Assert.Equal("header", apiKey.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", apiKey.GetProperty("name").GetString());
    }

    /// <summary>
    /// A known endpoint is present with its declared statuses and the bearer requirement (positive control).
    /// </summary>
    [Fact]
    public void A_known_endpoint_is_documented_with_its_statuses_and_its_bearer_requirement()
    {
        using var document = JsonDocument.Parse(_documents.First);

        Assert.True(OpenApiReader.TryOperation(document.RootElement, "get", "/api/v1/accounts", out var list));
        var responses = list.GetProperty("responses").EnumerateObject().Select(Name).ToList();
        Assert.Contains("200", responses);
        Assert.Contains("400", responses);
        var ok = list.GetProperty("responses").GetProperty("200").GetRawText();
        Assert.Contains("AccountDto", ok, StringComparison.Ordinal);

        var requirements = list.GetProperty("security").EnumerateArray().ToList();
        var requirement = Assert.Single(requirements);
        Assert.True(requirement.TryGetProperty("Bearer", out _));
    }

    /// <summary>An anonymous endpoint states no security requirement at all.</summary>
    [Fact]
    public void An_anonymous_endpoint_states_no_security_requirement()
    {
        using var document = JsonDocument.Parse(_documents.First);

        Assert.True(OpenApiReader.TryOperation(document.RootElement, "post", "/api/v1/auth/login", out var login));
        Assert.False(login.TryGetProperty("security", out _));
    }

    /// <summary>The stated version is the assembly's, without the revision that changes on every commit.</summary>
    [Fact]
    public void The_info_version_is_the_assembly_version_without_its_revision()
    {
        using var document = JsonDocument.Parse(_documents.First);
        var info = document.RootElement.GetProperty("info");

        Assert.Equal(OpenApiBuildVersion.Of(typeof(Program).Assembly), info.GetProperty("version").GetString());
        Assert.DoesNotContain('+', info.GetProperty("version").GetString()!);
    }

    /// <summary>Reads a JSON property's name.</summary>
    /// <param name="property">The property.</param>
    /// <returns>Its name.</returns>
    private static string Name(JsonProperty property)
    {
        return property.Name;
    }
}
