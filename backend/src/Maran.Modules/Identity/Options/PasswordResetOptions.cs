using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Identity.Options;

/// <summary>
/// What the password-reset mail needs to know about the panel it is sent from. Bound from the
/// <c>PasswordReset</c> configuration section and validated at startup (rules/csharp.md "Options
/// validated at startup").
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the panel's own address is configuration and not derived from the request.</b> The obvious
/// source for the link in the mail is the request that asked for the reset — its scheme and its
/// <c>Host</c> header. That header is supplied by the CALLER. An attacker who asks for a reset of
/// somebody else's account while sending <c>Host: evil.example</c> would have the panel compose a
/// mail, in the panel's own name, containing a live token pointed at their server; the victim clicks
/// it and hands over their account. Host-header injection into a reset link is one of the
/// best-documented ways to turn a correct token implementation into an account takeover, and the
/// only defence is to never read the value the attacker controls.
/// </para>
/// <para>
/// <b>Empty is a supported configuration and is not a failure.</b> A panel whose public address the
/// operator has not told it about still sends the mail — with the token and the path to paste it
/// into, rather than a clickable link. That is worse to use and completely safe, which is the right
/// trade for a value nobody has supplied.
/// </para>
/// </remarks>
public sealed class PasswordResetOptions
{
    /// <summary>Configuration section this type binds from.</summary>
    public const string SectionName = "PasswordReset";

    /// <summary>
    /// The panel's own public address, as an absolute <c>https://</c> URL with no trailing path —
    /// for example <c>https://panel.example.com</c>. Empty when the operator has not configured one.
    /// </summary>
    [MaxLength(2048)]
    public string PanelUrl { get; set; } = string.Empty;
}
