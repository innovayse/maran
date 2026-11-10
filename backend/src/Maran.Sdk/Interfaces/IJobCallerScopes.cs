namespace Maran.Sdk.Interfaces;

/// <summary>
/// Lets a durable job act as the administrator who started it, for the steps of that one job (issue #127).
/// </summary>
/// <remarks>
/// <para>
/// There is no "run as" in the panel: a message handler has no HTTP request, so <c>ICurrentUser</c> answers
/// anonymous and every tenant filter shows it nothing. This is the one narrow way out, and it is an
/// impersonation facility, so it is built to be hard to misuse. The caller is rebuilt from the user row on
/// every <see cref="BeginAsync"/> (a demoted or disabled initiator gets no scope, and the job halts), messages
/// carry the user's id and never a token, and the scope ends for every flow that copied it when it is disposed.
/// </para>
/// <para>
/// A step run outside any scope sees nothing, which a test holds to.
/// </para>
/// </remarks>
public interface IJobCallerScopes
{
    /// <summary>Starts acting as the administrator who started the job.</summary>
    /// <param name="initiatorUserId">The user the job was started by.</param>
    /// <param name="cancellationToken">Cancels the read of the user.</param>
    /// <returns>A scope to dispose when the step ends, or <c>null</c> when the initiator may no longer act.</returns>
    Task<IDisposable?> BeginAsync(Guid initiatorUserId, CancellationToken cancellationToken);
}
