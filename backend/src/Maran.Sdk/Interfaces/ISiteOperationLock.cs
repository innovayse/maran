namespace Maran.Sdk.Interfaces;

/// <summary>
/// Serialises the operations that change one site's host state - its vhost, its pool, its enabled flag,
/// its certificate - so that two of them never interleave on the same site, whichever module started them.
/// </summary>
/// <remarks>
/// <para>
/// The contract lives in the Sdk because two modules mutate the same site: Sites owns the vhost and
/// the site row, Ssl rewrites the vhost when a certificate is installed or renewed. A lock either of them
/// owned would be invisible to the other, so both resolve this one (rules/architecture.md "A shared
/// facility's contract lives in Maran.Sdk").
/// </para>
/// <para>
/// <b>Scope.</b> One key per site, and one per account for its php-fpm pools (<see cref="SiteLockKey"/>):
/// operations on different sites never wait for each other. A caller takes a site's lock outermost, before any
/// database transaction or other gate, and the account's pool lock, when it needs one, second and never the
/// reverse, so no two operations wait for each other.
/// </para>
/// <para>
/// <b>Reach.</b> The default implementation is in-process, which is exact because one panel process
/// manages one server. A lock that spans processes (a session-level database advisory lock) can replace
/// it behind this interface without any caller changing.
/// </para>
/// </remarks>
public interface ISiteOperationLock
{
    /// <summary>Waits up to <paramref name="wait"/> for exclusive use of one site.</summary>
    /// <param name="key">What the caller is about to change: a site's host state, or an account's pools.</param>
    /// <param name="wait">
    /// How long to wait for the holder to finish. <see cref="TimeSpan.Zero"/> asks without waiting.
    /// Negative durations are a programming error and throw.
    /// </param>
    /// <param name="ct">Cancels the wait; cancellation propagates as an exception.</param>
    /// <returns>
    /// A success carrying the held lock, released by disposing it (disposing twice is harmless); or the
    /// failure <c>SiteBusy</c> (a conflict) when the site was still in use when the wait ended. Timing out
    /// never throws.
    /// </returns>
    Task<Result<IAsyncDisposable>> AcquireAsync(SiteLockKey key, TimeSpan wait, CancellationToken ct);
}
