namespace Maran.Modules.Ssl.Common;

/// <summary>
/// What <c>GET /api/v1/panel-certificate</c> reports: whether the panel has a host name a certificate
/// can be ordered for, and the trusted certificate recorded for it.
/// </summary>
/// <param name="HostNameConfigured">
/// Whether the panel is configured with a usable host name. <c>false</c> on a panel installed by
/// address: an ordinary state, not a failure, so it is answered 200 and the fields below are
/// <c>null</c>.
/// </param>
/// <param name="HostNameNotice">
/// The sentence, in the caller's language, saying what to configure; <c>null</c> when
/// <paramref name="HostNameConfigured"/> is <c>true</c>.
/// </param>
/// <param name="HostName">The host name the certificate was ordered for; <c>null</c> without a host name.</param>
/// <param name="ExpiresAt">When it expires; <c>null</c> without a host name.</param>
/// <param name="IssuedAt">When it was installed; <c>null</c> without a host name.</param>
public sealed record PanelCertificateStatusDto(
    bool HostNameConfigured,
    string? HostNameNotice,
    string? HostName,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? IssuedAt);
