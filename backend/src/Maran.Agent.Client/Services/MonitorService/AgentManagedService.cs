namespace Maran.Agent.Client.Services.MonitorService;

/// <summary>Which of the host's services a status describes.</summary>
/// <remarks>
/// A closed set, mirroring the wire's <c>ManagedService</c> so callers outside this project never
/// hold a generated protobuf type. It has no member for "a service this build cannot name": the
/// client refuses a row naming one as an invalid response instead of inventing a service to carry it. No call anywhere accepts a unit name from a caller: the agent
/// watches the units it knows about and nothing else.
///
/// The set the agent REPORTS is smaller than this one, and absence is meaningful: a service with no
/// row in a response is one this agent does not observe, and a reader must treat that as
/// <see cref="AgentServiceState.Unknown"/> rather than as "not running".
/// </remarks>
public enum AgentManagedService
{
    /// <summary>The web server.</summary>
    WebServer = 1,

    /// <summary>
    /// PHP-FPM. Never reported: a php-fpm unit's name carries a PHP version, so there is no single
    /// unit to ask about. Kept so the numbering matches the wire and is never reused.
    /// </summary>
    PhpFpm = 2,

    /// <summary>The database server.</summary>
    Database = 3,

    /// <summary>
    /// FTP. Never reported: no FTP daemon ships, so the agent watches no FTP unit. Kept so a future
    /// one reports under this number rather than a new one.
    /// </summary>
    Ftp = 4,

    /// <summary>The cron daemon.</summary>
    Cron = 5,

    /// <summary>
    /// The OpenSSH server. The one managed unit whose "inactive" is routinely not an outage, which
    /// is why the state beside it has three values rather than two.
    /// </summary>
    Ssh = 6,
}
