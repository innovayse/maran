namespace Maran.Modules.Ssl.Models;

/// <summary>The authority refused the request for any reason other than a stale nonce; retrying the same request cannot change the answer.</summary>
public sealed record RefusedAcmeAttempt : AcmeAttempt
{
    /// <summary>Creates the attempt.</summary>
    /// <param name="body">The response body as text.</param>
    public RefusedAcmeAttempt(string body)
        : base(body)
    {
    }
}
