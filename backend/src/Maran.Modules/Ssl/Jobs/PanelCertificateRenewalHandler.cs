using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;
using Microsoft.Extensions.Logging;

namespace Maran.Modules.Ssl.Jobs;

/// <summary>
/// Re-orders the panel's own certificate when it expires within
/// <see cref="CertificateRenewalHandler.RenewalWindow"/>, once a day (issue #132).
/// </summary>
/// <remarks>
/// Named <c>…Handler</c> because that is how Wolverine discovers it (see
/// <see cref="CertificateRenewalHandler"/> for what a wrong name once cost). Unattended, so it is
/// journalled as the panel itself and logged, and recorded as a task only administrators read.
///
/// Only the row for the host name configured TODAY is renewed: a certificate for a name the panel no
/// longer answers to is not worth an order. A panel with no trusted certificate is left alone — it is
/// on the installer's self-signed pair by the operator's choice or because issuance never succeeded,
/// and an unattended job ordering one would be a decision nobody made. A renewal that fails keeps the
/// certificate on disk, which is still valid for up to thirty days, and is tried again the next day.
/// </remarks>
public sealed class PanelCertificateRenewalHandler
{
    /// <summary>Pre-compiled log delegate for a renewal that did not produce new material.</summary>
    private static readonly Action<ILogger, string, string, Exception?> LogRenewalFailure =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(1, nameof(PanelCertificateRenewalHandler)),
            "Renewal of the panel certificate for {HostName} failed with {ErrorCode}; retried tomorrow");

    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The Host's window onto the panel's configured host name.</summary>
    private readonly IPanelHostName _panelHostName;

    /// <summary>The one path that orders, installs and records the panel's certificate.</summary>
    private readonly PanelCertificateIssuer _issuer;

    /// <summary>This module's audit journal.</summary>
    private readonly CertificateAuditJournal _journal;

    /// <summary>The panel-wide task journal.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The injected time source, which defines the renewal window.</summary>
    private readonly IClock _clock;

    /// <summary>Where a renewal failure becomes visible to an operator.</summary>
    private readonly ILogger<PanelCertificateRenewalHandler> _logger;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="panelHostName">The Host's window onto the configured host name.</param>
    /// <param name="issuer">The shared issuance path.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="clock">The injected time source.</param>
    /// <param name="logger">Sink for renewal failures.</param>
    public PanelCertificateRenewalHandler(
        SslDbContext dbContext,
        IPanelHostName panelHostName,
        PanelCertificateIssuer issuer,
        CertificateAuditJournal journal,
        ITaskRecorder tasks,
        IClock clock,
        ILogger<PanelCertificateRenewalHandler> logger)
    {
        _dbContext = dbContext;
        _panelHostName = panelHostName;
        _issuer = issuer;
        _journal = journal;
        _tasks = tasks;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Renews the panel's certificate when it is due.</summary>
    /// <param name="message">The scheduled trigger, which carries nothing.</param>
    /// <param name="cancellationToken">Cancels the renewal.</param>
    /// <returns>True when a new certificate was installed.</returns>
    public async Task<bool> HandleAsync(PanelCertificateRenewalRequested message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!_panelHostName.TryGet(out var hostName))
        {
            return false;
        }

        var current = await _dbContext.PanelCertificates
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostName == hostName, cancellationToken);
        if (current is null || !current.IsDueForRenewal(_clock.UtcNow, CertificateRenewalHandler.RenewalWindow))
        {
            return false;
        }

        var taskId = await _tasks.BeginAsync(
            TaskKinds.CertificateRenewal,
            hostName,
            accountId: null,
            TaskAudience.AdministratorOnly,
            correlationId: null,
            cancellationToken);

        var renewed = await _issuer.IssueAsync(hostName, cancellationToken);

        await _journal.RecordScheduledAsync(
            AuditActions.CertificateRenewed, hostName, renewed.IsSuccess, cancellationToken);

        if (!renewed.IsSuccess)
        {
            LogRenewalFailure(_logger, hostName, renewed.Error!.Code, null);
            await _tasks.FailAsync(taskId, renewed.Error.Code, cancellationToken);
            return false;
        }

        await _tasks.CompleteAsync(taskId, cancellationToken);
        return true;
    }
}
