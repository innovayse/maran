namespace Maran.Sdk.Contracts;

/// <summary>
/// The answer to presenting an export grant: it authenticated (<see cref="MigrationGrantAuthenticated"/>) or it
/// did not (<see cref="MigrationGrantDenied"/>) — a closed set.
/// </summary>
/// <remarks>
/// A denial carries no reason on purpose. Unknown, malformed, expired, revoked, already used, address-refused and
/// out-of-date-export grants are one answer, so nothing here can become an oracle for which of them it was or for
/// whether a tenant's export exists; the implementation logs the reason for the operator, never the token. The
/// constructor is <c>private protected</c>, so no assembly but the Sdk can add a case.
/// </remarks>
public abstract record MigrationGrantAuthentication
{
    /// <summary>Seals the hierarchy to this assembly.</summary>
    private protected MigrationGrantAuthentication()
    {
    }
}
