namespace Maran.Modules.Sites.Services;

/// <summary>
/// Thrown by a queued job that could not take a site's operation lock in time, so the message is
/// retried instead of being recorded as handled.
/// </summary>
/// <remarks>
/// A job has no caller to hand a <c>SiteBusy</c> result to; an exception is the only way it can tell
/// the message pipeline that the work is not done. The jobs that throw this are idempotent, so the
/// retry repeats only what the pass could not finish.
/// </remarks>
public sealed class SiteBusyException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Which sites stayed busy, and for which job.</param>
    public SiteBusyException(string message)
        : base(message)
    {
    }
}
