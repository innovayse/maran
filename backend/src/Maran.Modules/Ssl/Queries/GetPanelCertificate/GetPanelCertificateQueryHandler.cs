using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Mappers;
using Maran.Modules.Ssl.Persistence;
using Maran.Modules.Ssl.Resources;
using Maran.Sdk.Interfaces;

namespace Maran.Modules.Ssl.Queries.GetPanelCertificate;

/// <summary>Handles <see cref="GetPanelCertificateQuery"/>.</summary>
/// <remarks>
/// Answers for the name the panel is configured with TODAY: a row recorded for a name the operator
/// has since changed is a certificate for a name the panel no longer answers to, and reporting it
/// would tell the operator the interface is trusted when it is not.
/// </remarks>
public sealed class GetPanelCertificateQueryHandler
{
    /// <summary>The Ssl module's database context.</summary>
    private readonly SslDbContext _dbContext;

    /// <summary>The Host's window onto the panel's configured host name.</summary>
    private readonly IPanelHostName _panelHostName;

    /// <summary>Creates the handler.</summary>
    /// <param name="dbContext">The Ssl module's database context.</param>
    /// <param name="panelHostName">The Host's window onto the configured host name.</param>
    public GetPanelCertificateQueryHandler(SslDbContext dbContext, IPanelHostName panelHostName)
    {
        _dbContext = dbContext;
        _panelHostName = panelHostName;
    }

    /// <summary>Reads the recorded certificate for the configured host name.</summary>
    /// <param name="query">The query, which carries nothing.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>
    /// The recorded certificate; <c>PanelHostNameNotConfigured</c> when no usable name is configured;
    /// <c>PanelCertificateNotIssued</c> when the panel still serves the installer's self-signed pair.
    /// </returns>
    public async Task<Result<PanelCertificateDto>> HandleAsync(
        GetPanelCertificateQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!_panelHostName.TryGet(out var hostName))
        {
            return Result<PanelCertificateDto>.Fail(
                Error.Of(nameof(ErrorMessages.PanelHostNameNotConfigured), ErrorType.Conflict));
        }

        var certificate = await _dbContext.PanelCertificates
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.HostName == hostName, cancellationToken);

        return certificate is null
            ? Result<PanelCertificateDto>.Fail(
                Error.Of(nameof(ErrorMessages.PanelCertificateNotIssued), ErrorType.NotFound))
            : Result<PanelCertificateDto>.Ok(PanelCertificateMapper.From(certificate));
    }
}
