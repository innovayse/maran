using System.Reflection;
using Maran.Sdk.Contracts;

namespace Maran.Sdk.Manifests;

/// <summary>
/// The names a module may list in <see cref="Manifest.Permissions"/>: every authorization policy the
/// Sdk names, read from the constants themselves so a policy added to either class is in the
/// vocabulary without anyone extending a list.
/// </summary>
/// <remarks>
/// The vocabulary is the Sdk's because the Host defines what each policy requires and a module can
/// never reference the Host. A module cannot add a name: a policy of its own needs a Host
/// registration mechanism that does not exist (issue #182).
/// </remarks>
public static class ManifestPermissionVocabulary
{
    /// <summary>Every policy name in <see cref="AuthorizationPolicies"/> and <see cref="ApiKeyPolicyNames"/>.</summary>
    public static IReadOnlySet<string> Policies { get; } =
        Read(typeof(AuthorizationPolicies), typeof(ApiKeyPolicyNames));

    /// <summary>Reads the string constants out of the given classes.</summary>
    /// <param name="holders">The static classes whose public string constants are policy names.</param>
    /// <returns>The distinct constant values.</returns>
    private static HashSet<string> Read(params Type[] holders)
    {
        return holders
            .SelectMany(holder =>
            {
                return holder.GetFields(BindingFlags.Public | BindingFlags.Static);
            })
            .Where(field =>
            {
                return field.IsLiteral && field.FieldType == typeof(string);
            })
            .Select(field =>
            {
                return (string)field.GetRawConstantValue()!;
            })
            .ToHashSet(StringComparer.Ordinal);
    }
}
