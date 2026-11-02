namespace Maran.Modules.Ssl.Models;

/// <summary>The authority refused the request because its nonce was stale, which is worth exactly one retry on the fresh nonce the refusal itself carried.</summary>
public sealed record StaleNonceAcmeAttempt : AcmeAttempt
{
    /// <summary>Creates the attempt.</summary>
    /// <param name="body">The response body as text.</param>
    public StaleNonceAcmeAttempt(string body)
        : base(body)
    {
    }
}
