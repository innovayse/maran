using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Mappers;
using Maran.Modules.Ssl.Resources;
using Maran.Modules.Ssl.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Commands.IssuePanelCertificate;

/// <summary>
/// Handles <see cref="IssuePanelCertificateCommand"/>: reads the panel's configured host name, then
/// orders, installs and records its certificate through <see cref="PanelCertificateIssuer"/>.
/// </summary>
/// <remarks>
/// An administrator's operation (the endpoint is <c>AdminOnly</c>), recorded as a task only
/// administrators read and journalled under the same actions a site certificate is, with the host
/// name as the subject. A panel with no usable host name configured is refused before anything is
/// ordered: a certificate authority cannot validate <c>localhost</c>, and the reason to fix is the
/// configuration, which the code says.
/// </remarks>
public sealed class IssuePanelCertificateCommandHandler
{
    /// <summary>The journal subject of a refusal made before any host name was known.</summary>
    private const string UnnamedPanelSubject = "panel";

    /// <summary>The Host's window onto the panel's configured host name.</summary>
    private readonly IPanelHostName _panelHostName;

    /// <summary>The one path that orders, installs and records the panel's certificate.</summary>
    private readonly PanelCertificateIssuer _issuer;

    /// <summary>This module's audit journal.</summary>
    private readonly CertificateAuditJournal _journal;

    /// <summary>The panel-wide task journal, so an operator can watch the order.</summary>
    private readonly ITaskRecorder _tasks;

    /// <summary>The current request's correlation id, recorded on the task.</summary>
    private readonly ICorrelationIdAccessor _correlationIds;

    /// <summary>Creates the handler.</summary>
    /// <param name="panelHostName">The Host's window onto the configured host name.</param>
    /// <param name="issuer">The shared issuance path.</param>
    /// <param name="journal">This module's audit journal.</param>
    /// <param name="tasks">The panel-wide task journal.</param>
    /// <param name="correlationIds">The current request's correlation id.</param>
    public IssuePanelCertificateCommandHandler(
        IPanelHostName panelHostName,
        PanelCertificateIssuer issuer,
        CertificateAuditJournal journal,
        ITaskRecorder tasks,
        ICorrelationIdAccessor correlationIds)
    {
        _panelHostName = panelHostName;
        _issuer = issuer;
        _journal = journal;
        _tasks = tasks;
        _correlationIds = correlationIds;
    }

    /// <summary>Issues and installs the panel's own certificate.</summary>
    /// <param name="command">The caller's address and user agent, stamped by the action.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The recorded certificate, <c>PanelHostNameNotConfigured</c>, or the authority's or agent's own
    /// typed failure — <c>AgentBelowFreeSpaceFloor</c> among them when the challenge write is refused
    /// at the free-space floor.
    /// </returns>
    public async Task<Result<PanelCertificateDto>> HandleAsync(
        IssuePanelCertificateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_panelHostName.TryGet(out var hostName))
        {
            await _journal.RecordFailureAsync(
                AuditActions.CertificateIssued, UnnamedPanelSubject, command.IpAddress, command.UserAgent, cancellationToken);
            return Result<PanelCertificateDto>.Fail(
                Error.Of(nameof(ErrorMessages.PanelHostNameNotConfigured), ErrorType.Conflict));
        }

        // No account: the panel's own certificate concerns the operator, so only administrators
        // read the task.
        var taskId = await _tasks.BeginAsync(
            TaskKinds.CertificateIssue,
            hostName,
            accountId: null,
            TaskAudience.AdministratorOnly,
            _correlationIds.CorrelationId,
            cancellationToken);

        await _tasks.ReportAsync(taskId, 20, "ordering the certificate from the authority", cancellationToken);

        var issued = await _issuer.IssueAsync(hostName, cancellationToken);
        if (!issued.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                AuditActions.CertificateIssued, hostName, command.IpAddress, command.UserAgent, cancellationToken);
            await _tasks.FailAsync(taskId, issued.Error!.Code, cancellationToken);
            return Result<PanelCertificateDto>.Fail(issued.Error);
        }

        await _journal.RecordSuccessAsync(
            AuditActions.CertificateIssued, hostName, command.IpAddress, command.UserAgent, cancellationToken);
        await _tasks.CompleteAsync(taskId, cancellationToken);

        return Result<PanelCertificateDto>.Ok(PanelCertificateMapper.From(issued.Value));
    }
}
