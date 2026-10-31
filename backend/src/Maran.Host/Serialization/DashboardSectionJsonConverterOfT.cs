using System.Text.Json;
using System.Text.Json.Serialization;
using Maran.Host.Dashboard;

namespace Maran.Host.Serialization;

/// <summary>
/// Writes a <see cref="DashboardSection{T}"/> as the union the SPA reads —
/// <c>{"state":"loaded","data":…}</c> or <c>{"state":"unavailable"}</c> — and reads it back.
/// </summary>
/// <remarks>
/// An unavailable section has NO <c>data</c> member at all, rather than a null or an empty one: a
/// member that is present only in one case cannot be mistaken for an answer in the other. The state
/// is written through the caller's options, so it is spelled by the panel-wide camelCase enum
/// converter like every other state on the wire, and the member names follow the options' naming
/// policy.
/// </remarks>
/// <typeparam name="T">What the section carries when it was read.</typeparam>
public sealed class DashboardSectionJsonConverter<T> : JsonConverter<DashboardSection<T>>
{
    /// <summary>The member carrying the section's state, before the naming policy.</summary>
    private const string StateMember = nameof(DashboardSection<T>.State);

    /// <summary>The member carrying a loaded section's data, before the naming policy.</summary>
    private const string DataMember = "Data";

    /// <summary>Reads a section written by <see cref="Write"/>.</summary>
    /// <param name="reader">The reader, positioned on the section's object.</param>
    /// <param name="typeToConvert">The closed section type.</param>
    /// <param name="options">The serializer's options.</param>
    /// <returns>The section the object describes.</returns>
    /// <exception cref="JsonException">
    /// The object is not a section: no state, a loaded section without data, or data on an
    /// unavailable one.
    /// </exception>
    public override DashboardSection<T> Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A dashboard section is a JSON object.");
        }

        var stateName = MemberName(StateMember, options);
        var dataName = MemberName(DataMember, options);
        DashboardSectionState? state = null;
        var hasData = false;
        T data = default!;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var member = reader.GetString();
            reader.Read();
            if (string.Equals(member, stateName, StringComparison.Ordinal))
            {
                state = JsonSerializer.Deserialize<DashboardSectionState>(ref reader, options);
            }
            else if (string.Equals(member, dataName, StringComparison.Ordinal))
            {
                data = JsonSerializer.Deserialize<T>(ref reader, options)!;
                hasData = true;
            }
            else
            {
                reader.Skip();
            }
        }

        return (state, hasData) switch
        {
            (DashboardSectionState.Loaded, true) => DashboardSection.Loaded<T>(data),
            (DashboardSectionState.Unavailable, false) => DashboardSection.Unavailable<T>(),
            _ => throw new JsonException("A dashboard section carries data exactly when it is loaded."),
        };
    }

    /// <summary>Writes the section as its state and, when loaded, its data.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The section.</param>
    /// <param name="options">The serializer's options, whose enum converter spells the state.</param>
    public override void Write(Utf8JsonWriter writer, DashboardSection<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(MemberName(StateMember, options));
        JsonSerializer.Serialize(writer, value.State, options);
        value.Switch(
            data =>
            {
                writer.WritePropertyName(MemberName(DataMember, options));
                JsonSerializer.Serialize(writer, data, options);
            },
            () =>
            {
                // An unavailable section has no data member at all; see the remarks.
            });
        writer.WriteEndObject();
    }

    /// <summary>Spells a member name the way the options spell every other member.</summary>
    /// <param name="name">The member name before the naming policy.</param>
    /// <param name="options">The serializer's options.</param>
    /// <returns>The name as written on the wire.</returns>
    private static string MemberName(string name, JsonSerializerOptions options)
    {
        return options.PropertyNamingPolicy?.ConvertName(name) ?? name;
    }
}
