using System.Text.Json;
using Maran.Sdk.Extensions;
using Maran.SharedKernel.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Maran.Sdk.Tests.Extensions;

/// <summary>
/// The problem-extension convention at its one wire seam: a failed result's carried
/// <see cref="ProblemExtension"/> becomes one more member of the problem response, and a failure
/// carrying none adds nothing. This is the half the SharedKernel tests cannot see — that the seat
/// on the result actually reaches the JSON an operator's browser decodes.
/// </summary>
public sealed class ApiResultExtensionsProblemExtensionTests
{
    /// <summary>
    /// The serializer settings the panel's MVC surface uses as its base: web defaults, which is
    /// where camelCase property naming comes from. Cached because building options per call is a
    /// build error (CA1869).
    /// </summary>
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>A failure carrying an extension publishes the payload under the extension's key.</summary>
    [Fact]
    public void A_failure_carrying_an_extension_publishes_it_under_its_key()
    {
        var payload = new RestoreCounts(FilesRestored: true, DatabasesRestored: 1, DatabasesTotal: 2);
        var result = Result<string>.Fail(
            Error.Of("RestorePartial", ErrorType.Failure),
            ProblemExtension.Of("restore", payload));

        var response = Assert.IsType<ObjectResult>(result.ToActionResult(NewHttpContext()));

        var problem = Assert.IsType<ProblemDetails>(response.Value);
        var published = Assert.IsType<RestoreCounts>(problem.Extensions["restore"]);
        Assert.True(published.FilesRestored);
        Assert.Equal(1u, published.DatabasesRestored);
        Assert.Equal(2u, published.DatabasesTotal);
        // The standing members survive beside it; the extension extends, never displaces.
        Assert.Equal("RestorePartial", problem.Extensions["code"]);
    }

    /// <summary>The created-translation failure arm publishes the extension too.</summary>
    /// <remarks>
    /// The second public path into the same private translation. Without this, dropping the
    /// pass-through in <c>ToCreatedActionResult</c> alone would survive every other test here.
    /// </remarks>
    [Fact]
    public void A_created_translation_failure_publishes_the_extension_too()
    {
        var result = Result<string>.Fail(
            Error.Of("RestorePartial", ErrorType.Failure),
            ProblemExtension.Of("restore", new RestoreCounts(false, 0, 3)));

        var response = Assert.IsType<ObjectResult>(
            result.ToCreatedActionResult(NewHttpContext(), "/api/v1/backups/1"));

        var problem = Assert.IsType<ProblemDetails>(response.Value);
        var published = Assert.IsType<RestoreCounts>(problem.Extensions["restore"]);
        Assert.False(published.FilesRestored);
        Assert.Equal(0u, published.DatabasesRestored);
        Assert.Equal(3u, published.DatabasesTotal);
    }

    /// <summary>A failure carrying no extension publishes exactly the two standing members.</summary>
    /// <remarks>
    /// The exact key set, not a bound: a phantom member sneaking onto every failure in the panel is
    /// precisely what this negative exists to catch, and "at least code and correlationId" would
    /// stay green over it. The carrying test above is its inverse control.
    /// </remarks>
    [Fact]
    public void A_failure_without_an_extension_publishes_only_the_standing_members()
    {
        var result = Result<string>.Fail(Error.Of("SiteNotFound", ErrorType.NotFound));

        var response = Assert.IsType<ObjectResult>(result.ToActionResult(NewHttpContext()));

        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(["code", "correlationId"], problem.Extensions.Keys.OrderBy(key =>
        {
            return key;
        }, StringComparer.Ordinal).ToArray());
    }

    /// <summary>The payload serializes into the problem JSON as camelCase members under the key.</summary>
    /// <remarks>
    /// The wire shape the SPA decodes, asserted as the exact JSON fragment. The panel serializes
    /// with ASP.NET Core's web defaults (camelCase properties; <c>JsonSerializationExtensions</c>
    /// adds only enum and secret converters on top), and <c>ProblemDetails.Extensions</c> is
    /// <c>[JsonExtensionData]</c>, so the member lands at the TOP level of the problem object —
    /// <c>problem.restore.filesRestored</c>, not <c>problem.extensions.restore</c>. A test that
    /// stopped at the dictionary would go green over a payload whose properties serialize
    /// PascalCase, which the SPA would silently read as absent.
    /// </remarks>
    [Fact]
    public void The_extension_serializes_into_the_problem_body_as_camel_case_members()
    {
        var result = Result<string>.Fail(
            Error.Of("RestorePartial", ErrorType.Failure),
            ProblemExtension.Of("restore", new RestoreCounts(true, 1, 2)));
        var response = Assert.IsType<ObjectResult>(result.ToActionResult(NewHttpContext()));
        var problem = Assert.IsType<ProblemDetails>(response.Value);

        var json = JsonSerializer.Serialize(problem, WebJson);

        Assert.Contains(
            "\"restore\":{\"filesRestored\":true,\"databasesRestored\":1,\"databasesTotal\":2}",
            json,
            StringComparison.Ordinal);
        // Positive control on the probe's axis: the standing member is in the same document, so the
        // fragment above is being sought in real serializer output rather than in an empty string.
        Assert.Contains("\"code\":\"RestorePartial\"", json, StringComparison.Ordinal);
    }

    /// <summary>Builds a request context with an empty container, the way an unloaded host presents one.</summary>
    /// <returns>The context handed to the extension under test.</returns>
    private static DefaultHttpContext NewHttpContext()
    {
        return new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider(),
        };
    }

    /// <summary>
    /// A stand-in payload shaped like the Backups module's real one, declared here because the Sdk
    /// tests cannot reference a module project.
    /// </summary>
    /// <param name="FilesRestored">Whether the files half was already replaced.</param>
    /// <param name="DatabasesRestored">How many databases had been replaced.</param>
    /// <param name="DatabasesTotal">How many the operation set out to replace.</param>
    private sealed record RestoreCounts(bool FilesRestored, uint DatabasesRestored, uint DatabasesTotal);
}
