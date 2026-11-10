using Npgsql;

namespace Maran.Modules.Identity.Domain.Policies;

/// <summary>
/// Decides which unique violation, raised while an account owner's login is saved, is a harmless
/// concurrent delivery of the same event — and which is a real refusal that must not be swallowed.
/// </summary>
/// <remarks>
/// A unique violation names the index it broke. Only <see cref="OwnerMembershipConstraint"/> means
/// "another delivery of this very event already made the owner's login". <c>IX_Users_Email</c> and
/// <c>IX_Users_Username</c> are unique too, and a violation of either means some OTHER login holds the
/// value: treating that as a repeat leaves an account nobody can sign in to and answers success.
/// </remarks>
public static class ConcurrentOwnerLoginPolicy
{
    /// <summary>The partial unique index that allows one owner membership per account.</summary>
    public const string OwnerMembershipConstraint = "UX_AccountMemberships_AccountId_Owner";

    /// <summary>Whether the exception is the loser of a race between two deliveries of one event.</summary>
    /// <param name="exception">The failure a save raised, or its inner exception.</param>
    /// <returns>True only for a unique violation of <see cref="OwnerMembershipConstraint"/>.</returns>
    public static bool IsConcurrentDuplicate(Exception? exception)
    {
        return exception is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OwnerMembershipConstraint,
        };
    }
}
