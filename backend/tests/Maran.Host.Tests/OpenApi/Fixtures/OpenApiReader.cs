using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;

namespace Maran.Host.Tests.OpenApi.Fixtures;

/// <summary>Reads a rendered OpenAPI document the way a consumer would: as JSON, by path and method.</summary>
public static class OpenApiReader
{
    /// <summary>A route constraint inside a template segment: <c>:guid</c> in <c>{id:guid}</c>.</summary>
    private static readonly Regex RouteConstraint = new(
        @"(?<=\{[^}:]+):[^}]+(?=\})",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    /// <summary>The path an API description is documented under: its relative path without route constraints.</summary>
    /// <param name="description">The description.</param>
    /// <returns>The key in the document's <c>paths</c> object.</returns>
    public static string PathOf(ApiDescription description)
    {
        var relative = description.RelativePath ?? string.Empty;
        return "/" + RouteConstraint.Replace(relative, string.Empty).TrimStart('/');
    }

    /// <summary>Finds one operation.</summary>
    /// <param name="document">The document's root.</param>
    /// <param name="method">The HTTP method, any case.</param>
    /// <param name="path">The key in <c>paths</c>.</param>
    /// <param name="operation">The operation when found.</param>
    /// <returns>True when the document lists the method under the path.</returns>
    public static bool TryOperation(JsonElement document, string method, string path, out JsonElement operation)
    {
        operation = default;
        return document.TryGetProperty("paths", out var paths)
            && paths.TryGetProperty(path, out var item)
            && item.TryGetProperty(method.ToLowerInvariant(), out operation);
    }

    /// <summary>The property names of every schema an operation's request body refers to.</summary>
    /// <param name="document">The document's root.</param>
    /// <param name="operation">The operation.</param>
    /// <returns>The names, from every content type of the body.</returns>
    public static IReadOnlyList<string> BodyProperties(JsonElement document, JsonElement operation)
    {
        var names = new List<string>();
        if (!operation.TryGetProperty("requestBody", out var body) || !body.TryGetProperty("content", out var content))
        {
            return names;
        }

        foreach (var media in content.EnumerateObject())
        {
            if (!media.Value.TryGetProperty("schema", out var schema))
            {
                continue;
            }

            names.AddRange(PropertiesOf(document, schema));
        }

        return names;
    }

    /// <summary>The property names of a schema, following one <c>$ref</c> into the components.</summary>
    /// <param name="document">The document's root.</param>
    /// <param name="schema">A schema or a reference to one.</param>
    /// <returns>The names declared under <c>properties</c>.</returns>
    private static List<string> PropertiesOf(JsonElement document, JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            var id = reference.GetString()!.Split('/')[^1];
            if (!document.TryGetProperty("components", out var components)
                || !components.GetProperty("schemas").TryGetProperty(id, out schema))
            {
                return [];
            }
        }

        if (!schema.TryGetProperty("properties", out var properties))
        {
            return [];
        }

        return properties.EnumerateObject().Select(property => { return property.Name; }).ToList();
    }
}
