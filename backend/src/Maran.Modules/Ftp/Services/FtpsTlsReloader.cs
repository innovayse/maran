using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ftp.Persistence;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ftp.Services;

/// <summary>
/// Restarts this server's FTPS daemon when certificate material has been replaced for the hostname
/// it serves — and does nothing at all for material replaced for any other domain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an event and not a poll.</b> vsftpd reads its certificate once, at start. A daily
/// reconcile would leave the daemon serving superseded material for up to a day after a renewal,
/// and — worse — a reconcile that restarts on a schedule restarts a working daemon for no reason.
/// The Ssl module knows the exact moment material lands, for a manual install and for an automatic
/// renewal alike, so it says so and this runs on being told.
/// </para>
/// <para>
/// <b>The domain comparison is the whole protection.</b> A busy server renews certificates for every
/// site it hosts; without the comparison this becomes "restart vsftpd on every renewal on the box",
/// which is a restart a day and every transfer in flight aborted with it. So the reload happens only
/// when the replaced domain is the hostname the panel persisted, and only when FTPS is enabled.
/// </para>
/// <para>
/// <b>What a FAILED reload records, and why it must record something.</b> The daemon is then still
/// running, still answering, and still presenting the OLD certificate — a state in which every other
/// status field reads healthy. Nothing about the failure is visible to the operator who asked for
/// the certificate, because they did not ask for this. So a refused reload writes an audit entry
/// with <c>succeeded: false</c> under <c>FtpsTlsReloaded</c>, naming the hostname, and logs an
/// operator-facing English line carrying the agent's error code; an event handled silently that did
/// nothing is worse than one that refused. It does NOT throw: the certificate WAS installed, the Ssl
/// module's own work succeeded, and failing its notification would report a successful installation
/// as a failure.
/// </para>
/// <para>
/// A refusal that is only "not now" arrives here indistinguishable from a fault. The agent's
/// per-account lock refuses rather than waits and the contract carries no busy code, so a
/// concurrent operation surfaces as <c>AgentSystemFailure</c> — see
/// <see cref="IAgentFtpsClient"/>, where the gap is recorded. The consequence for THIS path is that
/// the journal entry is the operator's only signal, which is the reason it exists.
/// </para>
/// </remarks>
public sealed class FtpsTlsReloader
{
    /// <summary>Pre-compiled log delegate for a reload the agent refused.</summary>
    /// <remarks>
    /// Error, not warning: the daemon is left presenting superseded certificate material while every
    /// other status field reads healthy, and nothing else in the panel will say so on its own. The
    /// agent's own English sentence is NOT here — it may name absolute paths on the host, and it has
    /// already been logged and redacted at the boundary that received it
    /// (<c>AgentErrorTranslator</c>); this line carries the stable code and the hostname, which are
    /// what an operator searches on.
    /// </remarks>
    private static readonly Action<ILogger, string, string, Exception?> LogReloadRefused =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(1, nameof(FtpsTlsReloader)),
            "FTPS TLS reload refused for {Hostname} ({Code}); the daemon is still presenting the "
            + "previous certificate material");

    /// <summary>The Ftp module's database context, holding the hostname to compare against.</summary>
    private readonly FtpDbContext _dbContext;

    /// <summary>The agent, which owns the daemon and is the only thing that may restart it.</summary>
    private readonly IAgentFtpsClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly FtpAuditJournal _journal;

    /// <summary>Operator-facing diagnostics, in English and never localized (rules/csharp.md).</summary>
    private readonly ILogger<FtpsTlsReloader> _logger;

    /// <summary>Creates the reloader.</summary>
    /// <param name="dbContext">The Ftp module's database context.</param>
    /// <param name="agent">The agent client that restarts the daemon.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="logger">Operator-facing diagnostics.</param>
    public FtpsTlsReloader(
        FtpDbContext dbContext,
        IAgentFtpsClient agent,
        FtpAuditJournal journal,
        ILogger<FtpsTlsReloader> logger)
    {
        _dbContext = dbContext;
        _agent = agent;
        _journal = journal;
        _logger = logger;
    }

    /// <summary>
    /// Restarts the daemon if — and only if — <paramref name="domain"/> is the hostname FTPS is
    /// enabled for on this server.
    /// </summary>
    /// <param name="domain">The domain whose certificate material was just installed or renewed.</param>
    /// <param name="cancellationToken">Cancels the read, the agent call and the journal write.</param>
    /// <returns>
    /// <c>true</c> when the daemon was restarted; <c>false</c> when nothing was done — because the
    /// domain is not this server's FTPS hostname, because FTPS is not enabled, or because the agent
    /// refused, which is journalled and logged.
    /// </returns>
    public async Task<bool> ReloadForAsync(string domain, CancellationToken cancellationToken)
    {
        // SingleOrDefault with no predicate, because the table holds at most one row by its primary
        // key: a second one would be a key violation, so "single" is a claim the database enforces
        // rather than an assumption this line makes.
        var settings = await _dbContext.FtpsSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);

        if (settings is null || !settings.Enabled)
        {
            return false;
        }

        // Ordinal, case-insensitive: DNS names are case-insensitive, and a certificate installed for
        // "FTP.Example.Test" is material for the same daemon as one installed for "ftp.example.test".
        // Ordinal rather than culture-aware because a host name is an identifier, not prose — a
        // culture-aware comparison is where a Turkish locale decides "I" and "i" are different letters.
        if (!string.Equals(settings.Hostname, domain, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var reloaded = await _agent.ReloadTlsAsync(cancellationToken);
        if (!reloaded.IsSuccess)
        {
            LogReloadRefused(_logger, settings.Hostname, reloaded.Error!.Code, null);

            await _journal.RecordUnattendedAsync(
                FtpAuditJournal.FtpsTlsReloaded, settings.Hostname, succeeded: false, cancellationToken);

            return false;
        }

        await _journal.RecordUnattendedAsync(
            FtpAuditJournal.FtpsTlsReloaded, settings.Hostname, succeeded: true, cancellationToken);

        return true;
    }
}
