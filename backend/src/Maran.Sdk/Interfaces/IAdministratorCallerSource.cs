using Maran.SharedKernel.Callers;

namespace Maran.Sdk.Interfaces;

/// <summary>
/// Reads an administrator back from the user table so a long job can act as them (issue #127). Implemented by
/// the module that owns the users.
/// </summary>
/// <remarks>
/// The answer is rebuilt from the row on every ask and never from a value carried in a message: a job that
/// outlives its request must stop being an administrator the moment the person is demoted or disabled.
/// </remarks>
public interface IAdministratorCallerSource
{
    /// <summary>Finds an administrator who may act now.</summary>
    /// <param name="userId">The user the job was started by.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The administrator's caller, or <c>null</c> when no such user exists, is not an administrator, or is disabled.</returns>
    Task<AdministratorCaller?> FindActiveAsync(Guid userId, CancellationToken cancellationToken);
}
