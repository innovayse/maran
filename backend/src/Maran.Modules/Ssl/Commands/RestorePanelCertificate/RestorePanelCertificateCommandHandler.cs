using Maran.Agent.Client.Interfaces;
using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Resources;
using Maran.Modules.Ssl.Services;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Commands.RestorePanelCertificate;

/// <summary>
/// Handles <see cref="RestorePanelCertificateCommand"/>: has the agent replace the panel's pair with a
/// self-signed one for the configured host name, and only then forgets the recorded trusted
/// certificate.
/// </summary>
/// <remarks>
/// <para>
/// The order is the safety. The agent is asked FIRST and the row is deleted only after it answered
/// success, so a refused or failed restore leaves both the pair the panel was serving and the row
/// describing it exactly as they were. Deleting the row at all is what stops the renewal sweep
/// re-ordering a trusted certificate the operator just chose to leave; the sweep renews only names
/// that have a row. A restore that succeeded and then failed to delete the row is the one partial
/// outcome: the panel serves the self-signed pair while the row still says trusted, and the next
/// sweep would replace the pair again — the operator repeats the request, which is idempotent.
/// </para>
/// <para>
/// An administrator's operation (the endpoint is <c>AdminOnly</c>), journalled under
/// <see cref="CertificateAuditActions.CertificateSelfSignedRestored"/> with the host name as the
/// subject; a refusal is journalled too. The host name is read from the panel's configuration through
/// the same window the issuing handler uses, never from the request.
/// </para>
/// </remarks>
public sealed class RestorePanelCertificateCommandHandler
{
    /// <summary>The journal subject of a refusal made before any host name was known.</summary>
    private const string UnnamedPanelSubject = "panel";

    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The Host's window onto the panel's configured host name.</summary>
    private readonly IPanelHostName _panelHostName;

    /// <summary>The agent's TLS operations, which own the panel's pair.</summary>
    private readonly IAgentSslClient _agent;

    /// <summary>This module's audit journal.</summary>
    private readonly CertificateAuditJournal _journal;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="panelHostName">The Host's window onto the configured host name.</param>
    /// <param name="agent">The agent's TLS operations.</param>
    /// <param name="journal">This module's audit journal.</param>
    public RestorePanelCertificateCommandHandler(
        SslDbContext dbContext,
        IPanelHostName panelHostName,
        IAgentSslClient agent,
        CertificateAuditJournal journal)
    {
        _dbContext = dbContext;
        _panelHostName = panelHostName;
        _agent = agent;
        _journal = journal;
    }

    /// <summary>Restores the panel's self-signed certificate.</summary>
    /// <param name="command">The caller's address and user agent, stamped by the action.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The host name and the expiry of the self-signed certificate now installed,
    /// <c>PanelHostNameNotConfigured</c> when the panel has no host name, or the agent's own typed
    /// failure — in which case the previous pair and its row are untouched.
    /// </returns>
    public async Task<Result<PanelSelfSignedDto>> HandleAsync(
        RestorePanelCertificateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!_panelHostName.TryGet(out var hostName))
        {
            await _journal.RecordFailureAsync(
                CertificateAuditActions.CertificateSelfSignedRestored,
                UnnamedPanelSubject,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);
            return Result<PanelSelfSignedDto>.Fail(
                Error.Of(nameof(ErrorMessages.PanelHostNameNotConfigured), ErrorType.Conflict));
        }

        var restored = await _agent.RestorePanelSelfSignedAsync(hostName, cancellationToken);
        if (!restored.IsSuccess)
        {
            await _journal.RecordFailureAsync(
                CertificateAuditActions.CertificateSelfSignedRestored,
                hostName,
                command.IpAddress,
                command.UserAgent,
                cancellationToken);
            return Result<PanelSelfSignedDto>.Fail(restored.Error!);
        }

        var recorded = await _dbContext.PanelCertificates
            .SingleOrDefaultAsync(row => row.HostName == hostName, cancellationToken);
        if (recorded is not null)
        {
            _dbContext.PanelCertificates.Remove(recorded);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        await _journal.RecordSuccessAsync(
            CertificateAuditActions.CertificateSelfSignedRestored,
            hostName,
            command.IpAddress,
            command.UserAgent,
            cancellationToken);

        return Result<PanelSelfSignedDto>.Ok(new PanelSelfSignedDto(hostName, restored.Value.ExpiresAt));
    }
}
