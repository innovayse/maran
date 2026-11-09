using Maran.Modules.Databases.Domain.Entities;

namespace Maran.Modules.Databases.Models;

/// <summary>
/// The rows that record one database the agent has just created: the database, and its dedicated user
/// already holding the full-access grant on it.
/// </summary>
/// <remarks>
/// A carrier between the create handler and the slot gate, and nothing the business has rules about:
/// it exists so the three rows reach the gate as one unit and are committed in one save, because a
/// database whose user row is missing is a database the users screen cannot show a password reset for.
/// </remarks>
/// <param name="Database">The database row.</param>
/// <param name="DedicatedUser">The dedicated user row, carrying its grant on <paramref name="Database"/>.</param>
public sealed record ProvisionedDatabaseRecords(Database Database, DatabaseUser DedicatedUser);
