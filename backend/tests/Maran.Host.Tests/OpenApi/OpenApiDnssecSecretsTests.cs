using System.Text.Json;
using Maran.Host.Tests.OpenApi.Fixtures;

namespace Maran.Host.Tests.OpenApi;

/// <summary>
/// The document the panel renders describes the DNSSEC routes and names no private key anywhere in the DNS
/// module's schemas. The search is over the rendered document the way a consumer reads it, and it carries a
/// positive control, because a search that finds nothing is only worth something if it could have found a thing.
/// </summary>
public sealed class OpenApiDnssecSecretsTests : IClassFixture<OpenApiDocumentFixture>
{
    /// <summary>The words a member that carries the key would be named with.</summary>
    private static readonly string[] SecretWords = ["private", "secret", "scalar"];

    /// <summary>The routes this issue adds, with the method each takes.</summary>
    private static readonly (string Path, string Method)[] DnssecRoutes =
    [
        ("/api/v1/dns-zones/{zoneId}/dnssec", "get"),
        ("/api/v1/dns-zones/{zoneId}/dnssec/enable", "post"),
        ("/api/v1/dns-zones/{zoneId}/dnssec/withdraw", "post"),
        ("/api/v1/dns-zones/{zoneId}/dnssec/finish-withdrawal", "post"),
    ];

    /// <summary>The booted hosts and their documents.</summary>
    private readonly OpenApiDocumentFixture _documents;

    /// <summary>Captures the shared fixture.</summary>
    /// <param name="documents">The booted hosts and their rendered documents.</param>
    public OpenApiDnssecSecretsTests(OpenApiDocumentFixture documents)
    {
        _documents = documents;
    }

    /// <summary>Lists the member names of every schema whose id starts with the DNS module's, that name a secret.</summary>
    /// <param name="document">The OpenAPI document as JSON text.</param>
    /// <returns>One line per offending member, as <c>schema.member</c>.</returns>
    private static List<string> SecretMembersOfDnsSchemas(string document)
    {
        using var parsed = JsonDocument.Parse(document);
        var found = new List<string>();
        foreach (var schema in parsed.RootElement.GetProperty("components").GetProperty("schemas").EnumerateObject())
        {
            if (!schema.Name.StartsWith("Modules.Dns.", StringComparison.Ordinal)
                || !schema.Value.TryGetProperty("properties", out var properties))
            {
                continue;
            }

            foreach (var member in properties.EnumerateObject())
            {
                if (SecretWords.Any(word =>
                {
                    return member.Name.Contains(word, StringComparison.OrdinalIgnoreCase);
                }))
                {
                    found.Add($"{schema.Name}.{member.Name}");
                }
            }
        }

        return found;
    }

    /// <summary>Every DNSSEC route is documented under its own path and method.</summary>
    [Fact]
    public void The_document_describes_every_dnssec_route()
    {
        using var parsed = JsonDocument.Parse(_documents.First);
        var paths = parsed.RootElement.GetProperty("paths");

        foreach (var (path, method) in DnssecRoutes)
        {
            Assert.True(paths.TryGetProperty(path, out var item), path);
            Assert.True(item.TryGetProperty(method, out _), $"{method} {path}");
        }
    }

    /// <summary>No member of any DNS schema is named for a secret, and the DNSSEC answer schema is among those searched.</summary>
    [Fact]
    public void No_dns_schema_names_a_private_key()
    {
        using var parsed = JsonDocument.Parse(_documents.First);
        var schemas = parsed.RootElement.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("Modules.Dns.Common.ZoneDnssecDto", out var dnssec));
        Assert.True(dnssec.GetProperty("properties").EnumerateObject().Any());
        Assert.Empty(SecretMembersOfDnsSchemas(_documents.First));
    }

    /// <summary>Positive control: the same search finds a private key member planted into a DNS schema.</summary>
    [Fact]
    public void The_search_finds_a_private_key_member_planted_into_a_dns_schema()
    {
        const string planted =
            """{"components":{"schemas":{"Modules.Dns.Common.PlantedDto":{"properties":{"privateKey":{"type":"string"}}}}}}""";

        Assert.Equal(["Modules.Dns.Common.PlantedDto.privateKey"], SecretMembersOfDnsSchemas(planted));
    }
}
