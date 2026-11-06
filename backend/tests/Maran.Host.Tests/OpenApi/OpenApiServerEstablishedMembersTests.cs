using System.Text.Json;
using Maran.Host.Tests.OpenApi.Fixtures;

namespace Maran.Host.Tests.OpenApi;

/// <summary>
/// A member the server establishes — the caller's address, agent, identity or a route id — is never
/// described as something a caller may send (rules/csharp.md "Server-established members").
/// </summary>
public sealed class OpenApiServerEstablishedMembersTests : IClassFixture<OpenApiDocumentFixture>
{
    /// <summary>A document that leaks two guarded members, one as a query parameter and one in the body.</summary>
    private const string PlantedLeak = """
        {
          "components": { "schemas": { "P": { "properties": { "ipAddress": {}, "name": {} } } } },
          "paths": { "/x": { "post": {
            "parameters": [ { "name": "userAgent", "in": "query" }, { "name": "id", "in": "path" } ],
            "requestBody": { "content": { "application/json": { "schema": { "$ref": "#/components/schemas/P" } } } }
          } } }
        }
        """;

    /// <summary>The leaks the planted document must be reported with, in report order.</summary>
    private static readonly string[] PlantedLeaks = ["query parameter userAgent", "body property ipAddress"];

    /// <summary>The two hosts and their documents.</summary>
    private readonly OpenApiDocumentFixture _documents;

    /// <summary>Captures the shared fixture.</summary>
    /// <param name="documents">The booted hosts and their rendered documents.</param>
    public OpenApiServerEstablishedMembersTests(OpenApiDocumentFixture documents)
    {
        _documents = documents;
    }

    /// <summary>No operation describes a guarded member of its own command as a parameter or a body property.</summary>
    [Fact]
    public void No_guarded_member_appears_in_any_request_description()
    {
        using var document = JsonDocument.Parse(_documents.First);
        var leaks = new List<string>();
        var guardedActions = 0;

        foreach (var description in _documents.Descriptions.Where(item => { return item.HttpMethod is not null; }))
        {
            var guarded = GuardedMemberCensus.NamesFor(description);
            if (guarded.Count == 0)
            {
                continue;
            }

            guardedActions++;
            var path = OpenApiReader.PathOf(description);
            var found = OpenApiReader.TryOperation(
                document.RootElement,
                description.HttpMethod!,
                path,
                out var operation);
            Assert.True(found);
            leaks.AddRange(
                GuardedMemberCensus.Leaks(document.RootElement, operation, guarded)
                    .Select(leak => { return $"{description.HttpMethod} {path}: {leak}"; }));
        }

        Assert.True(guardedActions > 5, $"only {guardedActions} actions bind a guarded command; census too small");
        Assert.Empty(leaks);
    }

    /// <summary>
    /// The check reports a guarded member planted in a query parameter and in a body, and ignores a path
    /// parameter and a name nobody guards.
    /// </summary>
    [Fact]
    public void The_check_finds_guarded_members_that_are_planted_and_ignores_a_route_parameter()
    {
        using var planted = JsonDocument.Parse(PlantedLeak);
        Assert.True(OpenApiReader.TryOperation(planted.RootElement, "post", "/x", out var operation));
        var guarded = new HashSet<string>(["ipAddress", "userAgent", "id"], StringComparer.OrdinalIgnoreCase);

        var leaks = GuardedMemberCensus.Leaks(planted.RootElement, operation, guarded);

        Assert.Equal(PlantedLeaks, leaks);
        Assert.Empty(GuardedMemberCensus.Leaks(planted.RootElement, operation, new HashSet<string> { "other" }));
    }
}
