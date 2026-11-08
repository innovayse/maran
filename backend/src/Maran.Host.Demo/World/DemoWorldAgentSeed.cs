using Maran.Agent.Simulated.Interfaces;
using Maran.Agent.Simulated.Seeds;
using Maran.Agent.Simulated.Services.PhpService;
using Maran.Agent.Simulated.State;
using Maran.Host.Demo.World.Enums;

namespace Maran.Host.Demo.World;

/// <summary>
/// Gives the simulated server the part of the world the database cannot supply: the system user of
/// every account, with its suspension and the disk it reports using, and the PHP versions the sites
/// run on. It is built from <see cref="StandardDemoWorld"/>, the definition the database seed uses, so
/// the two cannot disagree about who exists.
/// </summary>
/// <remarks>
/// The virtual hosts, certificates, databases, file-transfer logins, cron tables, backups and firewall
/// state are filled by <see cref="DemoWorldHostingSeed"/>, which runs after this seed.
/// </remarks>
public sealed class DemoWorldAgentSeed : ISimulatedAgentSeed
{
    /// <summary>Bytes in a megabyte, as the plan quota counts them.</summary>
    private const ulong BytesPerMegabyte = 1024UL * 1024UL;

    /// <summary>Reads the instant accounts are recorded as created at.</summary>
    private readonly IClock _clock;

    /// <summary>Creates the seed.</summary>
    /// <param name="clock">Reads the instant accounts are recorded as created at.</param>
    public DemoWorldAgentSeed(IClock clock)
    {
        _clock = clock;
    }

    /// <inheritdoc />
    public void ApplyTo(SimulatedAgentLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);

        var world = StandardDemoWorld.Instance;

        foreach (var version in world.PhpVersions.Where(version => { return !ledger.PhpRuntimes.Contains(version); }))
        {
            ledger.PhpRuntimes.Add(
                version,
                new SimulatedPhpRuntime(
                    version,
                    SimulatedBaselineSeed.FpmSocketDirectory,
                    false,
                    SimulatedPhpCatalog.DefaultEnabledExtensions));
        }

        foreach (var account in world.Accounts)
        {
            ledger.Accounts.Add(
                account.Name,
                new SimulatedAccount(
                    account.Name,
                    ledger.NextUid(),
                    "/home/" + account.Name,
                    (ulong)StandardDemoWorld.QuotaMbOf(account) * BytesPerMegabyte,
                    _clock.UtcNow,
                    account.State == DemoAccountState.Suspended,
                    SimulatedAccount.UnlimitedSlices,
                    (ulong)account.DiskUsedMb * BytesPerMegabyte));
        }
    }
}
