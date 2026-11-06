namespace Maran.Modules.Ssl.Common;

/// <summary>
/// The ACME authority this panel orders certificates from, as an administrator sees it. Read only: the
/// URL is the operator's configuration, and no request field changes it.
/// </summary>
/// <param name="Url">The configured directory URL.</param>
/// <param name="Host">The host name of the URL; empty when the URL carries none.</param>
/// <param name="Kind">The machine value <c>production</c>, <c>staging</c> or <c>custom</c>.</param>
/// <param name="KindName">The kind, named in the caller's language.</param>
public sealed record AcmeDirectoryDto(string Url, string Host, string Kind, string KindName);
