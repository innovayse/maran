using Maran.SharedKernel.Utilities.Network;

namespace Maran.Modules.Licensing.Domain.Policies;

/// <summary>
/// The ONE rule of the link from the licence screen to the marketplace's claim page: which server ids may be
/// carried in it, and how it is spelled. Pure over its arguments, so the spelling has a unit test and the
/// service that reads the id holds no judgement of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only an id the marketplace would accept is put in a link.</b> The marketplace binds a licence to a server
/// named by 32 hexadecimal digits (<c>docs/marketplace/api-v1.md</c>); a machine id of any other shape is shown
/// on the screen as it was read, so the administrator can still copy it, but is never offered as a link that the
/// claim page would refuse. The digits are written in lower case, the one spelling the marketplace stores, while
/// the licence comparison itself ignores case (<see cref="LicenceServerBindingPolicy"/>).
/// </para>
/// <para>
/// <b>Only a well-formed origin is linked to.</b> The base address goes through <see cref="PublicUrlRule"/>, so
/// a configured value with user information, a path, a scheme other than https or a control character yields no
/// link rather than a link that opens a different site than the one the administrator reads.
/// </para>
/// </remarks>
public static class LicenceClaimLinkPolicy
{
    /// <summary>The number of hexadecimal digits in a server id the marketplace accepts.</summary>
    public const int ServerIdLength = 32;

    /// <summary>The path of the claim page below the marketplace website's origin.</summary>
    public const string ClaimPath = "/claim";

    /// <summary>The query parameter that carries the server id to the claim page.</summary>
    public const string ServerParameter = "server";

    /// <summary>Builds the address of the claim page for one server.</summary>
    /// <param name="claimPageBaseUrl">The configured origin of the marketplace website.</param>
    /// <param name="serverId">The machine id as the licence comparison reads it, or null when it could not be read.</param>
    /// <returns>The link, or null when there is no id the marketplace accepts or no usable origin.</returns>
    public static string? Build(string? claimPageBaseUrl, string? serverId)
    {
        if (serverId is null || serverId.Length != ServerIdLength || !serverId.All(Uri.IsHexDigit))
        {
            return null;
        }

        if (!PublicUrlRule.TryNormalise(claimPageBaseUrl, out var origin))
        {
            return null;
        }

        return $"{origin}{ClaimPath}?{ServerParameter}={serverId.ToLowerInvariant()}";
    }
}
