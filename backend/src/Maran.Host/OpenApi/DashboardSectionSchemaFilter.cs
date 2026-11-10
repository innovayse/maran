using System.Text.Json;
using Maran.Host.Dashboard;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Maran.Host.OpenApi;

/// <summary>
/// Describes a dashboard section as the converter writes it: either a <c>loaded</c> object carrying
/// <c>data</c>, or an <c>unavailable</c> one carrying nothing else.
/// </summary>
/// <remarks>
/// <see cref="DashboardSection{T}"/> is written by its own converter, so the generator sees a type with
/// no members and describes an empty object. The two cases are restated here from the same enum the
/// converter writes, and the data member exists only in the loaded one, which is the property the type
/// exists to guarantee: a figure that could not be taken is never a zero.
/// </remarks>
public sealed class DashboardSectionSchemaFilter : ISchemaFilter
{
    /// <summary>The name of the member that tells the two cases apart.</summary>
    private const string StateMember = "state";

    /// <summary>The name of the member that carries the section's content.</summary>
    private const string DataMember = "data";

    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (!context.Type.IsGenericType || context.Type.GetGenericTypeDefinition() != typeof(DashboardSection<>))
        {
            return;
        }

        var data = context.SchemaGenerator.GenerateSchema(context.Type.GetGenericArguments()[0], context.SchemaRepository);

        var loaded = new OpenApiSchema
        {
            Type = "object",
            AdditionalPropertiesAllowed = false,
            Properties = new Dictionary<string, OpenApiSchema>
            {
                [StateMember] = StateSchema(DashboardSectionState.Loaded),
                [DataMember] = data,
            },
            Required = new SortedSet<string>(StringComparer.Ordinal) { StateMember, DataMember },
        };
        var unavailable = new OpenApiSchema
        {
            Type = "object",
            AdditionalPropertiesAllowed = false,
            Properties = new Dictionary<string, OpenApiSchema>
            {
                [StateMember] = StateSchema(DashboardSectionState.Unavailable),
            },
            Required = new SortedSet<string>(StringComparer.Ordinal) { StateMember },
        };

        schema.Type = null;
        schema.Properties.Clear();
        schema.Required.Clear();
        schema.AdditionalProperties = null;
        schema.AdditionalPropertiesAllowed = true;
        schema.OneOf = [loaded, unavailable];
    }

    /// <summary>Describes the <c>state</c> member of one case.</summary>
    /// <param name="state">The case.</param>
    /// <returns>A string schema accepting exactly the spelling the converter writes for the case.</returns>
    private static OpenApiSchema StateSchema(DashboardSectionState state)
    {
        var name = JsonNamingPolicy.CamelCase.ConvertName(state.ToString());
        return new OpenApiSchema { Type = "string", Enum = [new OpenApiString(name)] };
    }
}
