using System.Globalization;
using System.Text.Json;

namespace Maran.Modules.Ssl.Domain.Policies;

/// <summary>
/// A small ordered JSON object of string members, able to serialize itself in the exact form RFC 7638
/// requires for a JWK thumbprint.
/// </summary>
/// <remarks>
/// It exists because "just serialize the dictionary" is not enough for a thumbprint and quietly
/// looks like it is. The thumbprint is a hash of a canonical serialization — the required members
/// only, sorted lexicographically by name, with no whitespace and no escaping beyond what JSON
/// demands — so a serializer that reorders members, or that pretty-prints, produces a different hash
/// and therefore a key authorization the authority rejects with no useful diagnostic. Writing the
/// canonical form here, once, makes that a property of the type rather than of each call site.
/// </remarks>
public sealed class JsonObjectValue
{
    /// <summary>The object's members, in the order they were supplied.</summary>
    public IReadOnlyDictionary<string, string> Members { get; }

    /// <summary>Creates the object over the given members.</summary>
    /// <param name="members">The members, whose values are all strings.</param>
    public JsonObjectValue(IReadOnlyDictionary<string, string> members)
    {
        Members = members;
    }

    /// <summary>Serializes to the canonical form a JWK thumbprint is computed over.</summary>
    /// <returns>Compact JSON with members sorted lexicographically by name.</returns>
    public string ToCanonicalJson()
    {
        var parts = Members
            .OrderBy(
                member =>
                {
                    return member.Key;
                },
                StringComparer.Ordinal)
            .Select(member =>
            {
                return string.Create(
                    CultureInfo.InvariantCulture,
                    $"{JsonSerializer.Serialize(member.Key)}:{JsonSerializer.Serialize(member.Value)}");
            });

        return string.Create(CultureInfo.InvariantCulture, $"{{{string.Join(",", parts)}}}");
    }
}
