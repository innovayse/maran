namespace Maran.Modules.Identity.Domain.Enums;

/// <summary>How a login stands towards one hosting account it may act for.</summary>
public enum MembershipRole
{
    /// <summary>
    /// The login owns the account. An owner is not restricted by
    /// <see cref="Entities.AccountMembership.Grants"/>: they may use every feature the account's plan
    /// includes.
    /// </summary>
    Owner,

    /// <summary>
    /// The login was added to the account by its owner. A member may use only the features named in
    /// <see cref="Entities.AccountMembership.Grants"/>, and only those the account's plan includes.
    /// </summary>
    Member,
}
