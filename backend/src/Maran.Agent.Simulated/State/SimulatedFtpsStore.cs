using Maran.Agent.Client.Services.FtpsService;
using Maran.Agent.Simulated.Errors;
using Maran.Agent.Simulated.Limits;
using Maran.Agent.Simulated.Validation;
using Maran.SharedKernel.Utilities.Network;

namespace Maran.Agent.Simulated.State;

/// <summary>
/// The FTPS daemon and logins of the simulated server, bounded and safe to share between requests.
/// </summary>
/// <remarks>
/// <para>
/// Certificate material is not modelled here. The SSL service owns that store on a real host, and
/// the panel's own self-signed generation means a valid host name always has something to serve, so
/// the simulation reports every valid host name as served by a self-signed certificate and never
/// answers <c>AgentNotFound</c> for one.
/// </para>
/// <para>
/// The status is built from the stored configuration, which is the simulation's equivalent of the
/// real agent's measurement: there is no second source that could disagree with it.
/// </para>
/// </remarks>
public sealed class SimulatedFtpsStore
{
    /// <summary>The directory the simulated daemon files certificate material under; shown to operators only.</summary>
    public const string CertificateDirectory = "/etc/maran/ftps/";

    /// <summary>The lowest port a passive range may name.</summary>
    private const uint LowestPort = 1;

    /// <summary>The highest port a passive range may name.</summary>
    private const uint HighestPort = 65_535;

    /// <summary>Serialises every read and write of the daemon and the logins.</summary>
    private readonly Lock _gate = new();

    /// <summary>The ceilings this store enforces.</summary>
    private readonly SimulatedAgentLimits _limits;

    /// <summary>The logins, keyed by owning account and name suffix.</summary>
    private readonly Dictionary<(string Account, string Suffix), SimulatedFtpsLogin> _logins = [];

    /// <summary>The daemon's configuration and run state.</summary>
    private SimulatedFtpsDaemon _daemon = SimulatedFtpsDaemon.Unconfigured;

    /// <summary>Creates an empty store bounded by <paramref name="limits"/>.</summary>
    /// <param name="limits">The ceilings this store enforces.</param>
    public SimulatedFtpsStore(SimulatedAgentLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        _limits = limits;
    }

    /// <summary>Configures the daemon and brings it up; applying the same values again changes nothing.</summary>
    /// <param name="hostname">The hostname the daemon serves.</param>
    /// <param name="passivePortMin">Lowest passive data port.</param>
    /// <param name="passivePortMax">Highest passive data port.</param>
    /// <param name="passiveAddress">The address advertised in the PASV reply, or empty for none.</param>
    /// <param name="maxClients">The concurrent-session ceiling.</param>
    /// <returns>
    /// The daemon as configured, or <c>AgentInvalidInput</c> for a host name, range or ceiling it refuses.
    /// </returns>
    public Result<FtpsStatusDto> Enable(
        string hostname,
        uint passivePortMin,
        uint passivePortMax,
        string passiveAddress,
        uint maxClients)
    {
        if (!HostNameRule.IsHostName(hostname)
            || passivePortMin < LowestPort
            || passivePortMax > HighestPort
            || passivePortMin > passivePortMax
            || maxClients == 0)
        {
            return Result<FtpsStatusDto>.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            _daemon = new SimulatedFtpsDaemon(
                true,
                hostname,
                passivePortMin,
                passivePortMax,
                passiveAddress,
                maxClients);

            return Result<FtpsStatusDto>.Ok(StatusFor(_daemon.Hostname));
        }
    }

    /// <summary>Stops the daemon and keeps its configuration and every login.</summary>
    /// <returns>The daemon as it is afterwards.</returns>
    public FtpsStatusDto Disable()
    {
        lock (_gate)
        {
            _daemon = _daemon with { Running = false };

            return StatusFor(_daemon.Hostname);
        }
    }

    /// <summary>Reports the daemon, and what certificate material the asked-about host name has.</summary>
    /// <param name="hostname">The host name to describe material for; empty reports the daemon facts only.</param>
    /// <returns>The nine facts.</returns>
    public FtpsStatusDto Status(string hostname)
    {
        lock (_gate)
        {
            return StatusFor(hostname);
        }
    }

