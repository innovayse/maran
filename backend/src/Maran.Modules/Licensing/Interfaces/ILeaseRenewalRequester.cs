using Maran.Modules.Licensing.Models;

namespace Maran.Modules.Licensing.Interfaces;

/// <summary>
/// Asks for a licence lease now instead of at the next scheduled pass, through the very pass the schedule runs.
/// </summary>
/// <remarks>
/// Implemented by <see cref="Services.LeaseRenewalWorker"/>, so the request shares the schedule's serialisation,
/// its journal entries, its run of failures and its alert, and the schedule's next wait follows the outcome
/// exactly as if the timer had run the pass. The caller must NOT hold <see cref="Services.LicenceInstallLock"/>:
/// storing a lease takes it. The request is bounded by <c>InstallRequestDeadlineSeconds</c>, so a caller that is a
/// web request answers before a reverse proxy gives up on it.
/// </remarks>
public interface ILeaseRenewalRequester
{
    /// <summary>Runs one renewal pass now, for one licence, within the install deadline.</summary>
    /// <param name="licenceId">The licence the caller means; a pass that finds another one installed asks nothing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// What the pass did; <see cref="Domain.Enums.LeaseRenewalOutcome.RenewalInProgress"/> when another pass held the
    /// way for the whole deadline; <see cref="Domain.Enums.LeaseRenewalOutcome.Unreachable"/> when the service did not
    /// answer within it. Never throws for anything the file system, the network or the service does.
    /// </returns>
    Task<LeaseRenewalResult> RequestNowAsync(string licenceId, CancellationToken cancellationToken);
}
