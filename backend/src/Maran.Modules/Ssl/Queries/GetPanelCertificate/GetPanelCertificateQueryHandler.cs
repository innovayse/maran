using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Mappers;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Queries.GetPanelCertificate;

/// <summary>Handles <see cref="GetPanelCertificateQuery"/>.</summary>
/// <remarks>
/// <para>
/// Answers for the name the panel is configured with TODAY: a row recorded for a name the operator
/// has since changed is a certificate for a name the panel no longer answers to, and reporting it
/// would tell the operator the interface is trusted when it is not.
/// </para>
/// <para>
/// A panel with no host name is an ordinary state (it was installed by address), so it is answered
/// as content with <c>HostNameConfigured</c> false rather than refused: the browser logs every
/// 4xx a page provokes as a console error (issue #417). Ordering a certificate is still refused
/// for the same panel, by <c>IssuePanelCertificateCommandHandler</c>.
/// </para>
/// </remarks>
public sealed class GetPanelCertificateQueryHandler
{
    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The Host's window onto the panel's configured host name.</summary>
    private readonly IPanelHostName _panelHostName;

    /// <summary>The panel's one resolution from an error code to its sentence.</summary>
    private readonly IErrorTextProvider _errorTexts;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="panelHostName">The Host's window onto the configured host name.</param>
    /// <param name="errorTexts">The panel's error-message resolution, for the no-host-name sentence.</param>
    public GetPanelCertificateQueryHandler(
        SslDbContext dbContext,
        IPanelHostName panelHostName,
        IErrorTextProvider errorTexts)
    {
        _dbContext = dbContext;
        _panelHostName = panelHostName;
        _errorTexts = errorTexts;
    }

    /// <summary>Reads the recorded certificate for the configured host name.</summary>
    /// <param name="query">The query, which carries nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The recorded certificate; a status with <c>HostNameConfigured</c> false when no usable name is
    /// configured; <c>PanelCertificateNotIssued</c> when the panel still serves the installer's
    /// self-signed pair.
    /// </returns>
    public async Task<Result<PanelCertificateStatusDto>> HandleAsync(
        GetPanelCertificateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_panelHostName.TryGet(out var hostName))
        {
            return Result<PanelCertificateStatusDto>.Ok(PanelCertificateStatusMapper.WithoutHostName(
                _errorTexts.Resolve(nameof(ErrorMessages.PanelHostNameNotConfigured))));
        }

        var certificate = await _dbContext.PanelCertificates
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostName == hostName, cancellationToken);

        return certificate is null
            ? Result<PanelCertificateStatusDto>.Fail(
                Error.Of(nameof(ErrorMessages.PanelCertificateNotIssued), ErrorType.NotFound))
            : Result<PanelCertificateStatusDto>.Ok(PanelCertificateStatusMapper.From(certificate));
    }
}
