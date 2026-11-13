using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.Interfaces;
using Maran.Modules.Licensing.Domain.Policies;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Modules.Licensing.Interfaces;
using Maran.Modules.Licensing.Models;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Licensing.Services;

/// <summary>
/// Reads the licence and its lease from disk and judges both at the injected clock's instant: the
/// uncached half of the licence window. The status screen reads through it directly, so an
/// administrator always sees the truth; the request-time gate reads through
/// <see cref="LicenceCoverageCache"/>.
/// </summary>
/// <remarks>
/// It calls <see cref="LicenceVerifier"/> and the lease source, which never throw, and reads the
/// machine id through <see cref="IServerIdentitySource"/>, which answers null on any failure. It is
/// NOT what <see cref="LicenceStartupCheck"/> uses: that probe keeps calling the verifier directly, so
/// the "core never dies" path shares no code with the window the gate depends on.
/// </remarks>
public sealed class LicenceCoverageSource
{
    /// <summary>Where the raw licence text comes from.</summary>
    private readonly ILicenceRawTextSource _rawTextSource;

    /// <summary>Verifies the licence.</summary>
    private readonly LicenceVerifier _verifier;

    /// <summary>Where the stored lease comes from.</summary>
    private readonly ILicenceLeaseSource _leaseSource;

    /// <summary>This host's identity, for the lease's machine binding.</summary>
    private readonly IServerIdentitySource _serverIdentitySource;

    /// <summary>The instant to judge at.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the source.</summary>
    /// <param name="rawTextSource">Where the raw licence text comes from.</param>
    /// <param name="verifier">Verifies the licence.</param>
    /// <param name="leaseSource">Where the stored lease comes from.</param>
    /// <param name="serverIdentitySource">This host's identity.</param>
    /// <param name="clock">The instant to judge at.</param>
    public LicenceCoverageSource(
        ILicenceRawTextSource rawTextSource,
        LicenceVerifier verifier,
        ILicenceLeaseSource leaseSource,
        IServerIdentitySource serverIdentitySource,
        IClock clock)
    {
        _rawTextSource = rawTextSource;
        _verifier = verifier;
        _leaseSource = leaseSource;
        _serverIdentitySource = serverIdentitySource;
        _clock = clock;
    }

    /// <summary>Reads the licence and the lease and judges them now.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The licence status and the standing of its lease.</returns>
    public async Task<LicenceCoverage> ReadAsync(CancellationToken cancellationToken)
    {
        var status = await _verifier.VerifyAsync(_rawTextSource.ReadRawLicenceText(), cancellationToken);

        // Only a licence that verified has a lease to judge; everything else is covered by the
        // licence's own state, and a lease is not even read for it.
        if (status is not LicenceStatus.Valid valid)
        {
            return new LicenceCoverage(status, LeaseStanding.Missing);
        }

        var reading = await _leaseSource.ReadAsync(cancellationToken);
        var hostMachineId = await _serverIdentitySource.TryReadMachineIdAsync(cancellationToken);

        return new LicenceCoverage(
            status,
            LeaseGracePolicy.StandingOf(reading, valid.Licence, hostMachineId, _clock.UtcNow));
    }

    /// <summary>
    /// Judges the stored lease for one given licence, which need not be the one installed now. An install answers
    /// for the licence it installed, so a second install that replaced it in the meantime does not lend its own
    /// lease to the first answer: a lease that names another licence stands as such.
    /// </summary>
    /// <param name="licence">The licence the lease is judged for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The standing of the stored lease for <paramref name="licence"/>.</returns>
    public async Task<LeaseStanding> LeaseStandingForAsync(Licence licence, CancellationToken cancellationToken)
    {
        var reading = await _leaseSource.ReadAsync(cancellationToken);
        var hostMachineId = await _serverIdentitySource.TryReadMachineIdAsync(cancellationToken);

        return LeaseGracePolicy.StandingOf(reading, licence, hostMachineId, _clock.UtcNow);
    }
}
