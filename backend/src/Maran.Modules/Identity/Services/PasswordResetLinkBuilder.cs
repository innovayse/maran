using Maran.Modules.Identity.Domain.Policies;
using Maran.Modules.Identity.Options;
using Microsoft.Extensions.Options;

namespace Maran.Modules.Identity.Services;

/// <summary>
/// Builds the address a password-reset token is redeemed at, for the mailed link and for the link
/// the break-glass console prints.
/// </summary>
/// <remarks>
/// <para>
/// <b>One place writes the path, so the two links cannot drift.</b> The reset mail and the
/// administrator's console recovery both hand a person a link the SPA's reset screen accepts; if each
/// assembled its own, renaming the screen's route would fix one and silently break the other.
/// </para>
/// <para>
/// <b>The host comes from configuration, never from a request.</b> The absolute form is built from
/// <see cref="PanelOptions.PublicUrl"/> through <see cref="PanelLink"/>, for the host-header-injection reason
/// <see cref="InvitationLinkBuilder"/> states. With no public address configured the answer is a
/// root-relative path and <see cref="HasPublicAddress"/> says so, so a caller can tell the person the
/// link has no host rather than presenting a fragment as a complete address.
/// </para>
/// </remarks>
public sealed class PasswordResetLinkBuilder
{
    /// <summary>The panel's own public address, if one is configured.</summary>
    private readonly PanelOptions _options;

    /// <summary>Creates the builder.</summary>
    /// <param name="options">The panel's own public address.</param>
    public PasswordResetLinkBuilder(IOptions<PanelOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>Whether a public address is configured, so <see cref="Build"/> answers an absolute link.</summary>
    public bool HasPublicAddress
    {
        get { return PanelLink.IsConfigured(_options.PublicUrl); }
    }

    /// <summary>Builds the root-relative path that redeems a reset token.</summary>
    /// <param name="token">The plaintext reset token.</param>
    /// <returns>The path and query, ready to be appended to the panel's address.</returns>
    public static string RelativeFor(string token)
    {
        return $"/reset-password?token={Uri.EscapeDataString(token)}";
    }

    /// <summary>Builds the link to hand over: absolute when the panel has a public address, else root-relative.</summary>
    /// <param name="token">The plaintext reset token.</param>
    /// <returns>The link. It embeds the token, so it is exactly as secret as the token is.</returns>
    public string Build(string token)
    {
        return PanelLink.Compose(_options.PublicUrl, RelativeFor(token));
    }
}
