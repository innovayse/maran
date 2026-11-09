using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// A freshly minted invitation as it exists for the instant between being issued and being handed
/// to whoever asked: the login it belongs to, and the plaintext token.
/// </summary>
/// <remarks>
/// The token is a <see cref="SensitiveString"/> because this is a <c>record</c>, and a record's
/// generated <c>ToString()</c> prints every property: a plain string here would leak the credential
/// the first time anyone logged, interpolated or threw this value. It is never stored, never
/// journalled and never serialized — it goes to a mail (<c>ResendInvitationCommandHandler</c>) or to
/// the administrator who asked for the link (<c>IssueInvitationLinkCommandHandler</c>), and only
/// there does anything call <see cref="SensitiveString.Reveal"/>.
/// </remarks>
/// <param name="Username">The login's name, which is also the hosting account's system user.</param>
/// <param name="RecipientEmail">
/// The invited login's own stored address, where a mail would be sent: the account owner's for an
/// account invitation, the reseller contact's for a reseller invitation.
/// </param>
/// <param name="Token">The plaintext token. Its digest, not it, is what the database holds.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
/// <param name="RecipientLanguage">
/// The recipient's language, which a mailed invitation is written in: the account owner's language
/// for an account invitation, the reseller contact's for a reseller invitation.
/// </param>
public sealed record IssuedInvitation(
    string Username,
    string RecipientEmail,
    SensitiveString Token,
    DateTimeOffset ExpiresAt,
    string RecipientLanguage);
