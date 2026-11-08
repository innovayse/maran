using Maran.Agent.Simulated.Limits;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// Everything the simulated host remembers: the one place its clients read and write, and the
/// object a seed fills. A thread-safe, bounded, in-memory stand-in for a server's state.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here touches the machine it runs on.</b> The ledger is dictionaries behind one lock;
/// a restart of the process returns the host to its seed, which is how a demonstration resets.
/// </para>
/// <para>
/// <b>Two kinds of state.</b> Accounts, vhosts, certificates, the panel's certificate and
/// challenges, PHP runtimes and the host's facts are keyed sections and cells that share this
/// ledger's one lock, so an operation spanning several of them is atomic
/// (<see cref="Atomically{TResult}"/>). Databases, logins, files, crontabs and firewall state are
/// stores of their own, each safe to share and bounded by the same <see cref="SimulatedAgentLimits"/>;
/// the ledger only holds them so a seed and every client reach the same instance.
/// </para>
/// </remarks>
public sealed class SimulatedAgentLedger
{
    /// <summary>The first user id handed out; the ids below it belong to the host's own accounts.</summary>
    private const uint FirstUid = 10000;

    /// <summary>The lock every section and cell shares, so an operation spanning several is atomic.</summary>
    private readonly object _gate = new();

    /// <summary>The next user id to hand out; read and advanced under the lock.</summary>
    private uint _nextUid = FirstUid;

    /// <summary>The caps every section and every client honours.</summary>
    public SimulatedAgentLimits Limits { get; }

    /// <summary>The hosting accounts, keyed by system user name.</summary>
    public SimulatedLedgerSection<SimulatedAccount> Accounts { get; }

    /// <summary>The sites' vhosts, keyed by primary domain.</summary>
    public SimulatedLedgerSection<SimulatedVhost> Vhosts { get; }

    /// <summary>The certificates installed for sites, keyed by domain.</summary>
    public SimulatedLedgerSection<SimulatedCertificate> Certificates { get; }

    /// <summary>The challenge files written for the panel's own host name, keyed by token.</summary>
    public SimulatedLedgerSection<SimulatedPanelChallenge> PanelChallenges { get; }

    /// <summary>The installed PHP versions, keyed by version.</summary>
    public SimulatedLedgerSection<SimulatedPhpRuntime> PhpRuntimes { get; }

    /// <summary>The certificate the panel's own vhost names; empty until one is installed.</summary>
    public SimulatedLedgerCell<SimulatedPanelCertificate> PanelCertificate { get; }

    /// <summary>What the host is: identity, capacity, services and pending maintenance.</summary>
    public SimulatedLedgerCell<SimulatedHostFacts> HostFacts { get; }

    /// <summary>The databases and their users.</summary>
    public SimulatedDatabaseStore Databases { get; }

    /// <summary>The SFTP logins.</summary>
    public SimulatedSftpLoginStore Logins { get; }

    /// <summary>The FTPS logins and the daemon's settings.</summary>
    public SimulatedFtpsStore FtpsLogins { get; }

    /// <summary>The files customers hold in their homes.</summary>
    public SimulatedFileStore Files { get; }

    /// <summary>The per-account crontabs.</summary>
    public SimulatedCronStore CronTables { get; }

    /// <summary>The firewall rules and bans.</summary>
    public SimulatedFirewallStore FirewallRules { get; }

    /// <summary>The backup artifacts.</summary>
    public SimulatedBackupStore Backups { get; }

    /// <summary>Creates an empty ledger bounded by <paramref name="limits"/>.</summary>
    /// <param name="limits">The caps every section and every client honours.</param>
    public SimulatedAgentLedger(SimulatedAgentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        Limits = limits;
        Databases = new SimulatedDatabaseStore(limits);
        Logins = new SimulatedSftpLoginStore(limits);
        FtpsLogins = new SimulatedFtpsStore(limits);
        Files = new SimulatedFileStore(limits);
        CronTables = new SimulatedCronStore(limits);
        FirewallRules = new SimulatedFirewallStore(limits);
        Backups = new SimulatedBackupStore(limits);
        Accounts = new SimulatedLedgerSection<SimulatedAccount>(_gate, limits.MaxAccounts);
        Vhosts = new SimulatedLedgerSection<SimulatedVhost>(_gate, limits.MaxSites);
        Certificates = new SimulatedLedgerSection<SimulatedCertificate>(_gate, limits.MaxSites);
        PanelChallenges = new SimulatedLedgerSection<SimulatedPanelChallenge>(_gate, limits.MaxPanelChallenges);
        PhpRuntimes = new SimulatedLedgerSection<SimulatedPhpRuntime>(_gate, limits.MaxPhpRuntimes);
        PanelCertificate = new SimulatedLedgerCell<SimulatedPanelCertificate>(_gate, null);
        HostFacts = new SimulatedLedgerCell<SimulatedHostFacts>(_gate, SimulatedHostFacts.Baseline);
    }

    /// <summary>
    /// Runs <paramref name="operation"/> while holding the ledger's lock, so a read of one section
    /// and the write that depends on it cannot be interleaved with another thread's.
    /// </summary>
    /// <typeparam name="TResult">What the operation returns.</typeparam>
    /// <param name="operation">The work to do; it must not await.</param>
    /// <returns>What the operation returned.</returns>
    public TResult Atomically<TResult>(Func<TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_gate)
        {
            return operation();
        }
    }

    /// <summary>Hands out the next user id, which is never handed out twice.</summary>
    /// <returns>A user id above the range the host reserves for itself.</returns>
    public uint NextUid()
    {
        lock (_gate)
        {
            var uid = _nextUid;
            _nextUid++;

            return uid;
        }
    }
}
