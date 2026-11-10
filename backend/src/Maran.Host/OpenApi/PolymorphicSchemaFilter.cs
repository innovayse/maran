using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Maran.Host.OpenApi;

/// <summary>
/// Describes a closed family of DTOs the way the serializer writes it: the base is "exactly one of its
/// derived shapes", and each derived shape carries the discriminator member with its fixed value.
/// </summary>
/// <remarks>
/// <para>
/// The wire is decided by <c>[JsonPolymorphic]</c> and <c>[JsonDerivedType]</c>, which the generator
/// does not read: left alone it describes the abstract base as an empty object, so a document built from
/// it would accept any body and name no member of any case. This filter reads the same attributes the
/// serializer does and restates them, so the schema moves when the family does and cannot be a second,
/// hand-kept list of cases.
/// </para>
/// <para>
/// The base becomes a <c>oneOf</c> of the derived schemas with a discriminator naming the member and the
/// value of each case. It keeps no members of its own: every member a derived record declares, including
/// the ones it inherits from the base, is on the derived schema, which is where the serializer writes it.
/// Only a case with a discriminator value is a case; the serializer refuses to write one without.
/// </para>
/// </remarks>
public sealed class PolymorphicSchemaFilter : ISchemaFilter
{
    /// <summary>The discriminator member the serializer uses when the attribute names none.</summary>
    private const string DefaultDiscriminatorName = "$type";

    /// <summary>The prefix of a reference to a named schema.</summary>
    private const string ComponentsPrefix = "#/components/schemas/";

    /// <inheritdoc />
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        var polymorphic = context.Type.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false);
        if (polymorphic is not null)
        {
            DescribeBase(schema, context, polymorphic);
            return;
        }

        DescribeDerived(schema, context.Type);
    }

    /// <summary>Turns the schema of a polymorphic base into the choice between its cases.</summary>
    /// <param name="schema">The base type's schema, changed in place.</param>
    /// <param name="context">The generation context, which generates each derived type's schema.</param>
    /// <param name="polymorphic">The attribute naming the discriminator member.</param>
    private static void DescribeBase(OpenApiSchema schema, SchemaFilterContext context, JsonPolymorphicAttribute polymorphic)
    {
        var discriminator = new OpenApiDiscriminator
        {
            PropertyName = polymorphic.TypeDiscriminatorPropertyName ?? DefaultDiscriminatorName,
        };
        var cases = new List<OpenApiSchema>();

        foreach (var derived in context.Type.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false))
        {
            var reference = context.SchemaGenerator.GenerateSchema(derived.DerivedType, context.SchemaRepository);
            cases.Add(reference);
            if (derived.TypeDiscriminator is not null && reference.Reference is not null)
            {
                discriminator.Mapping[derived.TypeDiscriminator.ToString()!] = ComponentsPrefix + reference.Reference.Id;
            }
        }

        schema.Type = null;
        schema.Properties.Clear();
        schema.Required.Clear();
        schema.AdditionalProperties = null;
        schema.AdditionalPropertiesAllowed = true;
        schema.OneOf = cases;
        schema.Discriminator = discriminator;
    }

    /// <summary>Adds the discriminator member to the schema of one case of a polymorphic family.</summary>
    /// <param name="schema">The derived type's schema, changed in place.</param>
    /// <param name="type">The derived type.</param>
    private static void DescribeDerived(OpenApiSchema schema, Type type)
    {
        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            var polymorphic = baseType.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false);
            if (polymorphic is null)
            {
                continue;
            }

            var tag = baseType.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
                .FirstOrDefault(candidate => { return candidate.DerivedType == type; });
            if (tag?.TypeDiscriminator is null)
            {
                return;
            }

            var name = polymorphic.TypeDiscriminatorPropertyName ?? DefaultDiscriminatorName;
            schema.Properties[name] = tag.TypeDiscriminator is int number
                ? new OpenApiSchema { Type = "integer", Enum = [new OpenApiInteger(number)] }
                : new OpenApiSchema { Type = "string", Enum = [new OpenApiString(tag.TypeDiscriminator.ToString()!)] };
            schema.Required.Add(name);
            return;
        }
    }
}
