using Maran.Modules.Identity.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Builds the address an invited owner opens to set their first password.
/// </summary>
/// <remarks>
/// <para>
/// <b>The host comes from configuration, never from the request.</b> The absolute form is built
/// from <see cref="PanelOptions.PanelUrl"/>, the panel's own configured public address, for the
/// host-header-injection reason <see cref="InvitationMailComposer"/> states: a link assembled from
/// the incoming <c>Host</c> header would let a caller aim an administrator's hand-delivered
/// credential at a server the caller controls.
/// </para>
/// <para>
/// <b>With no public address configured the answer is a root-relative path, not a guess.</b> The
/// SPA that shows the link to the administrator knows the origin the administrator is actually
/// looking at, and prefixes it; the server has no honest way to know it.
/// </para>
/// <para>
/// The path itself is kept here and nowhere else, so the mailed link and the displayed link cannot
/// drift apart.
/// </para>
/// </remarks>
public sealed class InvitationLinkBuilder
{
    /// <summary>The panel's own public address, if one is configured.</summary>
    private readonly PanelOptions _options;

    /// <summary>Creates the builder.</summary>
    /// <param name="options">The panel's own public address.</param>
    public InvitationLinkBuilder(IOptions<PanelOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Builds the root-relative path that accepts an invitation.</summary>
    /// <param name="token">The plaintext invitation token.</param>
    /// <returns>The path and query, ready to be appended to the panel's address.</returns>
    public static string RelativeFor(string token)
    {
        return $"/accept-invitation?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>Builds the link to hand over: absolute when the panel has a public address, else root-relative.</summary>
    /// <param name="token">The plaintext invitation token.</param>
    /// <returns>The link. It embeds the token, so it is exactly as secret as the token is.</returns>
    public string Build(string token)
    {
        if (string.IsNullOrWhiteSpace(_options.PanelUrl))
        {
            return RelativeFor(token);
        }

        return $"{_options.PanelUrl.TrimEnd('/')}{RelativeFor(token)}";
    }
}
