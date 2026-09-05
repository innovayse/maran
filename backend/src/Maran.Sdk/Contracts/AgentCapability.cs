namespace Maran.Sdk.Contracts;

/// <summary>
/// One area of the agent's contract a module may be permitted to drive, as declared in its
/// <see cref="Manifest"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a module declares this at all.</b> The agent is the only root process on the server, and
/// <c>Maran.Agent.Client</c> is the single door to it — one door, shared by every module in the
/// panel process. A module that has no business touching the firewall can nevertheless resolve
/// <c>IAgentFirewallClient</c> from the container and open a port, and nothing in the panel would
/// notice. That is tolerable while every module is written in this repository and reviewed here. It
/// stops being tolerable the moment a module is bought from a marketplace: the buyer is trusting a
/// third party's code with root on their server, and "it only manages backups" would be a claim
/// with nothing behind it.
/// </para>
/// <para>
/// <b>What the declaration buys.</b> The list is part of a module's published identity, so an
/// administrator sees, before installing, exactly which parts of the server the module intends to
/// touch — and the panel refuses to compose a module that reaches for a door it did not declare.
/// One value per agent service, not per RPC: an area is what an administrator can judge, and a
/// per-method list would be a page of names nobody reads.
/// </para>
/// <para>
/// <b>The names are the contract.</b> Each value is spelled to match the client interface that
/// grants it — <see cref="Sites"/> ⇔ <c>IAgentSitesClient</c> — because the guard that enforces this
/// derives one from the other rather than holding a table that can silently fall behind. A new
/// agent service therefore cannot ship without a value here: the guard refuses a client interface
/// it cannot name, instead of waving it through as uncontrolled.
/// </para>
/// </remarks>
public enum AgentCapability
{
    /// <summary>System users and their home directories: <c>IAgentAccountsClient</c>.</summary>
    Accounts,

    /// <summary>An account's crontab: <c>IAgentCronClient</c>.</summary>
    Cron,

    /// <summary>Database servers, databases and their users: <c>IAgentDbClient</c>.</summary>
    Db,

    /// <summary>Files under an account's home, written as that account: <c>IAgentFilesClient</c>.</summary>
    Files,

    /// <summary>The host firewall's rules and bans: <c>IAgentFirewallClient</c>.</summary>
    Firewall,

    /// <summary>Host metrics and service states: <c>IAgentMonitorClient</c>.</summary>
    Monitor,

    /// <summary>Installed PHP versions and php-fpm pools: <c>IAgentPhpClient</c>.</summary>
    Php,

    /// <summary>SFTP logins: <c>IAgentSftpClient</c>.</summary>
    Sftp,

    /// <summary>Web server virtual hosts and site logs: <c>IAgentSitesClient</c>.</summary>
    Sites,

    /// <summary>Certificate material on disk and the web server's TLS configuration: <c>IAgentSslClient</c>.</summary>
    Ssl,

    /// <summary>The agent's own identity and health: <c>IAgentSystemClient</c>.</summary>
    System,
}
