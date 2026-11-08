namespace Maran.Modules.Sites.Common;

/// <summary>How many requests of the window asked for one path.</summary>
/// <param name="Path">The request path, without its query string. Visitor-chosen text: rendered as text only.</param>
/// <param name="Requests">How many requests asked for it.</param>
public sealed record SiteTrafficPathDto(string Path, long Requests);
