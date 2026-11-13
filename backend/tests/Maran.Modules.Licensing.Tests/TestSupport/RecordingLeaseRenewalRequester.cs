using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;
using Maran.Modules.Licensing.Services;

namespace Maran.Modules.Licensing.Tests.TestSupport;

/// <summary>
/// A lease requester that answers the outcome the test scripts and remembers how often it was asked, and whether
/// the install lock was free at that moment, so a test can prove both that the install asked and that it asked
/// from outside the lock the real renewal needs.
/// </summary>
internal sealed class RecordingLeaseRenewalRequester : ILeaseRenewalRequester
{
    /// <summary>The gate the install holds, when the test wants the lock observed.</summary>
    private readonly LicenceInstallLock? _installLock;

    /// <summary>Creates the requester.</summary>
    /// <param name="outcome">What every request amounts to.</param>
    /// <param name="installLock">The install lock the handler under test uses; null when the test does not observe it.</param>
    internal RecordingLeaseRenewalRequester(
        LeaseRenewalOutcome outcome = LeaseRenewalOutcome.Renewed,
        LicenceInstallLock? installLock = null)
    {
        Outcome = outcome;
        _installLock = installLock;
    }

    /// <summary>What every request amounts to.</summary>
    internal LeaseRenewalOutcome Outcome { get; set; }

    /// <summary>How many times a lease was requested.</summary>
    internal int Requests { get; private set; }

    /// <summary>The licence id of every request, in order.</summary>
    internal List<string> LicenceIds { get; } = [];

    /// <summary>Whether the install lock was free every time a lease was requested; true when it is not observed.</summary>
    internal bool LockWasFree { get; private set; } = true;

    /// <inheritdoc />
    public async Task<LeaseRenewalResult> RequestNowAsync(string licenceId, CancellationToken cancellationToken)
    {
        Requests += 1;
        LicenceIds.Add(licenceId);

        if (_installLock is not null)
        {
            // A lock still held would never be granted: the wait is bounded so that shows as a failed
            // assertion and not as a test that never ends.
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                await _installLock.RunExclusiveAsync(
                    _ => { return Task.FromResult(0); },
                    bounded.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LockWasFree = false;
            }
        }

        return LeaseRenewalResult.Of(Outcome);
    }
}
