using Maran.Modules.Identity.Domain.Enums;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Identity.Common;

/// <summary>
/// One person an account's owner added to the account: who they are, whether they have set their
/// password yet, and which gated features they may use.
/// </summary>
/// <param name="Id">
/// The identifier a request names to act on the row: the member's login id, or, for an open offer, the
/// offer's id (removing it cancels the offer). It is never the offered login's id.
/// </param>
/// <param name="Username">The name they sign in with.</param>
/// <param name="Email">The address their invitation went to and their password resets will go to.</param>
/// <param name="State">
/// <see cref="UserLifecycle.Invited"/> until they accept the invitation and choose a password, then
/// <see cref="UserLifecycle.Active"/>. A suspension is never shown here: it belongs to the
/// membership of an account, and the owner who reads this list cannot be signed in to a suspended one.
/// </param>
/// <param name="Features">The single features they may use, one value per feature (never the bits).</param>
/// <param name="CreatedAt">When the owner added them.</param>
/// <param name="LastLoginAt">Their most recent sign-in, or <c>null</c> before the first.</param>
/// <param name="IsOffer">
/// <c>true</c> for a pending offer to a login that already exists, which becomes a member only once the
/// person accepts it; <c>false</c> for a member.
/// </param>
public sealed record SubUserDto(
    Guid Id,
    string Username,
    string Email,
    UserLifecycle State,
    IReadOnlyList<AccountFeature> Features,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool IsOffer);
