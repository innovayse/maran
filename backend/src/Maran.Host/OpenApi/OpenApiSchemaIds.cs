using System.Text;

namespace Maran.Host.OpenApi;

/// <summary>
/// The component name a type is given in the OpenAPI document.
/// </summary>
/// <remarks>
/// The full name, not the short one: sixteen modules each own DTOs, and two of them may one day name a
/// type alike. A generator that used the short name would fail the emission on the day that happens, or
/// worse, fold two shapes into one. The <c>Maran.</c> prefix is dropped because every type here has it,
/// and a generic is written <c>PagedResultOf…</c> so the name stays inside the characters a component
/// name may hold (<c>A-Z a-z 0-9 . - _</c>).
/// </remarks>
public static class OpenApiSchemaIds
{
    /// <summary>The prefix every type of this product carries.</summary>
    private const string ProductPrefix = "Maran.";

    /// <summary>Names a type.</summary>
    /// <param name="type">The type a schema is generated for.</param>
    /// <returns>A deterministic, document-wide unique name for it.</returns>
    public static string Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        var builder = new StringBuilder();
        Append(builder, type);
        return builder.ToString();
    }

    /// <summary>Appends the name of one type, recursing into the arguments of a generic.</summary>
    /// <param name="builder">The name being built.</param>
    /// <param name="type">The type to append.</param>
    private static void Append(StringBuilder builder, Type type)
    {
        var fullName = type.IsGenericType ? type.GetGenericTypeDefinition().FullName : type.FullName;
        var name = (fullName ?? type.Name).Replace('+', '.');
        if (name.StartsWith(ProductPrefix, StringComparison.Ordinal))
        {
            name = name[ProductPrefix.Length..];
        }

        var tick = name.IndexOf('`', StringComparison.Ordinal);
        builder.Append(tick >= 0 ? name[..tick] : name);

        if (!type.IsGenericType)
        {
            return;
        }

        builder.Append("Of");
        var arguments = type.GetGenericArguments();
        for (var index = 0; index < arguments.Length; index++)
        {
            if (index > 0)
            {
                builder.Append("And");
            }

            Append(builder, arguments[index]);
        }
    }
}
