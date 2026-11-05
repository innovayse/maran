namespace Maran.Modules.Ssl.Domain.Policies;

/// <summary>
/// When a certificate is close enough to expiry that the server's operator is told about it.
/// </summary>
/// <remarks>
/// The alert is a second line of defence behind renewal, not a restatement of it: renewal starts
/// thirty days ahead and retries with backoff, so a certificate still unrenewed fourteen days out has
/// failed several attempts in a row and a person has to look. Fourteen days leaves the operator a
/// working fortnight to repair the cause before the site stops serving.
/// </remarks>
public static class CertificateExpiryAlertPolicy
{
    /// <summary>How many days before expiry an unrenewed certificate raises the operator alert.</summary>
    public const int ExpiryAlertDays = 14;

    /// <summary>Whether a certificate expiring at <paramref name="notAfter"/> is inside the alert window.</summary>
    /// <param name="notAfter">When the installed certificate expires.</param>
    /// <param name="now">The current instant, from <see cref="IClock"/>.</param>
    /// <returns>
    /// <c>true</c> when fourteen days or fewer remain, including a certificate that has already
    /// expired: the window has no lower bound, because an expired certificate is the case the alert
    /// exists for.
    /// </returns>
    public static bool IsDue(DateTimeOffset notAfter, DateTimeOffset now)
    {
        return notAfter - now <= TimeSpan.FromDays(ExpiryAlertDays);
    }
}
