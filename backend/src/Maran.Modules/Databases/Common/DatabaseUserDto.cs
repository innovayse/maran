namespace Maran.Modules.Databases.Common;

/// <summary>One database user as a list shows it. There is no password and no host list.</summary>
/// <param name="Id">The user's identity, and the only identifier a request may name.</param>
/// <param name="AccountId">The account that owns the user.</param>
/// <param name="OwnerName">The owning account's current short name; empty when the panel does not name it for this caller.</param>
/// <param name="NameSuffix">The name chosen at creation, without the account prefix.</param>
/// <param name="FullName">The fully-qualified name MySQL holds.</param>
/// <param name="IsDedicated">Whether this is a database's dedicated user, which cannot be dropped alone.</param>
/// <param name="Grants">What the user holds, one entry per database it has any privilege on.</param>
/// <param name="CreatedAt">The instant the user was created.</param>
public sealed record DatabaseUserDto(
    Guid Id,
    Guid AccountId,
    string OwnerName,
    string NameSuffix,
    string FullName,
    bool IsDedicated,
    IReadOnlyList<DatabaseUserGrantDto> Grants,
    DateTimeOffset CreatedAt);
