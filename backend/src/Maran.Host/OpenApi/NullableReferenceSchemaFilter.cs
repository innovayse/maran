using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Maran.Host.OpenApi;

/// <summary>
/// States that a member whose value is another named schema may be <c>null</c> when its C# type says so.
/// </summary>
/// <remarks>
/// OpenAPI 3.0 forbids a sibling next to <c>$ref</c>, so the generator has nowhere to put
/// <c>nullable</c> on a member that points at a component and silently drops it: a <c>TaskFailureDto?</c>
/// is described exactly like a <c>TaskFailureDto</c>, and a body that is correct on the wire (the
/// serializer writes every null as <c>null</c>) disagrees with its own contract. The 3.0 spelling is a
/// one-element <c>allOf</c> beside <c>nullable: true</c>, which this filter writes for every member that
/// is a reference and is declared nullable - a nullable reference type, or a nullable enum or struct.
/// A member that is not nullable keeps its bare <c>$ref</c>, so the document still says which are which.
/// </remarks>
public sealed class NullableReferenceSchemaFilter : ISchemaFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties.Count == 0)
        {
            return;
        }

        var nullability = new NullabilityInfoContext();
        foreach (var property in context.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || !IsNullable(property, nullability))
            {
                continue;
            }

            var name = WireNameOf(property);
            if (schema.Properties.TryGetValue(name, out var member) && member.Reference is not null)
            {
                schema.Properties[name] = new OpenApiSchema { Nullable = true, AllOf = [member] };
            }
        }
    }

    /// <summary>Reads whether the member's declared type admits <c>null</c>.</summary>
    /// <param name="property">The member.</param>
    /// <param name="nullability">The reader of the compiler's nullable annotations.</param>
    /// <returns>True for <c>T?</c> of a reference type and for <c>Nullable&lt;T&gt;</c>.</returns>
    private static bool IsNullable(PropertyInfo property, NullabilityInfoContext nullability)
    {
        if (property.PropertyType.IsValueType)
        {
            return Nullable.GetUnderlyingType(property.PropertyType) is not null;
        }

        return nullability.Create(property).ReadState == NullabilityState.Nullable;
    }

    /// <summary>Names the member the way the panel's serializer writes it.</summary>
    /// <param name="property">The member.</param>
    /// <returns>The explicit <c>JsonPropertyName</c>, or the camel-case spelling of the member name.</returns>
    private static string WireNameOf(PropertyInfo property)
    {
        return property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
            ?? JsonNamingPolicy.CamelCase.ConvertName(property.Name);
    }
}
