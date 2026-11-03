namespace Maran.Agent.Client.Services.PhpService;

/// <summary>One PHP version the server can install, and whether it already is.</summary>
/// <param name="Version">Two-component version as the packages name it, e.g. <c>8.3</c>.</param>
/// <param name="Installed">
/// <see langword="true"/> when the version is already installed on the server. A version the host
/// cannot install is not in the list at all, so this flag is the only state the list carries.
/// </param>
public sealed record InstallablePhpVersionDto(string Version, bool Installed);
