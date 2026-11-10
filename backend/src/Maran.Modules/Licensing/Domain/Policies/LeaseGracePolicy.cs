using Maran.Modules.Licensing.Domain.Entities;
using Maran.Modules.Licensing.Domain.Enums;
using Maran.Modules.Licensing.Domain.ValueObjects;
using Maran.Sdk.Contracts;

namespace Maran.Modules.Licensing.Domain.Policies;

/// <summary>
/// The pure rule that turns a lease reading, the installed licence, this host's machine id and the
/// clock into a <see cref="LeaseStanding"/>. It reads the injected instant only, never the ambient
/// clock, so a clock moved backwards cannot extend a lease and a test can place "now" on either side of
/// every boundary (docs/superpowers/notes/2026-11-07-licensing-lease-threat-note.md §4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Order of the checks matters for what an operator is told.</b> Whose lease it is comes first
/// (licence, then machine), then whether its dates can be believed (a future-dated lease is refused
/// before anything is said about its end), then whether it has ended, and only then whether the daily
/// renewal is late. A lease for another licence is never "expired", whatever its dates say.
/// </para>
/// <para>
/// <b>The grace is the lease's life.</b> The licence cloud is asked daily, so a lease older than
/// <see cref="RenewalInterval"/> means a renewal did not arrive; the cover nevertheless holds until
/// <see cref="LicenceLease.ValidUntil"/>, which is how an unreachable cloud costs nothing for 14 days
/// and then degrades only the paid modules.
/// </para>
/// </remarks>
public static class LeaseGracePolicy
{
    /// <summary>How old a lease may be before the daily renewal counts as late.</summary>
    public static readonly TimeSpan RenewalInterval = TimeSpan.FromDays(1);

    /// <summary>How far in the future a lease's issue time may be before the lease is refused as forged.</summary>
    public static readonly TimeSpan SkewBound = TimeSpan.FromMinutes(5);

    /// <summary>Judges a reading of the stored lease against the installed licence.</summary>
    /// <param name="reading">What the lease source found.</param>
    /// <param name="licence">The installed, valid licence the lease must renew.</param>
    /// <param name="hostMachineId">This host's machine id, or null when it cannot be read.</param>
    /// <param name="now">The instant to judge at (the injected clock's).</param>
    /// <returns>The lease's standing.</returns>
    public static LeaseStanding StandingOf(
        LeaseReading reading,
        Licence licence,
        string? hostMachineId,
        DateTimeOffset now)
    {
        if (licence.IsOffline)
        {
            return LeaseStanding.NotRequired;
        }

        return reading switch
        {
            LeaseReading.Absent => LeaseStanding.Missing,
            LeaseReading.Refused => new LeaseStanding(LeaseStandingKind.Refused, null),
            LeaseReading.Present present => Assess(present.Lease, licence, hostMachineId, now),
            _ => throw new ArgumentOutOfRangeException(nameof(reading), reading, "Unknown LeaseReading case."),
        };
    }

    /// <summary>Judges a verified lease.</summary>
    /// <param name="lease">The lease.</param>
    /// <param name="licence">The installed licence.</param>
    /// <param name="hostMachineId">This host's machine id, or null.</param>
    /// <param name="now">The instant to judge at.</param>
    /// <returns>The standing.</returns>
    private static LeaseStanding Assess(
        LicenceLease lease,
        Licence licence,
        string? hostMachineId,
        DateTimeOffset now)
    {
        if (!string.Equals(lease.LicenceId, licence.Id.Value, StringComparison.Ordinal))
        {
            return new LeaseStanding(LeaseStandingKind.WrongLicence, null);
        }

        // Both the licence and the lease may name a server; whichever do must name THIS one. A lease
        // that names a machine for a licence bound to a different one fails on the licence's side.
        if (NamesAnotherMachine(licence.BoundMachineId, hostMachineId)
            || NamesAnotherMachine(lease.MachineId, hostMachineId))
        {
            return new LeaseStanding(LeaseStandingKind.WrongMachine, null);
        }

        if (lease.IssuedAt > now + SkewBound)
        {
            return new LeaseStanding(LeaseStandingKind.FutureDated, null);
        }

        if (now >= lease.ValidUntil)
        {
            return new LeaseStanding(LeaseStandingKind.Expired, lease.ValidUntil);
        }

        var kind = now - lease.IssuedAt >= RenewalInterval
            ? LeaseStandingKind.RenewalOverdue
            : LeaseStandingKind.Fresh;

        return new LeaseStanding(kind, lease.ValidUntil);
    }

    /// <summary>Whether a named machine id is not this host's. A name that is absent claims nothing.</summary>
    /// <param name="namedMachineId">The machine id the licence or lease names, or null.</param>
    /// <param name="hostMachineId">This host's machine id, or null when unreadable.</param>
    /// <returns>True when a machine is named and it is not this host (an unreadable host matches nothing).</returns>
    private static bool NamesAnotherMachine(string? namedMachineId, string? hostMachineId)
    {
        return namedMachineId is not null
            && !string.Equals(namedMachineId, hostMachineId, StringComparison.OrdinalIgnoreCase);
    }
}
