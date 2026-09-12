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

    /// <summary>An account's backups, their restore and their destinations: <c>IAgentBackupClient</c>.</summary>
    Backup,

    /// <summary>An account's crontab: <c>IAgentCronClient</c>.</summary>
    Cron,

    /// <summary>Database servers, databases and their users: <c>IAgentDbClient</c>.</summary>
    Db,

    /// <summary>Files under an account's home, written as that account: <c>IAgentFilesClient</c>.</summary>
    Files,

    /// <summary>The host firewall's rules and bans: <c>IAgentFirewallClient</c>.</summary>
    Firewall,

    /// <summary>
    /// This panel's own FTPS daemon — its configuration, its observed state, and the logins it
    /// authorises: <c>IAgentFtpsClient</c>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Sftp"/> because the two clients drive different daemons, and an
    /// administrator judging a module wants to know which one it will touch: an OpenSSH the operator
    /// already runs, or a vsftpd this panel installs, configures and starts. It is also separate
    /// from <see cref="Firewall"/> on purpose — the module that manages FTPS does NOT open the
    /// passive port range, it reports which range an operator would have to open — and from
    /// <see cref="Ssl"/>, because nothing behind this capability ever writes certificate material.
    /// </remarks>
    Ftps,

    /// <summary>Host metrics and service states: <c>IAgentMonitorClient</c>.</summary>
    Monitor,

    /// <summary>Installed PHP versions and php-fpm pools: <c>IAgentPhpClient</c>.</summary>
    Php,

    /// <summary>SFTP logins: <c>IAgentSftpClient</c>.</summary>
    /// <remarks>
    /// Named for the daemon whose logins it creates, changes and removes, which is what an
    /// administrator is judging when they read it. One member of that client reaches further and is
    /// stated here rather than left to be discovered: <c>SetAccountLoginsLockedAsync</c> locks and
    /// unlocks EVERY file-transfer login the account holds, FTPS ones included, because the agent
    /// serves it out of an enumeration of the password database rather than out of one protocol's
    /// area — a per-protocol lock would walk past the other daemon's login and report success. So a
    /// module declaring only this capability can suspend and resume an account's FTPS credentials,
    /// though it can neither create, repassword nor delete one; that needs <see cref="Ftps"/>.
    /// </remarks>
    Sftp,

    /// <summary>Web server virtual hosts and site logs: <c>IAgentSitesClient</c>.</summary>
    Sites,

    /// <summary>Certificate material on disk and the web server's TLS configuration: <c>IAgentSslClient</c>.</summary>
    Ssl,

    /// <summary>The agent's own identity and health: <c>IAgentSystemClient</c>.</summary>
    System,
}
