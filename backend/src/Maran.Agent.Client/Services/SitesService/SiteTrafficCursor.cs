namespace Maran.Agent.Client.Services.SitesService;

/// <summary>Where the agent's last read of a site's access log stopped, and which file that was.</summary>
/// <param name="FileId">
/// The identity of the file the offset belongs to, in the agent's own spelling. The panel stores it and
/// hands it back unread: it is a token, not a path, and nothing in the panel parses it.
/// </param>
/// <param name="Offset">The byte offset in that file where the next read starts.</param>
public sealed record SiteTrafficCursor(string FileId, ulong Offset);
