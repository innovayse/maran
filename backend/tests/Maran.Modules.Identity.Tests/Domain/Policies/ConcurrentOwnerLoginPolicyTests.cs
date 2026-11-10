using Maran.Modules.Identity.Domain.Policies;
using Npgsql;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>
/// Behavioral contract of <see cref="ConcurrentOwnerLoginPolicy"/>: only the owner-membership index
/// means a repeat delivery; any other unique violation is a real refusal (issue #485).
/// </summary>
public sealed class ConcurrentOwnerLoginPolicyTests
{
    /// <summary>Builds a PostgreSQL failure the way the driver reports it.</summary>
    /// <param name="sqlState">The SQLSTATE.</param>
    /// <param name="constraint">The constraint the failure names.</param>
    /// <returns>The failure.</returns>
    private static PostgresException Failure(string sqlState, string constraint)
    {
        return new PostgresException(
            "violation", "ERROR", "ERROR", sqlState, constraintName: constraint);
    }

    /// <summary>The owner-membership index violation is the loser of a race between two deliveries.</summary>
    [Fact]
    public void A_violation_of_the_owner_membership_index_is_a_concurrent_duplicate()
    {
        Assert.True(ConcurrentOwnerLoginPolicy.IsConcurrentDuplicate(
            Failure(PostgresErrorCodes.UniqueViolation, "UX_AccountMemberships_AccountId_Owner")));
    }

    /// <summary>An e-mail or name another login holds is not a repeat and must not be swallowed.</summary>
    /// <param name="constraint">The unique index that was broken.</param>
    [Theory]
    [InlineData("IX_Users_Email")]
    [InlineData("IX_Users_Username")]
    [InlineData("UX_Users_ResellerId")]
    public void A_violation_of_any_other_unique_index_is_not_a_concurrent_duplicate(string constraint)
    {
        Assert.False(ConcurrentOwnerLoginPolicy.IsConcurrentDuplicate(
            Failure(PostgresErrorCodes.UniqueViolation, constraint)));
    }

    /// <summary>The right index under another SQLSTATE, a null and a foreign exception are not it either.</summary>
    [Fact]
    public void Only_a_unique_violation_of_that_index_counts()
    {
        Assert.False(ConcurrentOwnerLoginPolicy.IsConcurrentDuplicate(
            Failure(PostgresErrorCodes.ForeignKeyViolation, "UX_AccountMemberships_AccountId_Owner")));
        Assert.False(ConcurrentOwnerLoginPolicy.IsConcurrentDuplicate(null));
        Assert.False(ConcurrentOwnerLoginPolicy.IsConcurrentDuplicate(new InvalidOperationException()));
    }
}
