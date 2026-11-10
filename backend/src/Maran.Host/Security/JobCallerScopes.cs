using Maran.Sdk.Interfaces;

namespace Maran.Host.Security;

/// <summary>
/// Lets a durable job act as the administrator who started it (issue #127). The Sdk's
/// <see cref="IJobCallerScopes"/>.
/// </summary>
/// <remarks>
/// The initiator is asked of <see cref="IAdministratorCallerSource"/> on every <see cref="BeginAsync"/>: a
/// user who has been demoted, disabled or removed gets no scope, and the job's step answers
/// <c>MigrationInitiatorGone</c> and halts. Nothing a message carries is trusted for who the job acts as.
/// This method is not <c>async</c> on purpose: the scope must be placed on the caller's own flow, which an
/// async method cannot do (see <see cref="JobCallerAccessor.ActAsync"/>).
/// </remarks>
public sealed class JobCallerScopes : IJobCallerScopes
{
    /// <summary>Reads the administrator back from the user table.</summary>
    private readonly IAdministratorCallerSource _administrators;

    /// <summary>Holds the caller for the flow that enters the scope.</summary>
    private readonly JobCallerAccessor _accessor;

    /// <summary>Creates the opener.</summary>
    /// <param name="administrators">Reads the administrator back from the user table.</param>
    /// <param name="accessor">Holds the caller for the flow that enters the scope.</param>
    public JobCallerScopes(IAdministratorCallerSource administrators, JobCallerAccessor accessor)
    {
        _administrators = administrators;
        _accessor = accessor;
    }

    /// <inheritdoc />
    public Task<IDisposable?> BeginAsync(Guid initiatorUserId, CancellationToken cancellationToken)
    {
        return _accessor.ActAsync(() =>
        {
            return _administrators.FindActiveAsync(initiatorUserId, cancellationToken);
        });
    }
}
