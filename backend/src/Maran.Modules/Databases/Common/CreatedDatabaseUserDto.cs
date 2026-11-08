using Maran.SharedKernel.Security;

namespace Maran.Modules.Databases.Common;

/// <summary>A database user as created, with the generated password, which no other response ever carries again.</summary>
/// <param name="Id">The new user's identity.</param>
/// <param name="AccountId">The account that owns it.</param>
/// <param name="NameSuffix">The name chosen, without the account prefix.</param>
/// <param name="FullName">The fully-qualified user name.</param>
/// <param name="Password">The generated password, shown once and stored nowhere.</param>
/// <param name="CreatedAt">The instant the user was created.</param>
public sealed record CreatedDatabaseUserDto(
    Guid Id,
    Guid AccountId,
    string NameSuffix,
    string FullName,
    SensitiveString Password,
    DateTimeOffset CreatedAt);
