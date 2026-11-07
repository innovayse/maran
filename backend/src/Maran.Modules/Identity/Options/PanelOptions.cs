using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Identity.Options;

/// <summary>
/// What Identity's outbound mail needs to know about the panel it is sent from: its own public
/// address, the base of every link the panel hands out — the invitation of an account owner, of a
/// reseller and of a sub-user, and a password reset. Validated at startup
/// (<see cref="Maran.Modules.Identity.Validators.PanelOptionsValidator"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Bound from the <c>Panel</c> section, key <c>PublicUrl</c> (<c>Panel__PublicUrl</c> in
/// <c>panel.env</c>).</b> The installer writes it from the address the operator actually reaches the
/// panel at (installer/lib/60-config.sh <c>detect_panel_public_url</c>), which is the address it
/// prints at the end of the install. The same section also holds <c>HostName</c>, which the Host
/// reads for a different purpose (the name a customer may not claim); the two keys are independent.
/// The key used to be <c>PasswordReset:PanelUrl</c>, a name left over from when a reset link was the
/// only reader; it is no longer read, and the installer rewrites <c>panel.env</c> without it.
/// </para>
/// <para>
/// <b>Why the panel's own address is configuration and not derived from the request.</b> The obvious
/// source for the link in the mail is the request that asked for it — its scheme and its <c>Host</c>
/// header. That header is supplied by the CALLER. An attacker who asks for a reset of somebody else's
/// account while sending <c>Host: evil.example</c> would have the panel compose a mail, in the
/// panel's own name, containing a live token pointed at their server; the victim clicks it and hands
/// over their account. Host-header injection into a reset link is one of the best-documented ways to
/// turn a correct token implementation into an account takeover, and the only defence is to never
/// read the value the attacker controls. The same holds for <c>X-Forwarded-Host</c>. The invitation
/// link carries a token with the identical shape and the identical value to a forger.
/// </para>
/// <para>
/// <b>Empty is a supported configuration and is not a failure.</b> A panel whose public address the
/// operator has not told it about still sends the mail — with the token and the path to paste it
/// into, rather than a clickable link — and logs a warning at start-up saying so. That is worse to
/// use and completely safe, which is the right trade for a value nobody has supplied. A value that is
/// present but malformed is the opposite case and stops the boot.
/// </para>
/// </remarks>
public sealed class PanelOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "Panel";

    /// <summary>
    /// The panel's own public origin: <c>https://</c>, a DNS name or IP address, an optional port, and
    /// nothing after it — for example <c>https://panel.example.com:8443</c>. Empty when the operator
    /// has not configured one. The accepted shape is <see cref="Maran.SharedKernel.Utilities.Network.PublicUrlRule"/>.
    /// </summary>
    [MaxLength(Maran.SharedKernel.Utilities.Network.PublicUrlRule.MaximumLength)]
    public string PublicUrl { get; set; } = string.Empty;
}
