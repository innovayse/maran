namespace Maran.Modules.Ssl.Common;

/// <summary>The self-signed certificate the panel serves its own interface with after a restore.</summary>
/// <param name="HostName">The host name it was generated for.</param>
/// <param name="ExpiresAt">When it expires.</param>
public sealed record PanelSelfSignedDto(string HostName, DateTimeOffset ExpiresAt);
