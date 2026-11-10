using Maran.Sdk.Contracts;
using Microsoft.AspNetCore.Authorization;

namespace Maran.Host.Authorization;

/// <summary>
/// The authorization policy that admits an export grant: the <c>MigrationGrant</c> authentication scheme and the
/// claims only the grant handler writes (issue #127). Its name is <see cref="MigrationGrantPolicyNames"/>, so a
/// module can declare it on an endpoint without referencing the Host.
/// </summary>
/// <remarks>
/// These are, with <see cref="ApiKeyPolicies"/>, the only policies that name a scheme other than the default. Every
/// policy in <see cref="RolePolicies"/>, the fallback and the default name no scheme, so they authenticate with the
/// JWT scheme and a grant never satisfies them. Registering this changes neither the default scheme, the fallback
/// policy nor the default policy.
/// </remarks>
public static class MigrationGrantPolicies
{
    /// <summary>
    /// Registers the pull policy. A grant principal is recognised by its grant and export claims, which only the
    /// grant handler writes, so a JWT that merely carries a claim of that name cannot pass: the policy also names
    /// the scheme, and a policy that names a scheme is evaluated against that scheme's principal alone.
    /// </summary>
    /// <param name="options">The authorization options being configured.</param>
    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(MigrationGrantPolicyNames.Pull, policy =>
        {
            policy.AddAuthenticationSchemes(MigrationGrantDefaults.Scheme);
            policy.RequireAuthenticatedUser();
            policy.RequireClaim(PanelClaimTypes.MigrationGrantId);
            policy.RequireClaim(PanelClaimTypes.MigrationExportId);
        });
    }
}
