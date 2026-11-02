using Maran.SharedKernel.Security;

namespace Maran.Modules.Identity.Models;

/// <summary>
/// A freshly minted invitation token, as <c>InvitationIssuer.MintAsync</c> hands it to the one caller
/// that mails it or shows it. The plaintext exists here and nowhere else; the database holds its digest.
/// </summary>
/// <param name="Token">The plaintext token, wrapped so a record's printing members cannot leak it.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
public sealed record MintedInvitationToken(SensitiveString Token, DateTimeOffset ExpiresAt);
