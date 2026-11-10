namespace Maran.Modules.Marketplace.Models;

/// <summary>
/// One installed module as the update check names it. These three values are all the service learns
/// about what is installed: no host name, account or site ever accompanies them.
/// </summary>
/// <param name="Id">The module id.</param>
/// <param name="Version">The installed version.</param>
/// <param name="Sha256">The installed package's SHA-256, hexadecimal.</param>
public sealed record InstalledModuleReference(string Id, string Version, string Sha256);
