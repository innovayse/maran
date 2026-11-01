using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Domain.Entities;

namespace Maran.Modules.Ssl.Mappers;

/// <summary>Translates a <see cref="PanelCertificate"/> row into its wire shape.</summary>
public static class PanelCertificateMapper
{
    /// <summary>Restates the row on the wire.</summary>
    /// <param name="certificate">The recorded panel certificate.</param>
    /// <returns>The wire shape.</returns>
    public static PanelCertificateDto From(PanelCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return new PanelCertificateDto(certificate.HostName, certificate.ExpiresAt, certificate.IssuedAt);
    }
}
