using Maran.Modules.Ssl.Domain.Entities;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Contracts;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Services;

/// <summary>
/// Issues, or renews, the panel's own certificate for the operator at the server's console
/// (<c>maran issue-panel-certificate</c>, issue #426) — the same operation the administrator's
/// request performs, for the moment the interface cannot be used to ask.
/// </summary>
/// <remarks>
/// <para>
/// <b>Who may call it.</b> Not a request: it runs only from the console, whose authority is shell
/// access to the server. There is no principal, and nothing here consults one.
/// </para>
/// <para>
/// <b>One path.</b> The order, the install and the row are <see cref="PanelCertificateIssuer"/>'s, the
/// path the endpoint and the daily renewal take, so a first order and a renewal are the same call and
/// a failure at any step leaves the pair the panel was serving untouched. This type adds only what a
/// console run owes: the host name is read from the operator's configuration, never typed, and the
/// outcome is journalled with the operator's claimed name (<see cref="CertificateAuditJournal.RecordConsoleAsync"/>),
/// a refusal included. No task is recorded: the operator is watching the terminal.
/// </para>
/// </remarks>
public sealed class PanelCertificateConsoleIssuer
{
    /// <summary>The journal subject of a refusal made before any host name was known.</summary>
    private const string UnnamedPanelSubject = "panel";

    /// <summary>The Host's window onto the panel's configured host name.</summary>
    private readonly IPanelHostName _panelHostName;

    /// <summary>The one path that orders, installs and records the panel's certificate.</summary>
    private readonly PanelCertificateIssuer _issuer;

    /// <summary>This module's audit journal.</summary>
    private readonly CertificateAuditJournal _journal;

    /// <summary>Creates the service.</summary>
    /// <param name="panelHostName">The Host's window onto the configured host name.</param>
    /// <param name="issuer">The shared issuance path.</param>
    /// <param name="journal">This module's audit journal.</param>
    public PanelCertificateConsoleIssuer(
        IPanelHostName panelHostName,
        PanelCertificateIssuer issuer,
        CertificateAuditJournal journal)
    {
        _panelHostName = panelHostName;
        _issuer = issuer;
        _journal = journal;
    }

    /// <summary>Orders, installs and records a certificate for the panel's configured host name.</summary>
    /// <param name="operatorClaim">The operator's name as the console reported it (<c>SUDO_USER</c>).</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The recorded certificate, <c>PanelHostNameNotConfigured</c> when the panel has no usable host
    /// name, or the authority's or agent's own typed failure.
    /// </returns>
    public async Task<Result<PanelCertificate>> IssueAsync(string operatorClaim, CancellationToken cancellationToken)
    {
        if (!_panelHostName.TryGet(out var hostName))
        {
            await _journal.RecordConsoleAsync(
                CertificateAuditActions.CertificateIssued, UnnamedPanelSubject, operatorClaim, succeeded: false, cancellationToken);
            return Result<PanelCertificate>.Fail(
                Error.Of(nameof(ErrorMessages.PanelHostNameNotConfigured), ErrorType.Conflict));
        }

        var issued = await _issuer.IssueAsync(hostName, cancellationToken);
        await _journal.RecordConsoleAsync(
            CertificateAuditActions.CertificateIssued, hostName, operatorClaim, issued.IsSuccess, cancellationToken);

        return issued;
    }
}
