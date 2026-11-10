using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Recognises the endpoints a single-use export grant reaches and a signed-in login never does (issue #127).
/// </summary>
/// <remarks>
/// A walk that holds every endpoint to a rule about a signed-in LOGIN (a password change owed, an
/// enrolment owed) has nothing to say about these: they name the <c>MigrationGrant</c> scheme, whose
/// handler reads a grant and not a bearer token, so the answer is 401 whoever holds a session. That
/// 401 discloses nothing and cannot be used to get around the rule, so a walk leaves them out
/// the way it leaves out the endpoints only an API key reaches.
/// </remarks>
public static class MigrationGrantEndpoint
{
    /// <summary>Whether an endpoint names the migration-grant scheme and its pull policy, and nothing a login could satisfy.</summary>
    /// <param name="endpoint">The endpoint to classify.</param>
    /// <returns><c>true</c> when every authorize declaration of the endpoint is the grant's scheme and pull policy.</returns>
    public static bool IsGrantOnly(RouteEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        var declared = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();

        return declared.Count > 0 && declared.All(item =>
        {
            return string.Equals(item.AuthenticationSchemes, MigrationGrantDefaults.Scheme, StringComparison.Ordinal)
                && string.Equals(item.Policy, MigrationGrantPolicyNames.Pull, StringComparison.Ordinal);
        });
    }
}
