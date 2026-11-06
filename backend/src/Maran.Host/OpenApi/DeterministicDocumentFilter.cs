using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Maran.Host.OpenApi;

/// <summary>
/// Puts every keyed collection of the document into ordinal key order, so two emissions of the same API
/// are the same bytes.
/// </summary>
/// <remarks>
/// The generator writes paths, operations, responses and component names in the order it meets them,
/// which follows the order the controllers were discovered in. That order is stable for one build and
/// is nobody's decision, so a refactor that moves a controller between files would rewrite the
/// committed baseline without changing the API. Sorting here makes the document a function of the API
/// alone. Nothing else is rewritten: the order of a schema's properties is the declaration order of
/// the type, which is the author's and is kept, and the order of a request's parameters is the action's.
/// </remarks>
public sealed class DeterministicDocumentFilter : IDocumentFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        var paths = new OpenApiPaths();
        foreach (var path in swaggerDoc.Paths.OrderBy(entry => { return entry.Key; }, StringComparer.Ordinal))
        {
            SortOperations(path.Value);
            paths.Add(path.Key, path.Value);
        }

        swaggerDoc.Paths = paths;

        if (swaggerDoc.Components is null)
        {
            return;
        }

        swaggerDoc.Components.Schemas = new SortedDictionary<string, OpenApiSchema>(
            swaggerDoc.Components.Schemas,
            StringComparer.Ordinal);
        swaggerDoc.Components.SecuritySchemes = new SortedDictionary<string, OpenApiSecurityScheme>(
            swaggerDoc.Components.SecuritySchemes,
            StringComparer.Ordinal);
    }

    /// <summary>Orders the operations of one path by method, and the responses of each by status.</summary>
    /// <param name="item">The path item whose operations are put in order.</param>
    private static void SortOperations(OpenApiPathItem item)
    {
        var operations = new Dictionary<OperationType, OpenApiOperation>();
        foreach (var operation in item.Operations.OrderBy(entry => { return entry.Key; }))
        {
            var responses = new OpenApiResponses();
            var ordered = operation.Value.Responses.OrderBy(entry => { return entry.Key; }, StringComparer.Ordinal);
            foreach (var response in ordered)
            {
                responses.Add(response.Key, response.Value);
            }

            operation.Value.Responses = responses;
            operations.Add(operation.Key, operation.Value);
        }

        item.Operations = operations;
    }
}
