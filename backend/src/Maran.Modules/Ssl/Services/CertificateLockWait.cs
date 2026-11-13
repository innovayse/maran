namespace Maran.Modules.Ssl.Services;

/// <summary>
/// How long an operation waits for a site's operation lock before it answers <c>SiteBusy</c>.
/// </summary>
/// <remarks>
/// Two values because there are two kinds of caller. A customer is looking at a spinner and is better
/// served by a quick, honest "try again" than by a minute of silence. An unattended pass has nobody
/// waiting on it, and a site that is briefly busy (a PHP version change re-renders the vhost, which
/// takes seconds) is cheaper to wait for than to fail and retry a day later.
/// </remarks>
public static class CertificateLockWait
{
    /// <summary>The wait for a request a person is watching.</summary>
    public static readonly TimeSpan Interactive = TimeSpan.FromSeconds(15);

    /// <summary>The wait for unattended work such as the daily renewal pass.</summary>
    public static readonly TimeSpan Unattended = TimeSpan.FromSeconds(90);
}
