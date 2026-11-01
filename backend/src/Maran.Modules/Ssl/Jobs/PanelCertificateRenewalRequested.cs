namespace Maran.Modules.Ssl.Jobs;

/// <summary>
/// The scheduled trigger for the panel's own certificate renewal (issue #132), published by the Host
/// beside <see cref="CertificateRenewalRequested"/>. Its own message, so the panel's pair is renewed
/// by its own handler with its own transaction rather than inside a pass over every site.
/// </summary>
public sealed record PanelCertificateRenewalRequested;
