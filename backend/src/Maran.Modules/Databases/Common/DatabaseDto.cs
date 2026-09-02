using Maran.Modules.Databases.Domain;

namespace Maran.Modules.Databases.Common;

/// <summary>Outward view of a <see cref="Database"/>: everything a screen shows about one.</summary>
/// <remarks>
/// One DTO for both the list and the single read, because a database has no detail beyond this —
/// there is no larger view to grow into, and a second near-identical type would only be somewhere
/// for the two to drift apart.
///
/// It carries no password, and there is nothing for it to carry one FROM: no column holds one
/// (<see cref="Database"/>). The value is shown exactly once, by
/// <see cref="CreatedDatabaseDto"/> and <see cref="DatabasePasswordDto"/>, and never again.
/// </remarks>
/// <param name="Id">The database's identity, and the only identifier a request may name.</param>
/// <param name="AccountId">The account that owns this database.</param>
/// <param name="Name">The name the customer asked for, without the account prefix.</param>
/// <param name="FullName">The fully-qualified name MySQL holds — what an application's connection string needs.</param>
/// <param name="DbUserName">The fully-qualified dedicated user name — likewise.</param>
/// <param name="CreatedAt">The instant the database was created.</param>
public sealed record DatabaseDto(
    Guid Id,
    Guid AccountId,
    string Name,
    string FullName,
    string DbUserName,
    DateTimeOffset CreatedAt);
