using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Host.Dashboard;

namespace Maran.Host.Serialization;

/// <summary>
/// Hands out the <see cref="DashboardSectionJsonConverter{T}"/> for each closed
/// <see cref="DashboardSection{T}"/> the serializer meets.
/// </summary>
/// <remarks>
/// A factory because an attribute cannot name a converter for an open generic type: the
/// <see cref="JsonConverterAttribute"/> on <see cref="DashboardSection{T}"/> names this, and this
/// closes the converter over the section's own <c>T</c>.
/// </remarks>
public sealed class DashboardSectionJsonConverterFactory : JsonConverterFactory
{
    /// <summary>Whether <paramref name="typeToConvert"/> is a closed dashboard section.</summary>
    /// <param name="typeToConvert">The type the serializer asks about.</param>
    /// <returns><c>true</c> for <see cref="DashboardSection{T}"/> of any <c>T</c>.</returns>
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(DashboardSection<>);
    }

    /// <summary>Creates the converter for one closed section type.</summary>
    /// <param name="typeToConvert">A closed <see cref="DashboardSection{T}"/>.</param>
    /// <param name="options">The serializer's options; unused at creation.</param>
    /// <returns>The converter for that section's <c>T</c>.</returns>
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(DashboardSectionJsonConverter<>)
            .MakeGenericType(typeToConvert.GetGenericArguments()[0]);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}
