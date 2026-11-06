using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Host.Tests.OpenApi.Fixtures;

/// <summary>
/// The members the server establishes — carrying the bare <c>[BindNever]</c> on a command's constructor
/// parameter (rules/csharp.md) — and the check that none of them reaches a request description.
/// </summary>
public static class GuardedMemberCensus
{
    /// <summary>
    /// The names, as they are spelled on the wire, of the guarded members of one action's parameter types.
    /// </summary>
    /// <param name="description">The action's API description.</param>
    /// <returns>Camel-cased member names; empty when no parameter type guards a member.</returns>
    public static IReadOnlySet<string> NamesFor(ApiDescription description)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in description.ActionDescriptor.Parameters)
        {
            foreach (var constructor in parameter.ParameterType.GetConstructors())
            {
                foreach (var constructorParameter in constructor.GetParameters())
                {
                    if (constructorParameter.GetCustomAttribute<BindNeverAttribute>() is not null
                        && constructorParameter.Name is not null)
                    {
                        names.Add(JsonNamingPolicy.CamelCase.ConvertName(constructorParameter.Name));
                    }
                }
            }
        }

        return names;
    }

    /// <summary>Lists the guarded members an operation still describes as something a caller may send.</summary>
    /// <param name="document">The document's root.</param>
    /// <param name="operation">The operation under test.</param>
    /// <param name="guarded">The guarded member names of the action behind it.</param>
    /// <returns>One line per leak: the place and the name. Empty when the operation is clean.</returns>
    public static IReadOnlyList<string> Leaks(JsonElement document, JsonElement operation, IReadOnlySet<string> guarded)
    {
        var leaks = new List<string>();
        if (operation.TryGetProperty("parameters", out var parameters))
        {
            foreach (var parameter in parameters.EnumerateArray())
            {
                var location = parameter.GetProperty("in").GetString();
                var name = parameter.GetProperty("name").GetString() ?? string.Empty;
                if (location != "path" && guarded.Contains(name))
                {
                    leaks.Add($"{location} parameter {name}");
                }
            }
        }

        foreach (var property in OpenApiReader.BodyProperties(document, operation))
        {
            if (guarded.Contains(property))
            {
                leaks.Add($"body property {property}");
            }
        }

        return leaks;
    }
}
