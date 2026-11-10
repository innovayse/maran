using Maran.SharedKernel.Callers;

namespace Maran.Host.Security;

/// <summary>
/// The panel's <see cref="ICurrentUser"/>: the administrator a durable job is acting as while a job scope is
/// open, and the request's own caller at every other time.
/// </summary>
/// <remarks>
/// A served request never enters a job scope, so its caller is read from its validated token exactly as before
/// (<see cref="HttpContextCurrentUser"/>). A message handler with no scope reads the anonymous caller from the
/// same place, and every tenant filter shows it nothing.
/// </remarks>
public sealed class JobAwareCurrentUser : ICurrentUser
{
    /// <summary>Holds the caller a job is acting as.</summary>
    private readonly JobCallerAccessor _job;

    /// <summary>Reads the caller of a served request.</summary>
    private readonly HttpContextCurrentUser _request;

    /// <summary>Creates the current user.</summary>
    /// <param name="job">Holds the caller a job is acting as.</param>
    /// <param name="request">Reads the caller of a served request.</param>
    public JobAwareCurrentUser(JobCallerAccessor job, HttpContextCurrentUser request)
    {
        _job = job;
        _request = request;
    }

    /// <inheritdoc />
    public Caller Caller
    {
        get { return _job.Current ?? _request.Caller; }
    }
}