    /// <summary>Restarts a running daemon so it picks up replaced material; a stopped one is left alone.</summary>
    /// <returns>The daemon as it is afterwards, described for the host name it is configured with.</returns>
    public FtpsStatusDto ReloadTls()
    {
        lock (_gate)
        {
            return StatusFor(_daemon.Hostname);
        }
    }

    /// <summary>Creates a login with no sessions open; seeds pass the sessions it should already have.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="suffix">Login name suffix.</param>
    /// <param name="openSessions">Sessions the login has open at creation; a customer's creation passes zero.</param>
    /// <returns>
    /// The full login name, <c>AgentAlreadyExists</c> for a login that is already there, or
    /// <c>AgentLimitReached</c> when the server holds as many as it will.
    /// </returns>
    public Result<string> CreateLogin(string account, string suffix, int openSessions)
    {
        if (!SimulatedInput.IsBoundedName(account, _limits.MaxNameLength)
            || !SimulatedInput.IsBoundedName(suffix, _limits.MaxNameLength))
        {
            return Result<string>.Fail(SimulatedAgentErrors.InvalidInput());
        }

        lock (_gate)
        {
            if (_logins.ContainsKey((account, suffix)))
            {
                return Result<string>.Fail(SimulatedAgentErrors.AlreadyExists());
            }

            if (_logins.Count >= _limits.MaxFtpsLogins)
            {
                return Result<string>.Fail(SimulatedAgentErrors.LimitReached());
            }

            var login = new SimulatedFtpsLogin(account, suffix, openSessions);
            _logins[(account, suffix)] = login;

            return Result<string>.Ok(login.FullName);
        }
    }

    /// <summary>Checks that a login exists, which is all a password change leaves to remember.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="suffix">Login name suffix.</param>
    /// <returns>Success, or <c>AgentNotFound</c>.</returns>
    public Result RequireLogin(string account, string suffix)
    {
        lock (_gate)
        {
            return _logins.ContainsKey((account, suffix)) ? Result.Ok() : Result.Fail(SimulatedAgentErrors.NotFound());
        }
    }

    /// <summary>Removes a login and nothing else.</summary>
    /// <param name="account">System username of the owning account.</param>
    /// <param name="suffix">Login name suffix.</param>
    /// <returns>Success, or <c>AgentNotFound</c>.</returns>
    public Result DeleteLogin(string account, string suffix)
    {
        lock (_gate)
        {
            return _logins.Remove((account, suffix)) ? Result.Ok() : Result.Fail(SimulatedAgentErrors.NotFound());
        }
    }

    /// <summary>Ends every session every login of one account has open.</summary>
    /// <param name="account">System username of the account.</param>
    /// <returns>How many sessions were ended; zero for an account with no login or none open.</returns>
    public uint EndAllSessions(string account)
    {
        lock (_gate)
        {
            uint ended = 0;
            foreach (var login in _logins.Values.Where(login => { return login.Account == account; }).ToList())
            {
                ended += (uint)login.OpenSessions;
                _logins[(login.Account, login.Suffix)] = login with { OpenSessions = 0 };
            }

            return ended;
        }
    }

    /// <summary>Builds the status from the stored daemon; the caller holds <see cref="_gate"/>.</summary>
    /// <param name="hostname">The host name whose material the answer describes; empty describes none.</param>
    /// <returns>The nine facts.</returns>
    private FtpsStatusDto StatusFor(string hostname)
    {
        var hasMaterial = !string.IsNullOrEmpty(hostname);

        return new FtpsStatusDto(
            _daemon.Running,
            _daemon.Running,
            hasMaterial ? FtpsCertificate.SelfSigned : FtpsCertificate.Absent,
            hasMaterial ? $"{CertificateDirectory}{hostname}/fullchain.pem" : string.Empty,
            _daemon.PassivePortMin,
            _daemon.PassivePortMax,
            false,
            _daemon.Running ? FtpsTlsEnforcement.Enforced : FtpsTlsEnforcement.NotObserved);
    }
}
