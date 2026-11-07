using Maran.Modules.Ssl.Common;
using Maran.Modules.Ssl.Domain.Entities;

namespace Maran.Modules.Ssl.Mappers;

/// <summary>Translates what the panel knows about its own certificate into the read's wire shape.</summary>
public static class PanelCertificateStatusMapper
{
    /// <summary>Restates a recorded certificate for the configured host name.</summary>
    /// <param name="certificate">The recorded panel certificate.</param>
    /// <returns>The wire shape, with a host name configured.</returns>
    public static PanelCertificateStatusDto From(PanelCertificate certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);

        return new PanelCertificateStatusDto(
            HostNameConfigured: true,
            HostNameNotice: null,
            certificate.HostName,
            certificate.ExpiresAt,
            certificate.IssuedAt);
    }

    /// <summary>Restates a panel with no usable host name.</summary>
    /// <param name="notice">The sentence, already in the caller's language, saying what to configure.</param>
    /// <returns>The wire shape, with no host name and no certificate.</returns>
    public static PanelCertificateStatusDto WithoutHostName(string notice)
    {
        ArgumentException.ThrowIfNullOrEmpty(notice);

        return new PanelCertificateStatusDto(
            HostNameConfigured: false,
            HostNameNotice: notice,
            HostName: null,
            ExpiresAt: null,
            IssuedAt: null);
    }
}
