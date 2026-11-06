namespace Maran.Modules.Ssl.Queries.GetAcmeDirectory;

/// <summary>
/// Reads which ACME authority the panel orders certificates from (issue #161).
/// </summary>
/// <remarks>Carries nothing: the answer is the operator's configuration, never a request field.</remarks>
public sealed record GetAcmeDirectoryQuery;
