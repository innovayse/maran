namespace Maran.Modules.Identity.Common;

/// <summary>
/// What the panel tells the SPA about the view-as session the current token belongs to: whom the
/// operator is viewing as, who the operator is, and when it ends (issues #317, #314). The SPA's
/// "You are viewing as" banner is drawn from this and nothing else.
/// </summary>
/// <param name="AccountId">The account being viewed.</param>
/// <param name="AccountName">The account's name, as the banner shows it.</param>
/// <param name="Username">The login being viewed as.</param>
/// <param name="OperatorUsername">The operator viewing, so the banner can say who will be returned to.</param>
/// <param name="ExpiresAt">When the view-as session ends on its own.</param>
public sealed record ViewingAsDto(
    Guid AccountId,
    string AccountName,
    string Username,
    string OperatorUsername,
    DateTimeOffset ExpiresAt);
