using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Maran.Modules.Sites.Tests.TestSupport;

/// <summary>Builds the exceptions the slot gate throws in production, so a test can make the double throw the real shapes.</summary>
public static class GateFailures
{
    /// <summary>A refused insert whose cause is a unique-key violation, as the hostname key raises it.</summary>
    /// <returns>A <see cref="DbUpdateException"/> wrapping a <see cref="PostgresException"/> with SqlState 23505.</returns>
    public static DbUpdateException UniqueViolation()
    {
        return Refused(PostgresErrorCodes.UniqueViolation);
    }

    /// <summary>A refused insert whose cause says nothing about the request: the connection dropped.</summary>
    /// <returns>A <see cref="DbUpdateException"/> wrapping a <see cref="PostgresException"/> with SqlState 08006.</returns>
    public static DbUpdateException ConnectionFailure()
    {
        return Refused(PostgresErrorCodes.ConnectionFailure);
    }

    /// <summary>Wraps a PostgreSQL error with the given SqlState the way EF Core wraps a failed save.</summary>
    /// <param name="sqlState">The five-character SqlState.</param>
    /// <returns>The exception the gate would throw.</returns>
    private static DbUpdateException Refused(string sqlState)
    {
        return new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new PostgresException("simulated failure", "ERROR", "ERROR", sqlState));
    }
}
