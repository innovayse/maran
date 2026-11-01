namespace Maran.Modules.Ssl.Common;

/// <summary>The trusted certificate the panel serves its own interface with.</summary>
/// <param name="HostName">The host name it was ordered for.</param>
/// <param name="ExpiresAt">When it expires.</param>
/// <param name="IssuedAt">When it was installed.</param>
public sealed record PanelCertificateDto(string HostName, DateTimeOffset ExpiresAt, DateTimeOffset IssuedAt);
