namespace Maran.Sdk.Contracts;

/// <summary>
/// The answer to presenting an API key: it authenticated (<see cref="ApiKeyAuthenticated"/>) or it
/// did not (<see cref="ApiKeyDenied"/>) — a closed set.
/// </summary>
/// <remarks>
/// A denial carries no reason on purpose. Malformed, unknown, wrong, revoked, expired and
/// address-refused keys are one answer, so nothing here can become an oracle for which of them it was;
/// the implementation logs the reason for the operator. The constructor is <c>private protected</c>, so
/// no assembly but the Sdk can add a case.
/// </remarks>
public abstract record ApiKeyAuthentication
{
    /// <summary>Seals the hierarchy to this assembly.</summary>
    private protected ApiKeyAuthentication()
    {
    }
}
