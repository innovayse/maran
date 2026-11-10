namespace Maran.Modules.Marketplace.Models;

/// <summary>The body of the update check: the channel and the installed modules, nothing else.</summary>
/// <param name="Channel">The release channel.</param>
/// <param name="Installed">The installed modules.</param>
public sealed record UpdatesRequest(string Channel, IReadOnlyList<InstalledModuleReference> Installed);
