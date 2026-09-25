using System.ComponentModel.DataAnnotations;

namespace Maran.Modules.Identity.Options;

/// <summary>
/// What Identity's outbound mail needs to know about the panel it is sent from: its own public
/// address, used to build the link in a password-reset mail and in an invitation mail alike.
/// Validated at startup (rules/csharp.md "Options validated at startup").
/// </summary>
/// <remarks>
/// <para>
/// <b>Bound from the <c>PasswordReset</c> section on purpose, despite the name.</b> This type used
/// to be <c>PasswordResetOptions</c> — the panel's public address was the only thing that type held,
/// because the reset link was the first place that needed it. The invitation mail
/// (<see cref="Maran.Modules.Identity.Services.InvitationMailComposer"/>) needed the identical
/// address for the identical reason, which is what moved the field to its own type: the panel's
/// public address is not a password-reset concern, and a second copy — or a third, the next time
/// something needs it — is exactly what a dedicated type avoids.
/// </para>
/// <para>
/// The configuration KEY did not move with it. <c>PasswordReset:PanelUrl</c> — <c>PasswordReset__PanelUrl</c>
/// as an environment variable — is unchanged, and this type still binds to the <c>PasswordReset</c>
/// section rather than a new <c>Panel</c> one, because that key already ships: the repository's own
/// <c>.env.example</c> sets it, and it is named throughout
/// docs/superpowers/notes/2026-09-04-cron-firewall-monitoring-threat-note.md. Renaming the section
/// would silently blank the address on every environment that already sets it — the panel would keep
/// starting (an empty address is valid configuration, see below) but every reset and invitation mail
/// would fall back to the bare-token instruction with nobody told why. Keeping the key is the whole
/// of the migration.
/// </para>
/// <para>
/// <b>Why the panel's own address is configuration and not derived from the request.</b> The obvious
/// source for the link in the mail is the request that asked for it — its scheme and its <c>Host</c>
/// header. That header is supplied by the CALLER. An attacker who asks for a reset of somebody else's
/// account while sending <c>Host: evil.example</c> would have the panel compose a mail, in the
/// panel's own name, containing a live token pointed at their server; the victim clicks it and hands
/// over their account. Host-header injection into a reset link is one of the best-documented ways to
/// turn a correct token implementation into an account takeover, and the only defence is to never
/// read the value the attacker controls. The same reasoning holds for the invitation link, which
/// carries a token with the identical shape and the identical value to a forger.
/// </para>
/// <para>
/// <b>Empty is a supported configuration and is not a failure.</b> A panel whose public address the
/// operator has not told it about still sends the mail — with the token and the path to paste it
/// into, rather than a clickable link. That is worse to use and completely safe, which is the right
/// trade for a value nobody has supplied.
/// </para>
/// </remarks>
public sealed class PanelOptions
{
    /// <summary>
    /// Configuration section this type binds from — kept as <c>PasswordReset</c>, not renamed to
    /// match this type; see the remarks above for why.
    /// </summary>
    public const string SectionName = "PasswordReset";

    /// <summary>
    /// The panel's own public address, as an absolute <c>https://</c> URL with no trailing path —
    /// for example <c>https://panel.example.com</c>. Empty when the operator has not configured one.
    /// </summary>
    [MaxLength(2048)]
    public string PanelUrl { get; set; } = string.Empty;
}
