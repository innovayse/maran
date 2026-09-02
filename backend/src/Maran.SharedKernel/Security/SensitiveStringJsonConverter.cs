using System.Text.Json;
using System.Text.Json.Serialization;

namespace Maran.SharedKernel.Security;

/// <summary>
/// Renders a <see cref="SensitiveString"/> as its plain value in JSON, and reads one back.
/// </summary>
/// <remarks>
/// <para>
/// A secret that is never printable is also never deliverable, and a freshly minted database or SFTP
/// password has exactly one legitimate destination: the single response that shows it to the
/// customer who asked for it. This converter is that destination, and it is the JSON counterpart of
/// <see cref="SensitiveString.Reveal"/> — a deliberate, named, greppable act rather than something
/// the serializer does on its own.
/// </para>
/// <para>
/// Without it a DTO would have to carry the value as a plain <see cref="string"/>, which is the leak
/// <see cref="SensitiveString"/> exists to close: a <c>record</c>'s generated <c>ToString()</c>
/// prints every property, so the first thing that logs such a DTO logs the password. With the value
/// wrapped, that logging path renders <c>[redacted]</c> and only the serializer — asked explicitly,
/// on one response — produces the real thing.
/// </para>
/// <para>
/// The read side exists so the type round-trips rather than throwing somewhere unexpected. It is not
/// an invitation: nothing in this product accepts a customer-chosen database password, because the
/// panel mints them.
/// </para>
/// </remarks>
public sealed class SensitiveStringJsonConverter : JsonConverter<SensitiveString>
{
    /// <summary>Reads a JSON string into a <see cref="SensitiveString"/>.</summary>
    /// <param name="reader">The reader positioned on the value.</param>
    /// <param name="typeToConvert">The target type; always <see cref="SensitiveString"/>.</param>
    /// <param name="options">The serializer options in force.</param>
    /// <returns>The wrapped value, or null for a JSON null.</returns>
    public override SensitiveString? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        var value = reader.GetString();

        return value is null ? null : new SensitiveString(value);
    }

    /// <summary>Writes the wrapped value as a JSON string.</summary>
    /// <param name="writer">The writer to emit into.</param>
    /// <param name="value">The secret being delivered.</param>
    /// <param name="options">The serializer options in force.</param>
    public override void Write(Utf8JsonWriter writer, SensitiveString value, JsonSerializerOptions options)
    {
        // The one place a secret becomes wire text without a call to Reveal(), and it is this type's
        // whole reason to exist. ToString() would write "[redacted]" and hand the customer a
        // password they cannot use.
        writer.WriteStringValue(value.Reveal());
    }
}
