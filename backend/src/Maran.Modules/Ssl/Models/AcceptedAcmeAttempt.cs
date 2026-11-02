namespace Maran.Modules.Ssl.Models;

/// <summary>The authority answered with a success status.</summary>
public sealed record AcceptedAcmeAttempt : AcmeAttempt
{
    /// <summary>Creates the attempt.</summary>
    /// <param name="body">The response body as text.</param>
    public AcceptedAcmeAttempt(string body)
        : base(body)
    {
    }
}
