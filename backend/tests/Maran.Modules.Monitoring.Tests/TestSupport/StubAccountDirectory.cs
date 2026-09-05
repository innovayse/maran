using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Monitoring.Tests.TestSupport;

/// <summary>
/// An <see cref="IAccountDirectory"/> double holding a fixed set of snapshots, for the one thing this
/// module asks a directory: every account on the host.
/// </summary>
/// <remarks>
/// Unlike the doubles in the tenant-scoped modules, this one models an UNSCOPED read, because that is
/// what the contract says <see cref="IAccountDirectory.ListAsync"/> is: it hands back every account
/// there is, and the authorization sits at the caller's HTTP boundary rather than in the directory.
/// A double that filtered here would make an unscoped method look scoped and hide the exposure the
/// interface documents; the gating that actually protects it is asserted over real HTTP instead
/// (<c>MonitoringAuthorizationTests</c>).
/// </remarks>
public sealed class StubAccountDirectory : IAccountDirectory
{
    /// <summary>Every account this directory knows about.</summary>
    private readonly IReadOnlyList<AccountSnapshot> _snapshots;

    /// <summary>How many times the full listing was asked for.</summary>
    public int Listings { get; private set; }

    /// <summary>Creates a directory knowing the given snapshots.</summary>
    /// <param name="snapshots">The accounts on this imaginary host.</param>
    public StubAccountDirectory(params AccountSnapshot[] snapshots)
    {
        _snapshots = snapshots;
    }

    /// <inheritdoc />
    public Task<AccountSnapshot?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // Refused rather than answered. Nothing in this module resolves a single account, and the
        // scoping the real implementation applies here is the part a double cannot honestly model —
        // so a plausible-looking answer would only make a future caller's missing tenant check pass.
        throw new NotSupportedException("Monitoring resolves no single account; use ListAsync.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AccountSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        Listings++;
        return Task.FromResult(_snapshots);
    }
}
